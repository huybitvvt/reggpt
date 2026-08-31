namespace SmsWorkbench
{
    public partial class MainWindow
    {
        // Inbox view and mail detail dialog
        private void ShowInboxDialog(PoolRow row)
            => RunUiTask(() => ShowInboxDialogAsync(row));

        private async Task ShowInboxDialogAsync(PoolRow row)
        {
            var dialog = new Window
            {
                Title = "Hộp thư - " + row.Identifier,
                Owner = this,
                Width = 860,
                Height = 640,
                MinWidth = 700,
                MinHeight = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (System.Windows.Media.Brush)FindResource("AppBg")
            };

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerPanel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            var header = new TextBlock
            {
                Text = "Đang tải hộp thư...",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("TextMain"),
            };
            headerPanel.Children.Add(header);
            Grid.SetRow(headerPanel, 0);
            root.Children.Add(headerPanel);

            var mailGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                IsReadOnly = true,
                RowHeight = 28,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = (System.Windows.Media.Brush)FindResource("GridAltBg"),
                Background = (System.Windows.Media.Brush)FindResource("PanelBg"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextMain"),
                BorderThickness = new Thickness(0)
            };
            mailGrid.Columns.Add(new DataGridTextColumn { Header = "Thời gian", Binding = new System.Windows.Data.Binding("ReceivedAt"), Width = 150 });
            mailGrid.Columns.Add(new DataGridTextColumn { Header = "Người gửi", Binding = new System.Windows.Data.Binding("From"), Width = 200 });
            mailGrid.Columns.Add(new DataGridTextColumn { Header = "Tiêu đề", Binding = new System.Windows.Data.Binding("Subject"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            Grid.SetRow(mailGrid, 1);
            root.Children.Add(mailGrid);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var refreshBtn = new Button { Content = "Làm mới", Width = 72 };
            var closeBtn = new Button { Content = "Đóng", Width = 72 };
            actions.Children.Add(refreshBtn);
            actions.Children.Add(closeBtn);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);

            var mailItems = new ObservableCollection<MailItem>();
            mailGrid.ItemsSource = mailItems;

            closeBtn.Click += (_, __) => dialog.Close();

            async Task LoadEmails()
            {
                if (IsCfWorkerRow(row) || IsReMailRow(row) || IsSmailrRow(row))
                {
                    header.Text = IsReMailRow(row) ? "Đang lấy email ReMail..." : IsSmailrRow(row) ? "Đang lấy email Smailr..." : "Đang lấy email CFWorker...";
                    try
                    {
                        mailItems.Clear();
                        foreach (MailItem item in await FetchBackendInbox(row, 25))
                        {
                            mailItems.Add(item);
                        }
                        header.Text = row.Identifier + " - gần đây " + mailItems.Count + " email";
                    }
                    catch (Exception ex)
                    {
                        header.Text = "Lấy email thất bại: " + ex.Message;
                        Log((IsReMailRow(row) ? "ReMail" : IsSmailrRow(row) ? "Smailr" : "CFWorker") + "Lấy hộp thư thất bại: " + ex.Message);
                    }
                    return;
                }

                header.Text = "Đang làm mới token...";
                try
                {
                    mailItems.Clear();
                    foreach (MailItem item in await FetchBackendInbox(row, 20))
                    {
                        mailItems.Add(item);
                    }
                    header.Text = row.Identifier + " - " + mailItems.Count + " messages";
                }
                catch (Exception ex)
                {
                    header.Text = "Tải thất bại: " + ex.Message;
                    Log("Lỗi tải hộp thư: " + ex.Message);
                }
            }

            refreshBtn.Click += (_, __) => RunUiTask(LoadEmails);
            mailGrid.MouseDoubleClick += (_, __) =>
            {
                if (mailGrid.SelectedItem is MailItem item)
                {
                    ShowMailDetailDialog(item);
                }
            };

            dialog.Content = root;
            dialog.Show();
            await LoadEmails();
        }

        private async Task<List<MailItem>> FetchBackendInbox(PoolRow row, int limit)
        {
            var args = new List<string> { "--desktop-ipc", "--view-inbox", "--email", row.Identifier, "--inbox-limit", limit.ToString(CultureInfo.InvariantCulture) };
            string remailToken = IsReMailRow(row) ? (row.MailboxToken ?? "").Trim() : "";
            string mailboxLine = FindMailboxLineForRow(row);
            if (mailboxLine.Length == 0 && MailboxArgForLine(row.RawLine).Length > 0)
            {
                mailboxLine = row.RawLine;
            }
            string mailboxArg = MailboxArgForLine(mailboxLine);
            string tempMailboxFile = "";
            if (mailboxArg.Length > 0)
            {
                tempMailboxFile = Path.Combine(Path.GetTempPath(), "view_inbox_mailbox_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".txt");
                File.WriteAllText(tempMailboxFile, mailboxLine.Trim() + Environment.NewLine, new UTF8Encoding(false));
                args.AddRange(new[] { mailboxArg, tempMailboxFile });
            }
            AddSessionFileArg(args, row);
            AddMailboxProxy(args);
            var environment = new Dictionary<string, string>();
            if (remailToken.Length > 0)
                environment["REMAIL_SERVICE_TOKEN"] = remailToken;
            try
            {
                BackendCommandResult result = await backendClient.RunAsync(
                    BackendCommand.Create("Xem hộp thư", args, 120000, environment));
                if (!result.Payload.HasValue)
                    throw new InvalidOperationException(BackendFailureMessage(result));
                JsonElement payload = result.Payload.Value;
                if (!payload.TryGetProperty("ok", out JsonElement ok) || !ok.GetBoolean())
                {
                    string error = JsonString(payload, "error");
                    throw new InvalidOperationException(error.Length > 0 ? error : BackendFailureMessage(result));
                }
                var items = new List<MailItem>();
                if (payload.TryGetProperty("messages", out JsonElement messages) && messages.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement msg in messages.EnumerateArray())
                    {
                        string received = JsonString(msg, "receivedDateTime");
                        if (received.Length > 19) received = received.Substring(0, 19).Replace("T", " ");
                        items.Add(new MailItem
                        {
                            ReceivedAt = received,
                            From = JsonString(msg, "from"),
                            Subject = JsonString(msg, "subject"),
                            BodyPreview = JsonString(msg, "bodyPreview"),
                            Body = JsonString(msg, "body"),
                            VerificationCode = JsonString(msg, "verificationCode")
                        });
                    }
                }
                return items;
            }
            finally
            {
                if (tempMailboxFile.Length > 0)
                    TryDeleteFile(tempMailboxFile);
            }
        }

        private static string BackendFailureMessage(BackendCommandResult result)
        {
            string message = string.IsNullOrWhiteSpace(result.StandardError)
                ? SensitiveDataSanitizer.Redact(result.StandardOutput)
                : result.StandardError;
            message = (message ?? "").Trim();
            if (message.Length > 800) message = string.Concat(message.AsSpan(0, 800), "...");
            return message.Length > 0
                ? message
                : $"backend exited with code {result.ExitCode}, but produced no IPC result";
        }

        private bool IsCfWorkerRow(PoolRow row)
        {
            if (row == null) return false;
            return row.MailboxProvider.Equals("cfworker", StringComparison.OrdinalIgnoreCase)
                || row.AccountType.Contains("CFWorker")
                || row.Identifier.EndsWith("@edu.liziai.cloud", StringComparison.OrdinalIgnoreCase)
                || row.Identifier.EndsWith("@liziai.cloud", StringComparison.OrdinalIgnoreCase)
                || row.RawLine.StartsWith("cfworker://", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsReMailRow(PoolRow row)
        {
            if (row == null) return false;
            return row.MailboxProvider.Equals("remail", StringComparison.OrdinalIgnoreCase)
                || row.AccountType.Contains("ReMail", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSmailrRow(PoolRow row)
        {
            if (row == null) return false;
            return row.MailboxProvider.Equals("smailr", StringComparison.OrdinalIgnoreCase)
                || row.AccountType.Contains("Smailr", StringComparison.OrdinalIgnoreCase)
                || row.RawLine.StartsWith("smailr://", StringComparison.OrdinalIgnoreCase);
        }

        private void ShowMailDetailDialog(MailItem item)
        {
            if (item == null) return;
            string content = MailBodyFormatter.ToDisplayText(item.Body, item.BodyPreview);
            if (content.Length == 0) content = "(Nội dung email trống)";
            string code = item.VerificationCode.Length > 0 ? item.VerificationCode : ExtractVerificationCode(content);
            var dialog = new Window
            {
                Title = item.Subject.Length > 0 ? item.Subject : "Chi tiết email",
                Owner = this,
                Width = 720,
                Height = 460,
                MinWidth = 560,
                MinHeight = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (System.Windows.Media.Brush)FindResource("AppBg")
            };

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = item.Subject,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("TextMain")
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var meta = new TextBlock
            {
                Text = item.ReceivedAt + "    " + item.From,
                Margin = new Thickness(0, 6, 0, 10),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSub")
            };
            Grid.SetRow(meta, 1);
            root.Children.Add(meta);

            var body = new TextBox
            {
                Text = content,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Top,
                Height = double.NaN,
                Background = (System.Windows.Media.Brush)FindResource("PanelBg"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextMain"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("Line")
            };
            Grid.SetRow(body, 2);
            root.Children.Add(body);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var copyCodeBtn = new Button { Content = code.Length > 0 ? "Sao chép mã xác minh " + code : "Chưa nhận diện mã xác minh", MinWidth = 120, IsEnabled = code.Length > 0 };
            var copyBodyBtn = new Button { Content = "Sao chép nội dung", Width = 86 };
            var closeBtn = new Button { Content = "Đóng", Width = 72 };
            copyCodeBtn.Click += (_, __) =>
            {
                Clipboard.SetText(code);
                Log("Đã sao chép mã xác minh: " + code);
            };
            copyBodyBtn.Click += (_, __) => Clipboard.SetText(content);
            closeBtn.Click += (_, __) => dialog.Close();
            actions.Children.Add(copyCodeBtn);
            actions.Children.Add(copyBodyBtn);
            actions.Children.Add(closeBtn);
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);

            dialog.Content = root;
            dialog.ShowDialog();
        }

        private string ExtractVerificationCode(string text)
        {
            Match match = Regex.Match(text ?? "", @"(?<!\d)\d{5,8}(?!\d)");
            return match.Success ? match.Value : "";
        }

        private sealed class MailItem
        {
            public string ReceivedAt { get; set; } = "";
            public string From { get; set; } = "";
            public string Subject { get; set; } = "";
            public string BodyPreview { get; set; } = "";
            public string Body { get; set; } = "";
            public string VerificationCode { get; set; } = "";
        }
    }
}
