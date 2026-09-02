namespace SmsWorkbench
{
    public partial class MainWindow
    {
        // Pool/session loading, filtering, overview
        private bool FilterRow(object item)
        {
            return item is PoolRow row && FilterRow(row);
        }

        private bool FilterRow(PoolRow row)
        {
            if (row == null) return false;
            string scope = DisplayText(ScopeFilter);
            string term = (SearchText ?? "").Trim().ToLowerInvariant();

            if (scope == "Có dùng thử" && !PromotionStatusPresentation.IsTrialEligible(row.PromotionStatus)) return false;
            if (scope == "Chờ xử lý" && !IsAttentionStatus(row.Status)) return false;
            if (!AccountPaymentFilter.MatchesAll(
                    row,
                    PaymentFilterChips.Where(chip => chip.IsSelected).Select(chip => chip.Key)))
                return false;
            if (term.Length == 0) return true;

            string text = (row.Identifier + " " + row.AccountType + " " + row.Status + " " + row.Notes).ToLowerInvariant();
            return text.Contains(term);
        }

        private void PaymentFilterChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton toggle
                && toggle.DataContext is AccountFilterChip chip)
            {
                chip.IsSelected = toggle.IsChecked == true;
            }
            OnPropertyChanged(nameof(PaymentFilterSummary));
            OnPropertyChanged(nameof(HasPaymentFilters));
            currentPage = 1;
            RefreshPagedRows();
        }

        private void ClearPaymentFilters_Click(object sender, RoutedEventArgs e)
        {
            foreach (AccountFilterChip chip in PaymentFilterChips)
            {
                chip.IsSelected = false;
            }
            OnPropertyChanged(nameof(PaymentFilterSummary));
            OnPropertyChanged(nameof(HasPaymentFilters));
            currentPage = 1;
            RefreshPagedRows();
        }

        private bool IsMailboxPoolLikeRow(PoolRow row)
        {
            if (row == null) return false;
            return MailboxPoolFileStore.IsMailboxPoolLike(row.AccountType, row.MailboxProvider);
        }

        private bool poolsRefreshRunning;

        /// <summary>
        /// Fire-and-forget refresh entry kept for existing sync callers; the
        /// actual work is async so the UI thread never waits on a backend call.
        /// </summary>
        private void RefreshPools()
        {
            _ = RefreshPoolsAsync();
        }

        private async Task RefreshPoolsAsync()
        {
            if (poolsRefreshRunning)
                return;
            poolsRefreshRunning = true;
            try
            {
                allRows.Clear();
                try
                {
                    // One merged request (accounts + mailbox pool) through the
                    // resident channel; falls back to the two separate reads.
                    JsonElement merged = await desktopRead.ReadPoolsAsync(GetChataiMailboxFilePath());
                    ApplyMailboxPoolPayload(merged);
                    ApplyAccountsPayload(merged);
                }
                catch (Exception ex)
                {
                    Log("Đọc gộp pool email/tài khoản thất bại, chuyển sang đọc riêng: " + SensitiveDataSanitizer.Redact(ex.Message));
                    await LoadMailboxPoolAsync();
                    await LoadSessionPoolAsync();
                }
                DeduplicateRows();
                currentPage = 1;
                UpdateOverview();
                RefreshPagedRows();
                StatusText = $"Tổng {allRows.Count} dòng; bộ lọc hiện tại {filteredCount} dòng";
                Log("Đã làm mới trạng thái pool email và session.");
            }
            finally
            {
                poolsRefreshRunning = false;
            }
        }

        private void RefreshPagedRows()
        {
            if (PagedRows == null) return;
            var filtered = AccountGridOrdering.Apply(
                allRows.Where(FilterRow),
                accountSortMember,
                accountSortDirection).ToList();
            filteredCount = filtered.Count;
            int pageSize = PageSizeValue();
            int pageCount = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
            if (currentPage < 1) currentPage = 1;
            if (currentPage > pageCount) currentPage = pageCount;

            PagedRows.Clear();
            foreach (PoolRow row in filtered.Skip((currentPage - 1) * pageSize).Take(pageSize))
            {
                PagedRows.Add(row);
            }

            int start = filteredCount == 0 ? 0 : (currentPage - 1) * pageSize + 1;
            int end = filteredCount == 0 ? 0 : Math.Min(filteredCount, currentPage * pageSize);
            PageStatusText = $"Trang {currentPage}/{pageCount} trang, hiển thị {start}-{end} / {filteredCount}";
            StatusText = $"Tổng {allRows.Count} dòng; bộ lọc hiện tại {filteredCount} dòng";
        }

        private void UpdateOverview()
        {
            int trialEligible = allRows.Count(r => PromotionStatusPresentation.IsTrialEligible(r.PromotionStatus));
            int registered = allRows.Count(IsRegisteredRow);
            int attention = allRows.Count(r => IsAttentionStatus(r.Status));
            TotalCountText = allRows.Count.ToString();
            TrialCountText = trialEligible.ToString();
            RegisteredCountText = registered.ToString();
            AttentionCountText = attention.ToString();
        }

        private bool IsRegisteredRow(PoolRow row)
        {
            return row.AccountType.Contains("Session")
                || row.SourcePath.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)
                || HasStatus(row.Status, "Đã đăng ký", "\u5df2\u6ce8\u518c")
                || row.Status.Contains("PayPal");
        }

        private bool IsImportableAccountRow(PoolRow row)
        {
            if (row == null) return false;
            if (string.IsNullOrWhiteSpace(row.Identifier)) return false;
            if (row.HasAccessToken) return true;
            string status = (row.Status + " " + row.PayPalStatus).Trim();
            return status.Contains("Đã đăng ký")
                || status.Contains("Chờ thanh toán")
                || status.Contains("Thanh toán hoàn tất")
                || status.Contains("PM đã tạo")
                || status.Contains("Đã nhập")
                || status.Contains("Registered")
                || status.Contains("Payment completed");
        }

        private void DeduplicateRows()
        {
            var best = new Dictionary<string, PoolRow>(StringComparer.OrdinalIgnoreCase);
            foreach (PoolRow row in allRows.ToList())
            {
                string key = NormalizeEmailKey(row.Identifier);
                if (key.Length == 0) continue;
                if (!best.TryGetValue(key, out PoolRow existing) || RowPriority(row) > RowPriority(existing))
                {
                    best[key] = row;
                }
            }

            if (best.Count == 0) return;
            var deduped = allRows.Where(row =>
            {
                string key = NormalizeEmailKey(row.Identifier);
                return key.Length == 0 || ReferenceEquals(best[key], row);
            }).ToList();
            if (deduped.Count == allRows.Count) return;
            allRows.Clear();
            foreach (PoolRow row in deduped) allRows.Add(row);
        }

        private int RowPriority(PoolRow row)
        {
            if (row.SourcePath.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)) return 30;
            if (row.AccountType.Contains("Session")) return 20;
            if (row.PayPalUrl.Length > 0 || row.Status.Contains("PayPal")) return 15;
            return 10;
        }

        private string NormalizeEmailKey(string email)
        {
            return MailboxPoolFileStore.NormalizeEmailKey(email);
        }

        private async Task LoadMailboxPoolAsync()
        {
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this)) return;
            try
            {
                JsonElement payload = await desktopRead.ReadMailboxPoolAsync(GetChataiMailboxFilePath());
                ApplyMailboxPoolPayload(payload);
            }
            catch (Exception ex)
            {
                Log("Đọc backend pool email thất bại: " + SensitiveDataSanitizer.Redact(ex.Message));
            }
        }

        private void ApplyMailboxPoolPayload(JsonElement payload)
        {
            if (!payload.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
            {
                Log("Đọc backend pool email thất bại: Phản hồi thiếu mảng files.");
                return;
            }
            foreach (JsonElement file in files.EnumerateArray())
            {
                AddMailboxPoolFileRows(file);
            }
        }

        private void AddMailboxPoolFileRows(JsonElement file)
        {
            string path = JsonString(file, "path");
            if (path.Length == 0 || !File.Exists(path)) return;
            if (!file.TryGetProperty("lines", out JsonElement lines) || lines.ValueKind != JsonValueKind.Array) return;
            string fileTime = SafeTime(File.GetLastWriteTime(path));
            int index = 0;
            foreach (JsonElement line in lines.EnumerateArray())
            {
                AddMailboxPoolLineRow(path, fileTime, index, line);
                index++;
            }
        }

        private void AddMailboxPoolLineRow(string path, string fileTime, int index, JsonElement line)
        {
            string provider = JsonString(line, "provider").ToLowerInvariant();
            string email = JsonString(line, "email");
            if (email.Length == 0) return;
            string authMode = JsonString(line, "auth_mode");
            string rawLine = JsonString(line, "raw_line");
            string mailboxLine = provider == "cfworker" && !rawLine.StartsWith("cfworker://", StringComparison.OrdinalIgnoreCase)
                ? "cfworker://" + email
                : rawLine;
            string refreshToken = JsonString(line, "refresh_token");
            string token = JsonString(line, "token");
            allRows.Add(new PoolRow
            {
                Id = "M" + (index + 1),
                CreatedAt = fileTime,
                CompletedAt = fileTime,
                Identifier = email,
                AccountType = MailboxPoolAccountType(provider),
                Status = MailboxPoolStatus(provider, authMode),
                RefreshToken = MailboxPoolRefreshDisplay(provider, refreshToken),
                Notes = path,
                SourcePath = path,
                RawLine = mailboxLine,
                MailboxLine = mailboxLine,
                MailboxProvider = provider,
                MailboxToken = provider is "remail" or "smailr" or "icloud_url" ? token : "",
                ClientId = JsonString(line, "client_id"),
                RawRefreshToken = provider is "gmail" or "chatai" ? refreshToken : ""
            });
        }

        private static string MailboxPoolAccountType(string provider) => provider switch
        {
            "cfworker" => "Pool email CFWorker",
            "remail" => "Pool email ReMail",
            "smailr" => "Pool email Smailr",
            "icloud_url" => "Pool email iCloud",
            "gmail" => "Pool email Gmail",
            "chatai" => "Pool email Chatai",
            _ => "Pool email",
        };

        private static bool IsAttentionStatus(string status)
            => HasStatus(status, "Chờ", "chờ", "\u5f85", "Thiếu", "thiếu", "\u7f3a", "Thất bại", "\u5931\u8d25");

        private static bool HasStatus(string text, params string[] values)
            => values.Any(value => (text ?? "").Contains(value, StringComparison.OrdinalIgnoreCase));

        private static string MailboxPoolStatus(string provider, string authMode)
        {
            if (provider == "gmail") return authMode == "oauth_refresh" ? "Đã ủy quyền" : "Có thể nhận thư";
            if (provider is "chatai" or "graph" or "chongzhi") return "Đã ủy quyền";
            return "Có thể nhận thư";
        }

        private string MailboxPoolRefreshDisplay(string provider, string refreshToken)
        {
            switch (provider)
            {
                case "cfworker": return "CFWorker";
                case "remail": return "ReMail";
                case "smailr": return "Smailr";
                case "icloud_url": return "Link nhận mã";
                case "gmail": return refreshToken.Length > 0 ? Mask(refreshToken) : "AppPassword";
                default: return refreshToken.Length > 0 ? Mask(refreshToken) : "";
            }
        }

        private string GetChataiMailboxFilePath()
        {
            if (!string.IsNullOrWhiteSpace(chataiMailboxFilePath) && File.Exists(chataiMailboxFilePath))
                return chataiMailboxFilePath;

            string[] candidates = { "hotmail.txt", "chatai_mailbox.txt", "chatai.txt" };
            foreach (string name in candidates)
            {
                string path = Path.Combine(rootDir, name);
                if (File.Exists(path)) return path;
            }

            foreach (string path in Directory.GetFiles(rootDir, "*chatai*.txt", SearchOption.TopDirectoryOnly))
            {
                return path;
            }

            return "";
        }

        private async Task LoadSessionPoolAsync()
        {
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this)) return;
            try
            {
                JsonElement payload = await desktopRead.ReadAccountsAsync();
                ApplyAccountsPayload(payload);
            }
            catch (Exception ex)
            {
                Log("Đọc backend tài khoản thất bại: " + SensitiveDataSanitizer.Redact(ex.Message));
            }
        }

        private void ApplyAccountsPayload(JsonElement payload)
        {
            if (!payload.TryGetProperty("accounts", out JsonElement accounts) || accounts.ValueKind != JsonValueKind.Array)
            {
                Log("Đọc backend tài khoản thất bại: phản hồi thiếu mảng accounts.");
                return;
            }
            // Per-refresh values hoisted out of the row loop; each used to
            // re-read and re-parse config.json once (or twice) per account.
            string databasePath = GetDatabasePath();
            foreach (JsonElement account in accounts.EnumerateArray())
            {
                Dictionary<string, object> data = JsonElementToDictionary(account);
                string rawJson = account.TryGetProperty("session", out JsonElement sessionElement)
                    && sessionElement.ValueKind == JsonValueKind.Object
                    ? sessionElement.GetRawText()
                    : "{}";
                AddBackendAccountRow(data, databasePath, rawJson);
            }
        }

        private void AddBackendAccountRow(Dictionary<string, object> data, string databasePath, string rawJson)
        {
            string status = GetString(data, "status");
            bool hasAccess = ParseBoolean(FirstNonEmpty(
                GetString(data, "access_token_present"),
                GetString(data, "has_access_token")));
            bool hasPaymentUrl = ParseBoolean(FirstNonEmpty(
                GetString(data, "payment_url_present"),
                GetString(data, "has_payment_url")));
            string accessState = hasAccess ? "present" : "";
            string paymentMethod = GetString(data, "payment_method");
            string paypalUrl = hasPaymentUrl ? "backend://payment-url" : "";
            string paypalStatus = GetString(data, "paypal_status");
            string paypalOk = GetString(data, "paypal_ok");
            string refreshStatus = GetString(data, "refresh_token_status");
            if (ParseBoolean(FirstNonEmpty(
                    GetString(data, "refresh_token_present"),
                    GetString(data, "has_refresh_token")))
                && (refreshStatus.Length == 0 || refreshStatus.Equals("no_rt", StringComparison.OrdinalIgnoreCase)))
            {
                refreshStatus = "oauth_present";
            }
            string provider = GetString(data, "mailbox_provider");
            AccountPaymentCheckState paymentCheck = AccountStatusInterpreter.GetPaymentCheckState(data, rawJson);
            var row = new PoolRow
            {
                Id = "DB" + GetString(data, "id"),
                CreatedAt = UnixTimeText(GetString(data, "created_at")),
                CompletedAt = UnixTimeText(GetString(data, "updated_at")),
                Identifier = GetString(data, "email"),
                AccountType = MailboxTypeDisplay(provider, GetString(data, "email")),
                AccountPlanType = AccountStatusInterpreter.GetAccountPlanType(data),
                Source = GetString(data, "source"),
                RegisterMethod = GetString(data, "register_method"),
                SessionType = GetString(data, "session_type"),
                PlanType = FirstNonEmpty(GetString(data, "plan_type"), GetString(data, "account_type")),
                RegistrationCountry = GetString(data, "registration_country"),
                Status = AccountStatusInterpreter.DisplayAccountStatus(status, paypalOk, accessState, GetString(data, "error"), paypalStatus, refreshStatus, AccountStatusInterpreter.GetImportedStatus(rawJson)),
                PayPalStatus = AccountStatusInterpreter.DisplayPayPalStatus(paypalStatus, paypalOk, paypalUrl, paymentMethod),
                PayPalAmount = AccountStatusInterpreter.GetPaypalAmount(rawJson),
                PromotionStatus = AccountStatusInterpreter.DisplayPromotionStatus(
                    GetString(data, "promotion_status"),
                    AccountStatusInterpreter.DisplayPayPalStatus(paypalStatus, paypalOk, paypalUrl, paymentMethod),
                    AccountStatusInterpreter.GetPaypalAmount(rawJson)),
                PaymentCheckStatus = paymentCheck.Status,
                OfferState = paymentCheck.OfferState,
                PaymentCheckError = paymentCheck.Error,
                PaymentCheckedAt = paymentCheck.CheckedAt,
                RefreshTokenStatus = AccountStatusInterpreter.DisplayRtStatus(refreshStatus),
                TwoFactorStatus = AccountStatusInterpreter.HasTwoFactor(data) ? "Đã thiết lập" : "Chưa thiết lập",
                HasAccessToken = hasAccess,
                AccessTokenProbeStatusCode = AccountStatusInterpreter.GetAccessTokenProbeStatusCode(data),
                PayPalUrl = paypalUrl,
                RefreshToken = provider == "remail" ? "ReMail" : hasAccess ? "AT" : "",
                Proxy = DbTimingText(new Dictionary<string, string>(data.ToDictionary(pair => pair.Key, pair => Convert.ToString(pair.Value) ?? ""))),
                Notes = GetString(data, "json_path").Length > 0 ? GetString(data, "json_path") : databasePath,
                SourcePath = databasePath,
                RawLine = GetString(data, "id"),
                MailboxProvider = provider,
                PaymentMethodBadges = AccountStatusInterpreter.GetPaymentMethodBadges(data, rawJson)
            };
            allRows.Add(row);
        }

        internal static string MailboxTypeDisplay(string provider, string email = "")
        {
            string normalized = (provider ?? "").Trim().ToLowerInvariant().Replace("-", "_");
            string domain = (email ?? "").Split('@').LastOrDefault()?.ToLowerInvariant() ?? "";
            return normalized switch
            {
                "remail" when domain is "outlook.com" or "hotmail.com" or "live.com" or "msn.com" => "remail/outlook",
                "remail" => "remail",
                "icloud_url" or "icloud" => "icloud",
                "cf_worker" or "cfworker" => "cfworker",
                "chongzhi" when domain is "outlook.com" or "hotmail.com" or "live.com" or "msn.com" => "outlook",
                "microsoft" or "graph" or "outlook" => "outlook",
                "gmail" => "gmail",
                "smailr" => "smailr",
                "chatai" => "chatai",
                "" => "unknown",
                _ => normalized,
            };
        }
    }
}
