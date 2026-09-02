using SmsWorkbench;

namespace SmsWorkbench.Tests;

public sealed class AccountPaymentFilterTests
{
    [Fact]
    public void RegistrationDefaultsToEnrollingTotp()
    {
        Assert.False(new RegisterOptions().Disable2fa);
    }

    [Fact]
    public void TrialAndPaymentMethodFiltersUseAndSemantics()
    {
        var row = new PoolRow
        {
            PromotionStatus = "Có thể dùng thử Plus · 0 đ",
            PaymentMethodBadges = new[] { "Trial · 0 đ", "Card", "Apple Pay", "MoMo" },
        };

        Assert.True(AccountPaymentFilter.MatchesAll(row, new[] { "trial", "card", "momo" }));
        Assert.False(AccountPaymentFilter.MatchesAll(row, new[] { "trial", "gpay" }));
        Assert.False(AccountPaymentFilter.Matches(row, "no_offer"));
    }

    [Fact]
    public void NoOfferAndFriendlyAliasesMatchDisplayedBadges()
    {
        var row = new PoolRow
        {
            PromotionStatus = "Không có ưu đãi 0 đ",
            PaymentMethodBadges = new[] { "Card", "Google Pay", "Kakao Pay", "Naver Pay" },
        };

        Assert.True(AccountPaymentFilter.MatchesAll(
            row,
            new[] { "no_offer", "gpay", "kakao", "naver" }));
        Assert.False(AccountPaymentFilter.Matches(row, "trial"));
    }

    [Fact]
    public void ErrorAndUncheckedFiltersRemainDistinct()
    {
        var failed = new PoolRow { PromotionStatus = "Kiểm tra thất bại" };
        var uncheckedRow = new PoolRow();

        Assert.True(AccountPaymentFilter.Matches(failed, "payment_error"));
        Assert.False(AccountPaymentFilter.Matches(failed, "unchecked"));
        Assert.True(AccountPaymentFilter.Matches(uncheckedRow, "unchecked"));
        Assert.False(AccountPaymentFilter.Matches(uncheckedRow, "payment_error"));
    }

    [Fact]
    public void UnknownAmountIsNotMisclassifiedAsNoOfferOrUnchecked()
    {
        var row = new PoolRow
        {
            PaymentCheckStatus = "completed",
            OfferState = "unknown_amount",
            PaymentCheckedAt = 1_788_240_000,
            PaymentMethodBadges = new[] { "Card", "Apple Pay" },
        };

        Assert.False(AccountPaymentFilter.Matches(row, "no_offer"));
        Assert.False(AccountPaymentFilter.Matches(row, "unchecked"));
        Assert.True(AccountPaymentFilter.Matches(row, "card"));
    }

    [Fact]
    public void StructuredOfferErrorAndSkippedStatesDriveStatusChips()
    {
        var noOffer = new PoolRow { PaymentCheckStatus = "completed", OfferState = "nonzero_due" };
        var failed = new PoolRow { PaymentCheckStatus = "failed", PaymentCheckError = "checkout blocked" };
        var skipped = new PoolRow
        {
            PaymentCheckStatus = "skipped",
            PaymentCheckError = "payment-method detection is disabled",
        };

        Assert.True(AccountPaymentFilter.Matches(noOffer, "no_offer"));
        Assert.True(AccountPaymentFilter.Matches(failed, "payment_error"));
        Assert.True(AccountPaymentFilter.Matches(skipped, "unchecked"));
        Assert.False(AccountPaymentFilter.Matches(skipped, "payment_error"));
    }
}
