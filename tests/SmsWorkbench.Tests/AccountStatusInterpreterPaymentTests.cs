using SmsWorkbench;

namespace SmsWorkbench.Tests;

public sealed class AccountStatusInterpreterPaymentTests
{
    [Fact]
    public void PaymentBadgesReadPersistedFriendlyBadges()
    {
        const string rawJson = """
        {
          "payment_method_badges": ["Trial · 0 đ", "Card", "Apple Pay", "MoMo"]
        }
        """;

        IReadOnlyList<string> badges = AccountStatusInterpreter.GetPaymentMethodBadges(
            new Dictionary<string, object>(), rawJson);

        Assert.Equal(new[] { "Trial · 0 đ", "Card", "Apple Pay", "MoMo" }, badges);
    }

    [Fact]
    public void PaymentBadgesAreDerivedFromCapabilityEvidence()
    {
        const string rawJson = """
        {
          "payment_capability": {
            "amount_minor": 0,
            "currency": "PHP",
            "offer_state": "zero_due",
            "payment_method_types": ["card", "google_pay", "custom_payment_method"],
            "custom_payment_methods": ["gcash"]
          }
        }
        """;

        IReadOnlyList<string> badges = AccountStatusInterpreter.GetPaymentMethodBadges(
            new Dictionary<string, object>(), rawJson);

        Assert.Equal(new[] { "Trial · 0 PHP", "Card", "Google Pay", "GCash" }, badges);
    }

    [Fact]
    public void PaymentBadgesAreDerivedFromTopLevelPaymentTypes()
    {
        const string rawJson = """
        {
          "amount_due": 0,
          "currency": "VND",
          "payment_method_types": ["card", "link", "kakao_pay"]
        }
        """;

        IReadOnlyList<string> badges = AccountStatusInterpreter.GetPaymentMethodBadges(
            new Dictionary<string, object>(), rawJson);

        Assert.Equal(new[] { "Trial · 0 đ", "Card", "Link", "Kakao Pay" }, badges);
    }

    [Fact]
    public void PaymentCheckStateReadsStructuredCapabilityFields()
    {
        const string rawJson = """
        {
          "payment_check_status": "completed",
          "payment_checked_at": 1788240000,
          "payment_capability": {
            "status": "completed",
            "offer_state": "unknown_amount",
            "error": ""
          }
        }
        """;

        AccountPaymentCheckState state = AccountStatusInterpreter.GetPaymentCheckState(
            new Dictionary<string, object>(), rawJson);

        Assert.Equal("completed", state.Status);
        Assert.Equal("unknown_amount", state.OfferState);
        Assert.Equal("", state.Error);
        Assert.Equal(1788240000, state.CheckedAt);
    }
}
