using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Domain.Entities;

public sealed class PricingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public PricingStrategy Strategy { get; set; }

    public decimal MinimumPrice { get; set; }

    public decimal MaximumPrice { get; set; }

    public decimal AdjustmentValue { get; set; }

    public decimal? MinimumProfitPercentage { get; set; }

    public bool IsActive { get; set; } = true;

    public Product Product { get; set; } = null!;
}
