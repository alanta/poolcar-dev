using Microsoft.Extensions.Hosting;
using PoolCar.Fleet.Domain;
using PoolCar.Fleet.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(FleetCatalog.LoadEmbedded());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/ping", () => TypedResults.Ok("pong"));

app.MapOfficeEndpoints();
app.MapCategoryEndpoints();
app.MapCarEndpoints();

app.MapDefaultEndpoints();

app.Run();

public partial class Program;
