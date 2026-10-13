namespace PoolCar.Web.Tests.Support;

public static class FleetData
{
    public const string CategoriesJson = """
        [{"id":"middel","name":"Middel","description":"Gezinsauto","sortOrder":2},
         {"id":"klein","name":"Klein","description":"Compact","sortOrder":1},
         {"id":"luxe","name":"Luxe","description":"Comfort","sortOrder":3}]
        """;

    public const string UtrechtCarsJson = """
        [{"plate":"AA11BB","make":"Renault","model":"5","variant":"E-Tech","category":"klein","office":"utrecht",
          "colour":"Geel","seats":4,"rangeKm":400,"powerKw":110,"firstRegistration":"2025-01-01","listPrice":null},
         {"plate":"CC22DD","make":"Peugeot","model":"e-208","variant":"","category":"klein","office":"utrecht",
          "colour":"Blauw","seats":5,"rangeKm":350,"powerKw":100,"firstRegistration":"2024-01-01","listPrice":null},
         {"plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel","office":"utrecht",
          "colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,"firstRegistration":"2024-03-01","listPrice":null}]
        """;

    public static StubHttpHandler Fleet() => new StubHttpHandler()
        .OnJson(HttpMethod.Get, "/categories", CategoriesJson)
        .OnJson(HttpMethod.Get, "/cars", UtrechtCarsJson);
}
