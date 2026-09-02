namespace SmsWorkbench
{
    public static class AccountCredentialExport
    {
        public static bool TryBuildLoginLine(
            Dictionary<string, object> data,
            string fallbackEmail,
            bool requireTotp,
            out string line,
            out string missingField)
        {
            line = "";
            missingField = "";
            data ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string email = FirstNonEmpty(BackendJson.GetString(data, "email"), fallbackEmail).Trim();
            string password = BackendJson.GetString(data, "password").Trim();
            string secret = BackendJson.GetString(data, "totp_secret").Trim();

            if (email.Length == 0)
            {
                missingField = "email";
                return false;
            }
            if (password.Length == 0)
            {
                missingField = "password";
                return false;
            }
            if (requireTotp && secret.Length == 0)
            {
                missingField = "totp_secret";
                return false;
            }
            if (!IsSafeField(email) || !IsSafeField(password) || !IsSafeField(secret))
            {
                missingField = "invalid_separator";
                return false;
            }

            line = email + "|" + password;
            if (secret.Length > 0) line += "|" + secret;
            return true;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
        }

        private static bool IsSafeField(string value)
        {
            return !(value ?? "").Contains('|')
                && !(value ?? "").Contains('\r')
                && !(value ?? "").Contains('\n');
        }
    }
}
