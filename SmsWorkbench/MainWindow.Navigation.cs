namespace SmsWorkbench
{
    public partial class MainWindow
    {
        // Session refresh, row selection and paging filters
        private void RefreshSession_Click(object sender, RoutedEventArgs e)
        {
            PoolRow row = SelectedEmailRowOrNotify("Làm mới Session");
            if (row == null) return;
            var args = new List<string> { "--email", row.Identifier, "--refresh-session" };
            AddSessionFileArg(args, row);
            RunBackend("Làm mới Session", args);
        }

        private void AddSessionFileArg(List<string> args, PoolRow row)
        {
            string jsonPath = SessionFileFor(row);
            if (jsonPath.Length > 0)
            {
                args.Add("--session-file");
                args.Add(jsonPath);
            }
        }

        private static string SessionFileFor(PoolRow row)
        {
            if (row == null) return "";
            string jsonPath = File.Exists(row.Notes) && row.Notes.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? row.Notes
                : row.SourcePath;
            return File.Exists(jsonPath) && jsonPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? jsonPath
                : "";
        }

        private PoolRow SelectedEmailRowOrNotify(string action)
        {
            PoolRow row = SelectedRow ?? (AccountGrid.SelectedItem as PoolRow);
            if (row == null)
            {
                ShowEmailSelectionRequired(action);
            }
            return row;
        }

        private List<PoolRow> SelectedEmailRowsOrNotify(string action)
        {
            var rows = SelectedRowsOrCurrent()
                .Where(row => !string.IsNullOrWhiteSpace(row.Identifier))
                .GroupBy(row => row.Identifier.Trim().ToLowerInvariant())
                .Select(group => group.First())
                .ToList();
            if (rows.Count == 0)
            {
                ShowEmailSelectionRequired(action);
            }
            return rows;
        }

        private void ShowEmailSelectionRequired(string action)
        {
            string detail = string.IsNullOrWhiteSpace(action) ? "thực hiện thao tác này" : action.Trim();
            ShowThemedInfoDialog("Chưa chọn email", $"Vui lòng tick hoặc chọn tài khoản email trước khi {detail}.");
        }

        private List<PoolRow> SelectedRowsOrCurrent()
        {
            var rows = allRows.Where(r => r.IsChecked).ToList();
            if (rows.Count == 0)
            {
                PoolRow row = SelectedRow ?? (AccountGrid.SelectedItem as PoolRow);
                if (row != null) rows.Add(row);
            }
            return rows;
        }

        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            currentPage = 1;
            RefreshPagedRows();
        }

        private void AccountGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            string member = (e.Column.SortMemberPath ?? "").Trim();
            if (member.Length == 0) return;

            e.Handled = true;
            ListSortDirection next = e.Column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            foreach (DataGridColumn column in AccountGrid.Columns)
            {
                column.SortDirection = null;
            }
            e.Column.SortDirection = next;
            accountSortMember = member;
            accountSortDirection = next;
            currentPage = 1;
            RefreshPagedRows();
        }

        private void ShowAll_Click(object sender, RoutedEventArgs e) => SetScope("Tất cả");

        private void ShowMailboxPool_Click(object sender, RoutedEventArgs e) => SetScope("Pool email");

        private void ShowRegistered_Click(object sender, RoutedEventArgs e) => SetScope("Đã đăng ký");

        private void ShowPending_Click(object sender, RoutedEventArgs e) => SetScope("Chờ xử lý");

        private void FirstPage_Click(object sender, RoutedEventArgs e)
        {
            currentPage = 1;
            RefreshPagedRows();
        }

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            currentPage--;
            RefreshPagedRows();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            currentPage++;
            RefreshPagedRows();
        }

        private void LastPage_Click(object sender, RoutedEventArgs e)
        {
            int pageSize = PageSizeValue();
            int count = allRows.Count(FilterRow);
            currentPage = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
            RefreshPagedRows();
        }

        private void SetScope(string scope)
        {
            ScopeFilter = scope;
            currentPage = 1;
            RefreshPagedRows();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (PoolRow row in allRows) row.IsChecked = false;
            SelectedRow = null;
            OnPropertyChanged(nameof(SelectedRow));
            if (AccountGrid != null)
            {
                AccountGrid.SelectedItem = null;
                AccountGrid.SelectedIndex = -1;
                AccountGrid.UnselectAll();
            }
        }

        private void SelectAllFiltered_Click(object sender, RoutedEventArgs e)
        {
            foreach (PoolRow row in allRows.Where(FilterRow))
            {
                row.IsChecked = true;
            }
        }
    }
}
