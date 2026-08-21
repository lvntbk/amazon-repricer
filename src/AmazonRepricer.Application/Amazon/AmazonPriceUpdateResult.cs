namespace AmazonRepricer.Application.Amazon;

public sealed record AmazonPriceUpdateResult(
    bool Accepted,
    string? SubmissionId,
    IReadOnlyList<string> Issues);
