namespace SmsWorkbench
{
    public partial class MainWindow
    {
        private void BatchProtocolPayment_Click(object sender, RoutedEventArgs e)
        {
            var rows = SelectedRowsOrCurrent()
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Identifier))
                .ToList();
            ShowPaymentBatchDialog(rows);
        }

        private void ShowPaymentBatchDialog(IEnumerable<PoolRow> rows)
        {
            // Only one backend task may run at a time — the coordinator enforces
            // this and would reject a second concurrent run with
            // BackendTaskAlreadyRunningException. Block at the UI instead of
            // letting the dialog open and the run immediately fail.
            if (backendTasks.IsRunning)
            {
                MessageBox.Show(
                    this,
                    "Đang có tác vụ backend chạy. Vui lòng đợi hoàn tất hoặc hủy trước khi bắt đầu thanh toán hàng loạt.",
                    "Tác vụ đang chạy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var accounts = rows.Select(row => new PaymentBatchAccount(row.Identifier, row.HasAccessToken));
            if (paymentBatchDialogs.ShowDialog(this, accounts))
                RefreshPools();
        }
    }
}
