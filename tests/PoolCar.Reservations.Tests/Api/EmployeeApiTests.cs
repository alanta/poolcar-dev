using System.Net;
using System.Net.Http.Json;
using PoolCar.Reservations.Domain;
using PoolCar.Reservations.Endpoints;

namespace PoolCar.Reservations.Tests.Api;

public class EmployeeApiTests(ReservationsApiFactory api) : IClassFixture<ReservationsApiFactory>
{
    private readonly HttpClient _client = api.CreateClient();

    [Fact]
    public async Task Saving_a_new_employee_creates_a_signed_in_employee()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var response = await _client.PutAsJsonAsync(
            $"/employees/{id}", new EmployeeRequest(" Sanne de Vries ", "utrecht"), ReservationsApiFactory.Json, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var employee = await response.ReadAsync<EmployeeResponse>();
        employee.ShouldBe(new EmployeeResponse(id, "Sanne de Vries", "utrecht", EmployeeKind.SignedIn));
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain("\"kind\":\"SignedIn\"");
    }

    [Fact]
    public async Task Saving_an_existing_employee_updates_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await api.AddEmployeeAsync();

        var response = await _client.PutAsJsonAsync(
            $"/employees/{id}", new EmployeeRequest("Sanne Jansen", "amsterdam"), ReservationsApiFactory.Json, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var employee = await _client.GetFromJsonAsync<EmployeeResponse>(
            $"/employees/{id}", ReservationsApiFactory.Json, ct);
        employee.ShouldBe(new EmployeeResponse(id, "Sanne Jansen", "amsterdam", EmployeeKind.SignedIn));
    }

    [Fact]
    public async Task Saving_an_employee_without_a_name_is_invalid()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PutAsJsonAsync(
            $"/employees/{Guid.NewGuid()}", new EmployeeRequest(" ", "utrecht"), ReservationsApiFactory.Json, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.Code().ShouldBe(DomainError.InvalidCode);
        problem.GetProperty("detail").GetString().ShouldBe("Vul je naam in.");
    }

    [Fact]
    public async Task Getting_an_unknown_employee_returns_not_found()
    {
        var response = await _client.GetAsync($"/employees/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code().ShouldBe(ApiProblems.NotFoundCode);
    }
}
