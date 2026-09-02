namespace SmsWorkbench
{
    public sealed partial class AccountFilterChip : ObservableObject
    {
        public AccountFilterChip(string key, string label)
        {
            Key = key ?? "";
            Label = label ?? "";
        }

        public string Key { get; }

        public string Label { get; }

        [ObservableProperty]
        private bool isSelected;
    }

    public static class AccountPaymentFilter
    {
        public static bool MatchesAll(PoolRow row, IEnumerable<string> filterKeys)
        {
            if (row == null) return false;
            foreach (string key in filterKeys ?? Array.Empty<string>())
            {
                if (!Matches(row, key)) return false;
            }
            return true;
        }

        public static bool Matches(PoolRow row, string filterKey)
        {
            if (row == null) return false;
            string key = Normalize(filterKey);
            IReadOnlyList<string> badges = row.PaymentMethodBadges ?? Array.Empty<string>();
            HashSet<string> normalizedBadges = badges
                .Select(Normalize)
                .Where(value => value.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string promotion = row.PromotionStatus ?? "";
            bool hasBadges = normalizedBadges.Count > 0;
            string checkStatus = Normalize(row.PaymentCheckStatus);
            string offerState = Normalize(row.OfferState);
            bool hasTrial = offerState == "zerodue"
                || normalizedBadges.Any(value => value.StartsWith("trial", StringComparison.OrdinalIgnoreCase))
                || PromotionStatusPresentation.IsTrialEligible(promotion);
            bool skipped = checkStatus is "skipped" or "disabled";
            bool failed = !skipped && (
                (row.PaymentCheckError ?? "").Trim().Length > 0
                || checkStatus is "failed" or "error" or "unknown"
                || IsPaymentCheckFailure(promotion));
            bool uncheckedPayment = skipped
                || (checkStatus.Length == 0 && !hasBadges && !failed && row.PaymentCheckedAt <= 0);

            return key switch
            {
                "trial" => hasTrial,
                "nooffer" => !hasTrial && (offerState == "nonzerodue" || IsNoOffer(promotion)),
                "paymenterror" => failed,
                "unchecked" => uncheckedPayment,
                "momo" => normalizedBadges.Contains("momo"),
                "gpay" => normalizedBadges.Contains("googlepay"),
                "applepay" => normalizedBadges.Contains("applepay"),
                "card" => normalizedBadges.Contains("card"),
                "upi" => normalizedBadges.Contains("upi"),
                "gopay" => normalizedBadges.Contains("gopay"),
                "kakao" => normalizedBadges.Contains("kakaopay") || normalizedBadges.Contains("kakao"),
                "naver" => normalizedBadges.Contains("naverpay") || normalizedBadges.Contains("naver"),
                _ => true,
            };
        }

        private static bool IsNoOffer(string value)
        {
            string text = value ?? "";
            return text.Contains("không có ưu đãi", StringComparison.OrdinalIgnoreCase)
                || text.Contains("khong co uu dai", StringComparison.OrdinalIgnoreCase)
                || text.Contains("no offer", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPaymentCheckFailure(string value)
        {
            string text = value ?? "";
            return text.Contains("kiểm tra thất bại", StringComparison.OrdinalIgnoreCase)
                || text.Contains("kiem tra that bai", StringComparison.OrdinalIgnoreCase)
                || text.Contains("detection failed", StringComparison.OrdinalIgnoreCase)
                || text.Contains("payment check failed", StringComparison.OrdinalIgnoreCase)
                || text.Contains("pttt lỗi", StringComparison.OrdinalIgnoreCase)
                || text.Contains("检测失败", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            return Regex.Replace((value ?? "").Trim().ToLowerInvariant(), "[^a-z0-9]+", "");
        }
    }
}
