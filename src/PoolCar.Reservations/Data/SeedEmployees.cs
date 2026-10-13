namespace PoolCar.Reservations.Data;

/// <summary>The fictional employees that are created when the database is empty.</summary>
public static class SeedEmployees
{
    public static readonly IReadOnlyList<SeedEmployee> All =
    [
        new(Guid.Parse("f0671165-c417-421a-9eac-649e8534de6e"), "Bram Testbeek", "utrecht"),
        new(Guid.Parse("2c35f465-5839-4fc9-9bd6-1c56ee4f174f"), "Fleur Proefhuis", "amsterdam"),
        new(Guid.Parse("fa91db2c-71ec-4752-beb9-4dcf1e2e58d4"), "Daan Voorbeeld", "eindhoven"),
        new(Guid.Parse("95a818bf-a2cb-428e-9082-aca81970cebe"), "Sanne Monsterman", "utrecht"),
        new(Guid.Parse("99bbc113-d5f8-43b6-bfac-c8da77bb6f4d"), "Jesse Proefdorp", "amsterdam"),
        new(Guid.Parse("7176c9a1-bded-429c-9803-fbbb150ce973"), "Lotte Teststra", "eindhoven"),
        new(Guid.Parse("ff328fbb-779a-40fe-b362-7cf0d976063f"), "Thijs Voorproef", "utrecht"),
        new(Guid.Parse("bfa10c9f-be1b-4c43-80e0-4e5c1513c75d"), "Eline Prototype", "amsterdam"),
        new(Guid.Parse("37274162-1c4f-4ece-9a44-05d929230edf"), "Sem Proefkamp", "eindhoven"),
        new(Guid.Parse("db3d16aa-df40-4d85-8e7c-32b226054d8b"), "Noor Testveld", "utrecht"),
        new(Guid.Parse("8c0a6c3b-4081-445b-b5b7-d674489293a7"), "Ruben Nepman", "amsterdam"),
        new(Guid.Parse("34e6ed63-537d-4c98-9c26-eb78440f90d0"), "Iris Proefberg", "eindhoven"),
        new(Guid.Parse("d59f6659-147e-4bdb-8dfc-be83e44502e9"), "Milan Testhoek", "utrecht"),
        new(Guid.Parse("5c2dee50-285b-4282-bfca-d96b4d8b0452"), "Lieke Voorbeeldt", "amsterdam"),
        new(Guid.Parse("44582378-b1fc-4d7d-92d4-edb4b3f012c1"), "Finn Proefstad", "eindhoven"),
        new(Guid.Parse("3d4d398d-bd5f-4240-b0ae-ad746fe738d6"), "Maud Testwijk", "utrecht"),
        new(Guid.Parse("700403c8-d019-44b1-9da5-7ed068a7e433"), "Luuk Schijnbeek", "amsterdam"),
        new(Guid.Parse("f5c1d213-ae89-45bf-a258-98296ab495fb"), "Anouk Proefsloot", "eindhoven"),
        new(Guid.Parse("218b039c-9bb5-4db5-9bee-aeb1deb792f2"), "Tim Testerman", "utrecht"),
        new(Guid.Parse("53d2cccb-4c78-4f2b-ab54-a6041f73dc51"), "Roos Proefwerk", "amsterdam"),
        new(Guid.Parse("8b5d3551-94ad-4d55-a2d1-9a9a18da9643"), "Joris Testdijk", "eindhoven"),
        new(Guid.Parse("b15f3749-75a0-47ba-91e0-af991ee42d35"), "Femke Voorproeve", "utrecht"),
        new(Guid.Parse("be178337-87a1-4993-b31a-87a0cf132f86"), "Gijs Proefhof", "amsterdam"),
        new(Guid.Parse("689c4bfe-7e1c-47e7-8e96-9b87817a6ad5"), "Merel Testlaan", "eindhoven"),
        new(Guid.Parse("d0e2a4ca-8d0b-4f24-9806-655df3a0940e"), "Niels Schijnhuis", "utrecht"),
        new(Guid.Parse("6b014294-7333-4877-ba53-91efe7fa2771"), "Eva Proefmeer", "amsterdam"),
        new(Guid.Parse("f684a559-4681-4ffe-96d0-59c7b52f0973"), "Stijn Testwaard", "eindhoven"),
        new(Guid.Parse("a1e9ba5f-abd0-4dd0-90db-bdf96ff4b0a9"), "Julia Proefkoop", "utrecht"),
        new(Guid.Parse("e3999bac-7566-4bab-be20-7d88cc2efc9c"), "Koen Testland", "amsterdam"),
        new(Guid.Parse("a3d4ae11-ca0d-4fe5-b4b7-651abc9dfeb9"), "Yara Voorbeeldveld", "eindhoven"),
    ];
}

public sealed record SeedEmployee(Guid Id, string Name, string HomeOffice);
