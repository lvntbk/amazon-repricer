using System.Text.Json.Serialization;

namespace AmazonRepricer.Infrastructure.Amazon.Sellers;

internal sealed class AmazonMarketplaceParticipationsResponse
{
    [JsonPropertyName("payload")]
    public List<AmazonMarketplaceParticipation> Payload { get; set; } = [];
}
