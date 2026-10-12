using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17")
    .WithDataVolume();
var reservationsDb = postgres.AddDatabase("reservationsdb");

var fleet = builder.AddProject<Projects.PoolCar_Fleet>("fleet")
    .WithHttpHealthCheck("/health");
var reservations = builder.AddProject<Projects.PoolCar_Reservations>("reservations")
    .WithReference(reservationsDb)
    .WithHttpHealthCheck("/health")
    .WaitFor(reservationsDb);
var web = builder.AddProject<Projects.PoolCar_Web>("web")
    .WithReference(fleet)
    .WithReference(reservations)
    .WithEnvironment("FleetApi__BaseAddress", fleet.GetEndpoint("http"))
    .WithEnvironment("ReservationsApi__BaseAddress", reservations.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WaitFor(fleet)
    .WaitFor(reservations);
var simulator = builder.AddProject<Projects.PoolCar_Simulator>("simulator")
    .WithReference(fleet)
    .WithReference(reservations)
    .WithEnvironment("FleetApi__BaseAddress", fleet.GetEndpoint("http"))
    .WithEnvironment("ReservationsApi__BaseAddress", reservations.GetEndpoint("http"))
    .WaitFor(fleet)
    .WaitFor(reservations);

if (!builder.Configuration.GetValue<bool>("Simulator:Enabled"))
{
    simulator.WithExplicitStart();
}

builder.AddEasyAuthProxy("easyauth")
    .WithBackend(web)
    .WithHostPort(5000);

builder.Build().Run();
