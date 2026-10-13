namespace PoolCar.Web.Auth;

public static class PoolCarPolicies
{
    /// <summary>Requires the <see cref="PoolCarRoles.FleetOperator"/> role.</summary>
    public const string Operator = "Operator";
}
