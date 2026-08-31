namespace SmsWorkbench
{
    public partial class MainWindow
    {
        // Payment-link actions and unified protocol extractor.
        // CLI argument construction is delegated to BackendCommandPlanner;
        // backend JSON interpretation is delegated to ProtocolPaymentResultPresenter
        // and BackendResultInterpreter.

        private void OpenSessions_Click(object sender, RoutedEventArgs e) => OpenPath(GetSessionsDir());

        private void OpenDatabase_Click(object sender, RoutedEventArgs e) => OpenPath(GetDatabasePath());

        private void OpenMailboxPool_Click(object sender, RoutedEventArgs e) => OpenPath(GetMailboxTokenFile());

        private void OpenPayPalLink_Click(object sender, RoutedEventArgs e)
        {
            PoolRow row = SelectedEmailRowOrNotify("Mở link thanh toán");
            if (row == null) return;
            if (string.IsNullOrWhiteSpace(row.PayPalUrl))
            {
                MessageBox.Show("Tài khoản đang chọn không có link thanh toán để mở.", "Không có link thanh toán", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            OpenPayPalUrl(row.PayPalUrl, row.Identifier);
        }

        private void AtExtractBaLink_Click(object sender, RoutedEventArgs e)
        {
            var selected = SelectedRowsOrCurrent()
                .Where(row => !string.IsNullOrWhiteSpace(row.Identifier))
                .GroupBy(row => row.Identifier.Trim().ToLowerInvariant())
                .Select(group => group.First())
                .ToList();
            ShowPaymentBatchDialog(selected);
        }

        /// <summary>
        /// Unified protocol payment-link extractor.
        /// Uses ProtocolPaymentExecutionPlanner for CLI construction and
        /// ProtocolPaymentResultPresenter for JSON interpretation.
        /// Error handling is unified via BackendResultInterpreter.
        /// </summary>
        private void ShowProtocolPaymentDialog(PoolRow selectedAccount = null)
        {
            // Same single-backend-task guard as the batch dialog: the coordinator
            // rejects a concurrent run, so surface it here instead of failing
            // once the dialog's run begins.
            if (backendTasks.IsRunning)
            {
                MessageBox.Show(
                    this,
                    "Đang có tác vụ backend chạy. Vui lòng đợi hoàn tất hoặc hủy trước khi bắt đầu thanh toán giao thức.",
                    "Tác vụ đang chạy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ProtocolPaymentAccount account = selectedAccount == null
                ? null
                : new ProtocolPaymentAccount(selectedAccount.Identifier, SessionFileFor(selectedAccount));
            if (protocolPaymentDialogs == null)
            {
                MessageBox.Show(this, "Dịch vụ hộp thoại thanh toán giao thức chưa được cấu hình.", "Lỗi cấu hình", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            protocolPaymentDialogs.ShowDialog(this, account);
        }

    }
}
