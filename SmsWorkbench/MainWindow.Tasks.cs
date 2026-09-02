using System.Text.Json;

namespace SmsWorkbench
{
    public partial class MainWindow
    {
        // Backend process, task list, deletion and cancellation actions.
        //
        // CLI argument construction is delegated to BackendCommandPlanner;
        // backend JSON business interpretation is delegated to
        // BackendResultInterpreter.

        private bool doctorProbeStarted;

        // Long-running backend tasks (registration / payment batches) share one
        // timeout budget; keep it a named constant instead of inline math.
        private const int BackendTaskTimeoutMs = 12 * 60 * 60 * 1000;
        private const int BackendTaskTimeoutSeconds = 12 * 60 * 60;
        private DateTime lastHotPersistenceRefreshUtc = DateTime.MinValue;

        /// <summary>
        /// One-shot background environment probe (`python -m sms_tool --doctor --json`)
        /// run straight through the backend client so the single-active-task
        /// invariant is untouched. Surfaces missing interpreter/dependencies
        /// with fix hints instead of letting them surface as per-task failures.
        /// </summary>
        internal async Task RunStartupDoctorProbeAsync()
        {
            if (doctorProbeStarted)
                return;
            doctorProbeStarted = true;
            try
            {
                var command = BackendCommand.Create("doctor", new[] { "--doctor", "--json" }, 90 * 1000);
                BackendCommandResult result = await backendClient.RunAsync(command).ConfigureAwait(true);
                if (!result.Payload.HasValue)
                {
                    Log("[doctor] Tự kiểm tra môi trường không trả về kết quả có cấu trúc (exit code " + result.ExitCode + ")");
                    return;
                }
                var fails = new List<string>();
                int warned = 0;
                foreach (JsonElement check in result.Payload.Value.GetProperty("checks").EnumerateArray())
                {
                    string status = check.TryGetProperty("status", out JsonElement statusElement) ? statusElement.GetString() : "";
                    string name = check.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : "";
                    string hint = check.TryGetProperty("hint", out JsonElement hintElement) ? hintElement.GetString() : "";
                    if (status == "fail")
                        fails.Add(string.IsNullOrEmpty(hint) ? name : $"{name}: {hint}");
                    else if (status == "warn")
                        warned++;
                }
                if (fails.Count == 0)
                {
                    Log($"[doctor] Tự kiểm tra môi trường đạt{(warned > 0 ? $"({warned} cảnh báo, xem chi tiết trong cài đặt và cấu hình proxy)" : "")}");
                    return;
                }
                var detail = string.Join("\n  - ", fails);
                Log("[doctor] Tự kiểm tra môi trường phát hiện " + fails.Count + " dependency bị thiếu");
                MessageBox.Show(
                    this,
                    $"Tự kiểm tra môi trường phát hiện {fails.Count} dependency bắt buộc bị thiếu:\n  - {detail}\n\n" +
                    "Có thể chạy trước: python -m pip install -r requirements.txt\n" +
                    "hoặc dùng lệnh python chatgpt_phone_reg.py --doctor để xem báo cáo đầy đủ.",
                    "Tự kiểm tra môi trường",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log("[doctor] Tự kiểm tra môi trường thất bại: " + ex.Message);
                MessageBox.Show(
                    this,
                    ex.Message + "\n\nỨng dụng desktop phụ thuộc Python 3.10+ và các dependency trong requirements.txt." +
                    "\nSau khi cài, cấu hình đường dẫn interpreter trong Cài đặt → Dữ liệu và file → Môi trường chạy, rồi khởi động lại chương trình.",
                    "Không thể khởi động backend Python",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RerunFailed_Click(object sender, RoutedEventArgs e)
        {
            var failedRows = allRows.Where(r =>
                (r.Status.Contains("Thất bại") || r.Status.Contains("Chờ xử lý")
                    || r.Status.Contains("Thiếu") || r.Status.Contains("thiếu"))
                && IsMailboxPoolLikeRow(r)
                && !string.IsNullOrWhiteSpace(r.RawLine)).ToList();

            if (failedRows.Count == 0)
            {
                MessageBox.Show("Không tìm thấy tài khoản thất bại cần đăng ký lại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Tìm thấy {failedRows.Count} dòng thất bại/chờ xử lý. OK để đăng ký lại?\n\nQuy trình: đăng ký → lấy access token → lưu session vào kho",
                "Xác nhận đăng ký lại", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            if (!TryCreateMailboxFile(failedRows, out string mailboxArg, out string tempFile, out int mailboxCount))
            {
                MessageBox.Show("Bản ghi thất bại thiếu thông tin đăng nhập email khả dụng nên không thể đăng ký lại.", "Định dạng không khớp", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var plan = BackendCommandPlanner.CreateRerunFailedRegistration(
                mailboxArg,
                tempFile,
                mailboxCount,
                GetRegistrationProxyPool());
            RunBackend(plan.TaskName, plan.Arguments.ToList());
        }

        private void RebuildSqlite_Click(object sender, RoutedEventArgs e)
        {
            var plan = BackendCommandPlanner.CreateRebuildSqlite();
            RunBackend(plan.TaskName, plan.Arguments.ToList());
        }

        private void AccountGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (object item in e.AddedItems)
            {
                if (item is PoolRow row) row.IsChecked = true;
            }
        }

        private void AccountDetail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PoolRow row)
            {
                ShowAccountDetail(row);
            }
        }

        private void RunBackend(string taskName, List<string> args)
            => RunUiTask(() => RunBackendAsync(taskName, args));

        private void RunAccountBatchBackend(string taskName, List<string> args, string domain, int total)
            => RunUiTask(() => RunBackendAsync(taskName, args, domain, total));

        private async Task RunBackendAsync(string taskName, List<string> args, string progressDomain = "", int progressTotal = 0)
        {
            if (backendTasks.IsRunning)
            {
                MessageBox.Show("Đang có batch chạy, vui lòng hủy hoặc chờ hoàn tất trước.", "Đang chạy", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string safeArgs = FormatBackendArgsForDisplay(args);
            var task = new TaskRow { Name = "Batch " + taskSeq++, Task = taskName, Status = "Đang chạy", Info = safeArgs };
            Tasks.Add(task);
            ScrollTaskGridToBottom();
            DateTime started = DateTime.Now;
            AccountBatchProgressTracker accountProgress = string.IsNullOrWhiteSpace(progressDomain)
                ? null
                : new AccountBatchProgressTracker(progressDomain, progressTotal);
            AccountBatchProgressDialog progressDialog = accountProgress == null
                ? null
                : new AccountBatchProgressDialog(this, taskName, progressTotal, () => backendTasks.Cancel());
            progressDialog?.Show();

            var backendOutput = new StringBuilder();
            object backendOutputLock = new object();
            void CaptureBackendLine(string line)
            {
                lock (backendOutputLock)
                {
                    backendOutput.AppendLine(line);
                }
            }

            var progress = new Progress<BackendOutputLine>(line =>
            {
                if (BackendProgressEventParser.TryParse(line.Text, out BackendProgressEvent progressEvent))
                {
                    if (accountProgress != null
                        && string.Equals(progressEvent.Domain, accountProgress.Domain, StringComparison.OrdinalIgnoreCase))
                    {
                        accountProgress.Update(progressEvent);
                        progressDialog?.Update(
                            accountProgress.Completed,
                            accountProgress.Total,
                            progressEvent.AccountRef,
                            progressEvent.Detail);
                    }
                    task.Info = progressEvent.Detail.Length > 0
                        ? $"{progressEvent.Stage}: {progressEvent.Detail}"
                        : progressEvent.Stage;
                    return;
                }
                CaptureBackendLine(line.Text);
                UiLog(line.Text);
                RefreshPoolsAfterHotPersistence(line.Text);
            });
            try
            {
                Log("Khởi động: python " + safeArgs);
                StatusText = taskName + " Đang chạy";
                BackendCommandResult result = await backendTasks.RunAsync(
                    BackendCommand.Create(
                        taskName,
                        args,
                        BackendTaskTimeoutMs,
                        new Dictionary<string, string> { ["SMSWORKBENCH_EVENTS"] = "1" }),
                    progress);

                // Use BackendResultInterpreter to normalize the outcome
                BackendExecutionResult interpreted = BackendResultInterpreter.Interpret(
                    result, taskName, BackendTaskTimeoutSeconds);

                task.Status = interpreted.IsSuccess ? "Hoàn tất" : "Thất bại";
                task.Cost = ((int)(DateTime.Now - started).TotalSeconds).ToString(CultureInfo.InvariantCulture);
                task.DoneAt = SafeTime(DateTime.Now);
                StatusText = taskName + " Đã kết thúc";
                RefreshPools();
                ScrollTaskGridToBottom();
                if (taskName.StartsWith("Kiểm tra sống tài khoản", StringComparison.OrdinalIgnoreCase))
                {
                    string output;
                    lock (backendOutputLock)
                    {
                        output = backendOutput.ToString();
                    }
                    ShowAccountScanResultDialog(output);
                }
            }
            catch (OperationCanceledException)
            {
                task.Status = "Đã hủy";
                task.DoneAt = SafeTime(DateTime.Now);
                StatusText = taskName + " Đã hủy";
            }
            catch (BackendTaskAlreadyRunningException)
            {
                task.Status = "Chưa khởi động";
                task.DoneAt = SafeTime(DateTime.Now);
                StatusText = taskName + " Chưa khởi động";
                MessageBox.Show("Đang có batch chạy, vui lòng hủy hoặc chờ hoàn tất trước.", "Đang chạy", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                task.Status = "Khởi động thất bại";
                Log("Khởi động thất bại: " + ex.Message);
            }
            finally
            {
                progressDialog?.Close();
            }
        }

        private async Task<string> RunBackendWithResultAsync(string taskName, List<string> args, int timeoutMs = 120000)
        {
            Log("Khởi động: python " + FormatBackendArgsForDisplay(args));
            return await backendTasks.RunForResultAsync(
                BackendCommand.Create(taskName, args, timeoutMs));
        }

        private static string FormatBackendArgsForDisplay(List<string> args)
        {
            return SensitiveDataSanitizer.RedactArguments(args);
        }

        private void RefreshPoolsAfterHotPersistence(string line)
        {
            if (string.IsNullOrWhiteSpace(line)
                || !line.Contains("Saved session:", StringComparison.OrdinalIgnoreCase))
                return;

            DateTime now = DateTime.UtcNow;
            if ((now - lastHotPersistenceRefreshUtc).TotalMilliseconds < 750)
                return;
            lastHotPersistenceRefreshUtc = now;
            RefreshPools();
        }

        private void TaskGrid_Loaded(object sender, RoutedEventArgs e) => ScrollTaskGridToBottom();

        private void ScrollTaskGridToBottom()
        {
            if (TaskGrid == null || Tasks.Count == 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                object last = Tasks[Tasks.Count - 1];
                TaskGrid.SelectedItem = last;
                TaskGrid.ScrollIntoView(last);
            }), DispatcherPriority.Background);
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
            => RunUiTask(DeleteSelectedAsync);

        private async Task DeleteSelectedAsync()
        {
            var selected = SelectedRowsOrCurrent()
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Identifier))
                .ToList();
            if (selected.Count == 0)
            {
                ShowEmailSelectionRequired("Xóa");
                return;
            }
            if (!await ShowDeleteConfirmDialog(selected.Count)) return;
            BackendCommandPlan plan = null;
            int localRemoved = 0;
            try
            {
                var mailboxRows = selected.Where(IsMailboxPoolFileRow).ToList();
                var accountRows = selected.Except(mailboxRows).ToList();

                localRemoved = DeleteMailboxPoolRows(mailboxRows);

                int failed = 0;
                if (accountRows.Count > 0)
                {
                    var accountEmails = accountRows
                        .Select(row => NormalizeEmailKey(row.Identifier))
                        .Where(email => email.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    plan = BackendCommandPlanner.CreateBatchDeleteAccounts(
                        accountEmails,
                        workers: Math.Min(8, Math.Max(1, accountEmails.Length)));
                    BackendCommandResult backend = await backendTasks.RunAsync(
                        BackendCommand.Create(plan.TaskName, plan.Arguments.ToList(), plan.TimeoutMilliseconds ?? 120000));
                    failed = CountBatchDeleteFailures(backend, accountEmails.Length);
                }

                int mailboxFailed = Math.Max(0, mailboxRows.Count - localRemoved);
                failed += mailboxFailed;
                if (failed > 0)
                {
                    await DialogFactory.ShowInfoAsync(
                        this,
                        "Xóa chưa hoàn tất",
                        failed + " bản ghi chưa xóa hoàn toàn. Vui lòng xem log chạy.");
                }
                else
                {
                    string message = localRemoved > 0
                        ? $"Đã xóa {localRemoved} dòng mail khỏi file nguồn."
                        : "Đã xóa các mục đã chọn.";
                    Log(message);
                }
            }
            catch (Exception ex)
            {
                Log("Xóa hàng loạt thất bại: " + SensitiveDataSanitizer.Redact(ex.Message));
                await DialogFactory.ShowInfoAsync(this, "Xóa thất bại", "Xóa hàng loạt chưa hoàn tất, vui lòng xem log chạy.");
            }
            finally
            {
                if (plan != null)
                {
                    foreach (string path in plan.TempFiles)
                        TryDeleteFile(path);
                }
                RefreshPools();
            }
        }

        private bool IsMailboxPoolFileRow(PoolRow row)
        {
            if (row == null) return false;
            string path = FirstNonEmpty(row.SourcePath, row.Notes);
            if (path.Length == 0 || !File.Exists(path)) return false;
            string extension = Path.GetExtension(path);
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".db", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return MailboxPoolFileStore.IsMailboxPoolLike(row.AccountType, row.MailboxProvider)
                && FirstNonEmpty(row.MailboxLine, row.RawLine).Length > 0;
        }

        private int DeleteMailboxPoolRows(IEnumerable<PoolRow> rows)
        {
            int removed = 0;
            foreach (PoolRow row in rows)
            {
                string path = FirstNonEmpty(row.SourcePath, row.Notes);
                string exactLine = FirstNonEmpty(row.MailboxLine, row.RawLine);
                int count = MailboxPoolFileStore.DeleteMatchingLines(
                    path,
                    NormalizeEmailKey(row.Identifier),
                    new[] { exactLine });
                removed += Math.Min(1, count);
                if (count > 0)
                    Log("Đã xóa mail khỏi pool: " + row.Identifier);
                else
                    Log("Không tìm thấy dòng mail để xóa: " + row.Identifier);
            }
            return removed;
        }

        private static int CountBatchDeleteFailures(BackendCommandResult backend, int expected)
        {
            if (backend.ExitCode != 0 || !backend.Payload.HasValue)
                return expected;
            JsonElement payload = backend.Payload.Value;
            if (payload.TryGetProperty("failed", out JsonElement failed) && failed.ValueKind == JsonValueKind.Number)
                return Math.Max(0, failed.GetInt32());
            return payload.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True
                ? 0
                : expected;
        }

        private async Task<bool> ShowDeleteConfirmDialog(int count)
        {
            return await DialogFactory.ShowConfirmAsync(
                this,
                "Xóa " + count + " dòng bản ghi đã chọn?",
                "Sẽ đồng bộ dọn pool email cục bộ, index SQLite và file session khớp. Thao tác này không thể hoàn tác.",
                "Xóa",
                isDanger: true);
        }

        private bool TryDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Log("Xóa file thất bại: " + SensitiveDataSanitizer.Redact(path) + " " + SensitiveDataSanitizer.Redact(ex.Message));
                return false;
            }
        }

        private void CancelBatch_Click(object sender, RoutedEventArgs e)
        {
            if (!backendTasks.IsRunning)
            {
                Log("Hiện không có batch nào đang chạy.");
                return;
            }
            try
            {
                if (backendTasks.Cancel())
                    Log("Đã hủy batch hiện tại.");
            }
            catch (Exception ex)
            {
                Log("Hủy thất bại: " + ex.Message);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPools();

        private void Settings_Click(object sender, RoutedEventArgs e) => ShowConfigDialog();
    }
}
