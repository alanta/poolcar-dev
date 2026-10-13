using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Onboarding;

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

builder.Services.AddScoped<CurrentEmployee>();

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

// Add services to the container and require authentication on all pages
builder.Services.AddRazorPages(options =>
    {
        options.Conventions.AuthorizeFolder("/");
    })
    .AddMvcOptions(options => options.Filters.Add<OnboardingPageFilter>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

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
