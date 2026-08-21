using System.Text.Json.Serialization;

namespace AmazonRepricer.Infrastructure.Amazon;

internal sealed class ListingsItemSubmissionResponse
{
    [JsonPropertyName("sku")]
    public string Sku { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("submissionId")]
    public string SubmissionId { get; set; } = string.Empty;

    [JsonPropertyName("issues")]
    public List<ListingsItemIssue> Issues { get; set; } = [];
}

internal sealed class ListingsItemIssue
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;
}
