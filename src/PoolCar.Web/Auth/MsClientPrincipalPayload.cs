using System.Text.Json.Serialization;

namespace PoolCar.Web.Auth;

public sealed class MsClientPrincipalPayload
{
    [JsonPropertyName("auth_typ")]
    public string? AuthenticationType { get; set; }

    [JsonPropertyName("name_typ")]
    public string? NameType { get; set; }

    [JsonPropertyName("role_typ")]
    public string? RoleType { get; set; }

    [JsonPropertyName("claims")]
    public List<MsClientClaim>? Claims { get; set; }
}

public sealed class MsClientClaim
{
    [JsonPropertyName("typ")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("val")]
    public string Value { get; set; } = string.Empty;
}
