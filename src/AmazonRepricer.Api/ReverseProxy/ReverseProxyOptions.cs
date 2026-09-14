namespace AmazonRepricer.Api.ReverseProxy;

public sealed class ReverseProxyOptions
{
    public const string SectionName =
        "ReverseProxy";

    public bool Enabled { get; init; }

    public List<string> KnownProxies { get; init; } = [];
}
