using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using PoolCar.Reservations.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ReservationsDbContext>(
    "reservations",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());
builder.Services.AddScoped<ReservationStore>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/ping", () => TypedResults.Ok("pong"));

app.MapDefaultEndpoints();

await app.RunAsync();
