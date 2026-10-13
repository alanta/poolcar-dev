using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ReservationsDbContext>(
    "reservations",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());
builder.Services.AddScoped<ReservationStore>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExclusionViolationHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
    await db.Database.MigrateAsync();
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
