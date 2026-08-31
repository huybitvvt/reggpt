namespace SmsWorkbench
{
    public partial class MainWindow
    {
        // ── Search clear button ──

        private void SearchClear_Click(object sender, RoutedEventArgs e)
        {
            SearchText = "";
            UpdateSearchClearVisibility();
        }

        /// <summary>
        /// Toggle the visibility of the search clear (×) button based on
        /// whether the search text is non-empty. Called from the SearchText
        /// setter and from the clear button click handler.
        /// </summary>
        private void UpdateSearchClearVisibility()
        {
            if (SearchClearButton != null)
            {
                SearchClearButton.Visibility = string.IsNullOrEmpty(SearchText)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        // ── DataGrid context menu handlers ──

        private void CtxViewDetail_Click(object sender, RoutedEventArgs e)
        {
            if (AccountGrid?.SelectedItem is PoolRow row)
                ShowAccountDetail(row);
        }

        private void CtxViewInbox_Click(object sender, RoutedEventArgs e)
        {
            if (AccountGrid?.SelectedItem is PoolRow row)
                ShowInboxDialog(row);
        }

        private void CtxCopyEmail_Click(object sender, RoutedEventArgs e)
        {
            if (AccountGrid?.SelectedItem is PoolRow row && !string.IsNullOrWhiteSpace(row.Identifier))
            {
                try
                {
                    Clipboard.SetText(row.Identifier);
                    NotifyInfo("Đã sao chép email: " + row.Identifier);
                }
                catch (Exception ex)
                {
                    Log("Sao chép email thất bại: " + ex.Message);
                }
            }
        }

        private void CtxCopyAccessToken_Click(object sender, RoutedEventArgs e)
            => RunUiTask(CtxCopyAccessTokenAsync);

        private async Task CtxCopyAccessTokenAsync()
        {
            if (AccountGrid?.SelectedItem is not PoolRow row)
            {
                NotifyWarning("Vui lòng chọn một tài khoản trước.");
                return;
            }
            string accessToken = await ResolveAccountAccessTokenAsync(row);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                NotifyWarning("Tài khoản đang chọn không có AT để sao chép.");
                return;
            }
            try
            {
                Clipboard.SetText(accessToken);
                NotifyInfo("Đã sao chép AT.");
            }
            catch (Exception ex)
            {
                Log("Sao chép AT thất bại: " + ex.Message);
            }
        }

        private void CtxCopyPayPal_Click(object sender, RoutedEventArgs e)
        {
            if (AccountGrid?.SelectedItem is PoolRow row && !string.IsNullOrWhiteSpace(row.PayPalUrl))
            {
                CopyPayPalUrl(row.PayPalUrl, row.Identifier);
            }
            else
            {
                NotifyWarning("Dòng đang chọn không có link thanh toán.");
            }
        }

        private void CtxOpenPayPal_Click(object sender, RoutedEventArgs e)
        {
            if (AccountGrid?.SelectedItem is PoolRow row && !string.IsNullOrWhiteSpace(row.PayPalUrl))
            {
                OpenPayPalUrl(row.PayPalUrl, row.Identifier);
            }
            else
            {
                NotifyWarning("Dòng đang chọn không có link thanh toán.");
            }
        }

        private void CtxOpenSource_Click(object sender, RoutedEventArgs e)
        {
            if (AccountGrid?.SelectedItem is PoolRow row)
                OpenAccountJson(row);
        }

        private void CtxCheckAccountAlive_Click(object sender, RoutedEventArgs e)
            => RunUiTask(CtxCheckAccountAliveAsync);

        private async Task CtxCheckAccountAliveAsync()
        {
            if (AccountGrid?.SelectedItem is not PoolRow row || string.IsNullOrWhiteSpace(row.Identifier))
            {
                NotifyWarning("Vui lòng chọn một tài khoản trước.");
                return;
            }
            await CheckAccountAliveAsync(row);
        }

        private void CtxBatchProtocolPayment_Click(object sender, RoutedEventArgs e)
        {
            BatchProtocolPayment_Click(sender, e);
        }

        private void CtxChangeEmail_Click(object sender, RoutedEventArgs e)
            => RunUiTask(CtxChangeEmailAsync);

        private void ChangeEmail_Click(object sender, RoutedEventArgs e)
            => RunUiTask(CtxChangeEmailAsync);

        private async Task CtxChangeEmailAsync()
        {
            var rows = SelectedRowsOrCurrent().Where(row => row != null && !string.IsNullOrWhiteSpace(row.Identifier)).ToList();
            if (rows.Count == 0)
            {
                NotifyWarning("Vui lòng chọn tài khoản cần đổi email trước.");
                return;
            }
            var options = ChangeEmailDialogService.Show(
                this,
                rows.Count,
                DefaultWorkerCount(),
                GetConfiguredSmailrDomain(),
                GetConfiguredCfWorkerDomain());
            if (options is null) return;
            var plan = BackendCommandPlanner.CreateChangeEmail(
                rows.Select(row => row.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                options.Provider,
                options.MailboxFile,
                options.Workers,
                options.SmailrDomain,
                options.CfworkerDomain,
                GetRegistrationProxyPool(),
                rootDir);
            string json;
            try { json = await RunBackendWithResultAsync(plan.TaskName, plan.Arguments.ToList(), plan.TimeoutMilliseconds ?? 900000); }
            finally { foreach (string path in plan.TempFiles) TryDeleteFile(path); }
            try
            {
                using var doc = JsonDocument.Parse(json);
                bool ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
                await DialogFactory.ShowInfoAsync(this, "Đổi email liên kết", ok ? "Đổi email hoàn tất." : "Đổi email thất bại một phần, vui lòng xem kết quả tác vụ. ");
                RefreshPools();
            }
            catch
            {
                await DialogFactory.ShowInfoAsync(this, "Đổi email liên kết", "Chưa nhận được kết quả hợp lệ, vui lòng xem log chạy.");
            }
        }

        private async Task CheckAccountAliveAsync(PoolRow row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.Identifier))
            {
                NotifyWarning("Vui lòng chọn một tài khoản trước.");
                return;
            }

            if (!row.HasAccessToken)
            {
                await DialogFactory.ShowInfoAsync(this, "Kiểm tra sống tài khoản", "Tài khoản này chưa có Access Token nên không thể kiểm tra sống. Vui lòng đăng nhập để lấy AT trước.");
                return;
            }

            try
            {
                Log($"Đang kiểm tra sống tài khoản: {row.Identifier}");
                var args = new List<string> { "--quota-usage", "--email", row.Identifier, "--refresh-timeout", "45" };
                AddRegistrationProxy(args);
                string json = await RunBackendWithResultAsync("Kiểm tra sống tài khoản", args);

                if (string.IsNullOrWhiteSpace(json))
                {
                    await DialogFactory.ShowInfoAsync(this, "Kiểm tra sống tài khoản", "Kiểm tra sống tài khoản thất bại: chưa nhận được phản hồi hợp lệ.");
                    return;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean())
                {
                    string detail = FormatAccountLivenessDetail(root);
                    await DialogFactory.ShowInfoAsync(this, $"Kiểm tra sống tài khoản: {row.Identifier}", detail);
                    Log($"Kiểm tra sống tài khoản thành công: {row.Identifier} → AT hợp lệ");
                    RefreshPools();
                }
                else
                {
                    string error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() ?? "Lỗi không xác định" : "Lỗi không xác định";
                    string status = root.TryGetProperty("status", out var stEl) ? stEl.GetString() ?? "" : "";
                    string msg = $"Kiểm tra sống thất bại: {error}";
                    if (status == "token_invalid")
                        msg += "\n\nAPI trả về HTTP 401, Access Token hiện tại đã hết hiệu lực.";
                    await DialogFactory.ShowInfoAsync(this, $"Kiểm tra sống tài khoản: {row.Identifier}", msg);
                    Log($"Kiểm tra sống tài khoản thất bại: {row.Identifier} → {error}");
                }
            }
            catch (Exception ex)
            {
                Log($"Lỗi kiểm tra sống tài khoản: {ex.Message}");
                await DialogFactory.ShowInfoAsync(this, "Kiểm tra sống tài khoản", $"Lỗi kiểm tra sống: {ex.Message}");
            }
        }

        private static string FormatAccountLivenessDetail(JsonElement root)
        {
            var sb = new StringBuilder();
            string statusCode = root.TryGetProperty("status_code", out var codeEl) ? codeEl.ToString() : "";
            sb.AppendLine("Trạng thái: AT hợp lệ");
            sb.AppendLine("API: HTTP " + (string.IsNullOrWhiteSpace(statusCode) ? "200" : statusCode));
            return sb.ToString().TrimEnd();
        }
    }
}
