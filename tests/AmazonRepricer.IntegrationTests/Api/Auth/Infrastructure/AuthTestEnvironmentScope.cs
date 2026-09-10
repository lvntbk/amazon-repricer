namespace AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;

internal sealed class AuthTestEnvironmentScope : IDisposable
{
    public const string DefaultIssuer =
        "AmazonRepricer.IntegrationTests";

    public const string DefaultAudience =
        "AmazonRepricer.IntegrationTests";

    public const string DefaultSigningKey =
        "integration-test-signing-key-32-bytes-minimum";

    private const string ConnectionStringVariable =
        "ConnectionStrings__DefaultConnection";

    private const string IssuerVariable =
        "Jwt__Issuer";

    private const string AudienceVariable =
        "Jwt__Audience";

    private const string SigningKeyVariable =
        "Jwt__SigningKey";

    private readonly Dictionary<string, string?> _originalValues =
        new(StringComparer.Ordinal);

    private bool _disposed;

    private AuthTestEnvironmentScope(
        IReadOnlyDictionary<string, string?> variables)
    {
        foreach (var variable in variables)
        {
            _originalValues[variable.Key] =
                Environment.GetEnvironmentVariable(
                    variable.Key);

            Environment.SetEnvironmentVariable(
                variable.Key,
                variable.Value);
        }
    }

    public static AuthTestEnvironmentScope CreateDefault(
        string connectionString,
        params (string Name, string? Value)[] overrides)
    {
        var variables =
            new Dictionary<string, string?>(
                StringComparer.Ordinal)
            {
                [ConnectionStringVariable] =
                    connectionString,

                [IssuerVariable] =
                    DefaultIssuer,

                [AudienceVariable] =
                    DefaultAudience,

                [SigningKeyVariable] =
                    DefaultSigningKey
            };

        foreach (var (name, value) in overrides)
        {
            variables[name] = value;
        }

        return new AuthTestEnvironmentScope(
            variables);
    }

    public static AuthTestEnvironmentScope Capture(params string[] names)
    {
        var variables = names.ToDictionary(
            name => name,
            _ => (string?)null);

        var scope = new AuthTestEnvironmentScope(
            new Dictionary<string, string?>());

        foreach (var name in names)
        {
            scope._originalValues[name] =
                Environment.GetEnvironmentVariable(name);
        }

        return scope;
    }

    public void Set(
        string name,
        string? value)
    {
        if (!_originalValues.ContainsKey(name))
        {
            _originalValues[name] =
                Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(
            name,
            value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var variable in _originalValues)
        {
            Environment.SetEnvironmentVariable(
                variable.Key,
                variable.Value);
        }

        _disposed = true;
    }
}
