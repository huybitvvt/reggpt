using System.Linq.Expressions;
using Expr = System.Linq.Expressions.Expression;

namespace SmsWorkbench
{
    public sealed class StatusSeverityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = (value as string ?? "").Trim();
            if (status.Length == 0) return "neutral";

            if (PromotionStatusPresentation.IsTrialEligible(status)
                || status.Contains('✅') || Has(status, "Hoàn tất", "\u5b8c\u6210")
                || Has(status, "Đã đăng ký", "\u5df2\u6ce8\u518c")
                || Has(status, "Đã lấy", "\u5df2\u83b7\u53d6")
                || Has(status, "Đã nhập", "\u5df2\u5bfc\u5165")
                || Has(status, "K12 đã vào", "PM đã tạo", "Đã thiết lập"))
                return "success";

            if (Has(status, "Thất bại", "\u5931\u8d25")
                || Has(status, "hết hiệu lực", "\u5931\u6548")
                || Has(status, "Bị vô hiệu", "\u6389\u53f7", "\u505c\u7528")
                || Has(status, "Lỗi", "\u5f02\u5e38")
                || Has(status, "Không có RT", "Thiếu", "Chưa lấy", "K12 chưa chuyển", "K12 đã thoát"))
                return "danger";

            if (Has(status, "Chờ", "chờ", "\u5f85", "Thiếu", "thiếu", "\u7f3a", "OTP")
                || Has(status, "K12 đã yêu cầu", "token cũ"))
                return "warn";

            if (Has(status, "Đã lưu", "Chờ làm mới", "Không rõ", "\u672a\u77e5"))
                return "info";

            return "neutral";
        }

        private static bool Has(string text, params string[] values)
            => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public static class PromotionStatusPresentation
    {
        public static bool IsTrialEligible(string status)
        {
            string value = (status ?? "").Trim();
            if (value.Length == 0) return false;
            return (value.Contains("có dùng thử", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("\u53ef\u8bd5\u7528", StringComparison.OrdinalIgnoreCase))
                && value.Contains("plus", StringComparison.OrdinalIgnoreCase);
        }

        public static int SortRank(string status)
        {
            if (IsTrialEligible(status)) return 0;
            return string.IsNullOrWhiteSpace(status) ? 2 : 1;
        }
    }

    public static class AccountGridOrdering
    {
        // Compiled property getters: sorting several hundred rows used to do a
        // TypeDescriptor property lookup (reflection) per row per keystroke.
        private static readonly Dictionary<string, Func<PoolRow, object>> PropertyGetters = BuildGetters();

        private static Dictionary<string, Func<PoolRow, object>> BuildGetters()
        {
            var getters = new Dictionary<string, Func<PoolRow, object>>(StringComparer.Ordinal);
            ParameterExpression parameter = Expr.Parameter(typeof(PoolRow), "row");
            foreach (System.Reflection.PropertyInfo property in typeof(PoolRow).GetProperties())
            {
                if (!property.CanRead || property.GetMethod == null) continue;
                System.Linq.Expressions.Expression body = Expr.Convert(
                    Expr.Property(parameter, property), typeof(object));
                getters[property.Name] = Expr.Lambda<Func<PoolRow, object>>(body, parameter).Compile();
            }
            return getters;
        }

        public static IEnumerable<PoolRow> Apply(
            IEnumerable<PoolRow> rows,
            string sortMember,
            ListSortDirection? direction)
        {
            if (rows == null) return Enumerable.Empty<PoolRow>();
            string member = (sortMember ?? "").Trim();
            if (member.Length == 0 || direction == null) return rows;

            Func<PoolRow, AccountSortValue> selector = row => SortValue(row, member);
            return direction == ListSortDirection.Descending
                ? rows.OrderByDescending(selector)
                : rows.OrderBy(selector);
        }

        private static AccountSortValue SortValue(PoolRow row, string member)
        {
            if (member.Equals(nameof(PoolRow.PromotionStatus), StringComparison.Ordinal))
            {
                string promotion = row?.PromotionStatus ?? "";
                return new AccountSortValue(PromotionStatusPresentation.SortRank(promotion), promotion);
            }

            if (PropertyGetters.TryGetValue(member, out Func<PoolRow, object> getter))
            {
                object value = row == null ? null : getter(row);
                return new AccountSortValue(value == null ? 1 : 0, Convert.ToString(value, CultureInfo.CurrentCulture) ?? "");
            }
            return new AccountSortValue(1, "");
        }

        private readonly record struct AccountSortValue(int Rank, string Text) : IComparable<AccountSortValue>
        {
            public int CompareTo(AccountSortValue other)
            {
                int rank = Rank.CompareTo(other.Rank);
                return rank != 0
                    ? rank
                    : StringComparer.CurrentCultureIgnoreCase.Compare(Text, other.Text);
            }
        }
    }
}
