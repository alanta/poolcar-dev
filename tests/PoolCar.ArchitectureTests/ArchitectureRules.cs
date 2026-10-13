using ArchUnitNET.Domain;
using ClrAssembly = System.Reflection.Assembly;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Shouldly;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PoolCar.ArchitectureTests;

public class ArchitectureRules
{
    private static readonly ClrAssembly[] ServiceAssemblies =
    [
        ClrAssembly.Load("PoolCar.Fleet"),
        ClrAssembly.Load("PoolCar.Reservations"),
        ClrAssembly.Load("PoolCar.Web"),
    ];

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(ServiceAssemblies)
        .Build();

    private const string DomainTypes = @"^PoolCar\.(Fleet|Reservations)\.Domain\.";

    private const string ForbiddenDependencies =
        @"^(Microsoft\.EntityFrameworkCore|Npgsql|Microsoft\.AspNetCore)\.|^PoolCar\.(Fleet|Reservations)\.(Data|Endpoints)\.";

    [Fact]
    public void Domain_does_not_depend_on_frameworks_or_outer_layers()
    {
        Types().That().HaveFullNameMatching(DomainTypes)
            .Should().NotDependOnAnyTypesThat().HaveFullNameMatching(ForbiddenDependencies)
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    [Fact]
    public void Fleet_domain_is_covered_by_the_domain_rule()
    {
        Types().That().HaveFullNameMatching(DomainTypes).GetObjects(Architecture)
            .ShouldContain(t => t.Namespace.FullName == "PoolCar.Fleet.Domain");
    }

    [Fact]
    public void Web_does_not_reference_api_projects()
    {
        var referenced = ClrAssembly.Load("PoolCar.Web").GetReferencedAssemblies().Select(a => a.Name);

        referenced.ShouldNotContain("PoolCar.Fleet");
        referenced.ShouldNotContain("PoolCar.Reservations");

        Types().That().HaveFullNameMatching(@"^PoolCar\.Web\.")
            .Should().NotDependOnAnyTypesThat().HaveFullNameMatching(@"^PoolCar\.(Fleet|Reservations)\.")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }
}
