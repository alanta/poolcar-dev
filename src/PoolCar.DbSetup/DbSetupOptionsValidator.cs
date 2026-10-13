using Microsoft.Extensions.Options;

namespace PoolCar.DbSetup;

public sealed class DbSetupOptionsValidator : IValidateOptions<DbSetupOptions>
{
    public ValidateOptionsResult Validate(string? name, DbSetupOptions options)
    {
        var failures = new List<string>();

        if (Uri.CheckHostName(options.Host) != UriHostNameType.Dns)
        {
            failures.Add($"{DbSetupOptions.Section}:{nameof(options.Host)} must be a host name.");
        }

        AddNameFailure(failures, nameof(options.AdminUser), options.AdminUser);
        AddNameFailure(failures, nameof(options.Database), options.Database);
        AddNameFailure(failures, nameof(options.Principal), options.Principal);

        if (!Guid.TryParse(options.PrincipalObjectId, out _))
        {
            failures.Add($"{DbSetupOptions.Section}:{nameof(options.PrincipalObjectId)} must be a GUID.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddNameFailure(List<string> failures, string setting, string value)
    {
        if (!Identifier.IsValid(value))
        {
            failures.Add($"{DbSetupOptions.Section}:{setting} must match {Identifier.Pattern}.");
        }
    }
}
