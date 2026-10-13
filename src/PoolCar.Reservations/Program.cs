using Azure.Identity;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ReservationsDbContext>(
    "reservations",
    configureDbContextOptions: options => options
        .UseSnakeCaseNamingConvention()
        .UseNpgsql(npgsql => npgsql.ConfigureDataSource(dataSource =>
            dataSource.UseEntraIdWhenPasswordless(() => new DefaultAzureCredential()))));
builder.Services.AddScoped<ReservationStore>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExclusionViolationHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

// In Azure the database setup job migrates and seeds the database instead.
if (app.Configuration.GetValue<bool>("Database:Migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(
        db,
        scope.ServiceProvider.GetRequiredService<TimeProvider>(),
        app.Configuration.GetValue<bool>("Seed:Reservations"));
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/ping", () => TypedResults.Ok("pong"));

app.MapEmployeeEndpoints();
app.MapReservationEndpoints();
app.MapPeriodEndpoints();
app.MapAvailabilityEndpoints();
app.MapScheduleEndpoints();

app.MapDefaultEndpoints();

await app.RunAsync();
