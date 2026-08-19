using Appizza.Modules.Promotions;

namespace Appizza.UnitTests;

public sealed class Phase6PromotionRulesTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static PromotionCandidate P(string kind, string scope, decimal value, int priority = 0, Guid? id = null, params Guid[] products) => new(id ?? Guid.NewGuid(), Guid.NewGuid(), "Promo", kind, scope, value, priority, products.ToHashSet());
    [Fact] public void PercentageEntireOrder() => Assert.Equal(10m, PromotionRules.Select([P(PromotionKinds.Percentage, PromotionScopes.EntireOrder, 10)], [new(A, 100)])!.DiscountAmount);
    [Fact] public void PercentageSpecificProductsExcludesOthers() => Assert.Equal(10m, PromotionRules.Select([P(PromotionKinds.Percentage, PromotionScopes.SpecificProducts, 10, products: A)], [new(A, 100), new(B, 200)])!.DiscountAmount);
    [Fact] public void FixedAmountIsAppliedOnceAndCapped() => Assert.Equal(100m, PromotionRules.Select([P(PromotionKinds.FixedAmount, PromotionScopes.EntireOrder, 150)], [new(A, 100)])!.DiscountAmount);
    [Fact] public void FixedSpecificProductsUsesOnlyEligibleBase() => Assert.Equal(10m, PromotionRules.Select([P(PromotionKinds.FixedAmount, PromotionScopes.SpecificProducts, 20, products: A)], [new(A, 10), new(B, 100)])!.DiscountAmount);
    [Fact] public void PercentageRoundsToCent() => Assert.Equal(1.01m, PromotionRules.Select([P(PromotionKinds.Percentage, PromotionScopes.EntireOrder, 10)], [new(A, 10.05m)])!.DiscountAmount);
    [Fact] public void HighestFinancialBenefitWinsWithoutAccumulation() { var result = PromotionRules.Select([P(PromotionKinds.Percentage, PromotionScopes.EntireOrder, 10), P(PromotionKinds.FixedAmount, PromotionScopes.EntireOrder, 20)], [new(A, 100)]); Assert.Equal(20m, result!.DiscountAmount); }
    [Fact] public void PriorityBreaksEqualBenefit() { var low=P(PromotionKinds.FixedAmount,PromotionScopes.EntireOrder,10,1); var high=P(PromotionKinds.FixedAmount,PromotionScopes.EntireOrder,10,2); Assert.Equal(high.PromotionId, PromotionRules.Select([low,high],[new(A,100)])!.PromotionId); }
    [Fact] public void IdentifierBreaksEqualBenefitAndPriority() { var a=P(PromotionKinds.FixedAmount,PromotionScopes.EntireOrder,10,1,A); var b=P(PromotionKinds.FixedAmount,PromotionScopes.EntireOrder,10,1,B); Assert.Equal(A, PromotionRules.Select([b,a],[new(A,100)])!.PromotionId); }
    [Fact] public void EmptyOrIneligibleBaseDoesNotApply() => Assert.Null(PromotionRules.Select([P(PromotionKinds.Percentage,PromotionScopes.SpecificProducts,10,products:A)],[new(B,100)]));
    [Fact] public void DiscountNeverMakesTotalNegative() => Assert.Equal(5m, PromotionRules.Select([P(PromotionKinds.FixedAmount,PromotionScopes.EntireOrder,50)],[new(A,5)])!.DiscountAmount);
    [Fact] public void FutureOrInactiveCandidatesAreFilteredBeforeRules() { var candidates = Array.Empty<PromotionCandidate>(); Assert.Null(PromotionRules.Select(candidates,[new(A,10)])); }
    [Fact] public void ProductIdsAreExplicitInCandidate() { var result=PromotionRules.Select([P(PromotionKinds.FixedAmount,PromotionScopes.SpecificProducts,7,products:A)],[new(A,10),new(B,10)]); Assert.Equal(7m,result!.DiscountAmount); Assert.DoesNotContain(B, System.Text.Json.JsonSerializer.Deserialize<Guid[]>(result.ProductIds)!); }
}
