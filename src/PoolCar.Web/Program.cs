using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PoolCar.Web.Auth;
using PoolCar.Web.Availability;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.ExpiredForms;
using PoolCar.Web.Offices;
using PoolCar.Web.Onboarding;
using PoolCar.Web.Outages;
using PoolCar.Web.Schedule;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Add typed API options and clients
builder.Services.AddOptions<FleetApiOptions>()
    .BindConfiguration("FleetApi")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ReservationsApiOptions>()
    .BindConfiguration("ReservationsApi")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<FleetApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<FleetApiOptions>>().Value;
    client.BaseAddress = options.BaseAddress;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ActingUserHandler>();
builder.Services.AddHttpClient<ReservationsApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ReservationsApiOptions>>().Value;
        client.BaseAddress = options.BaseAddress;
    })
    .AddHttpMessageHandler<ActingUserHandler>();

// A retried write could store a booking twice, so API clients only retry reads
builder.Services.ConfigureAll<HttpStandardResilienceOptions>(options => options.Retry.DisableForUnsafeHttpMethods());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CurrentEmployee>();
builder.Services.AddScoped<OfficeDirectory>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<ScheduleService>();

// Configure EasyAuth as default authentication scheme
builder.Services.AddAuthentication(EasyAuthAuthenticationDefaults.AuthenticationScheme)
    .AddEasyAuth();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PoolCarPolicies.Operator, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(EasyAuthClaimTypes.Roles, PoolCarRoles.FleetOperator));

var dutchCulture = new CultureInfo("nl-NL");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(dutchCulture);
    options.SupportedCultures = [dutchCulture];
    options.SupportedUICultures = [dutchCulture];
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// The data protection keys are not persisted, so after a restart tokens from pages opened before cannot be decrypted.
// Antiforgery logs that as an error with an exception, also when it silently replaces the cookie of a page load.
// ExpiredFormFilter logs a failed validation as a warning instead and asks the user to reload the page.
builder.Logging.AddFilter("Microsoft.AspNetCore.Antiforgery", LogLevel.Critical);

// Add services to the container and require authentication on all pages
builder.Services.AddRazorPages(options =>
    {
        options.Conventions.AuthorizeFolder("/");
    })
    .AddMvcOptions(options =>
    {
        options.Filters.Add<OnboardingPageFilter>();
        options.Filters.Add<ExpiredFormFilter>();
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/fout");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Show the Dutch error page for error status codes without a body, such as 404 and 403
app.UseStatusCodePagesWithReExecute("/fout/{0}");

// Show which part is unavailable when the Fleet or Reservations API cannot be reached
app.UseMiddleware<ServiceUnavailableMiddleware>();

app.UseRequestLocalization();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapDefaultEndpoints();

app.Run();

public partial class Program;
