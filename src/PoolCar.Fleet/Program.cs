using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();

app.MapGet("/ping", () => TypedResults.Ok("pong"));

app.MapDefaultEndpoints();

app.Run();
