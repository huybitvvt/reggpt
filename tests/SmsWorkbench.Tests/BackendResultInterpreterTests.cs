using System.Text.Json;
using SmsWorkbench;

namespace SmsWorkbench.Tests;

public sealed class BackendResultInterpreterTests
{
    // ── Scan summary extraction ─────────────────────────────────────────

    [Fact]
    public void TryExtractScanSummary_ReturnsNullForEmptyOutput()
    {
        Assert.Null(BackendResultInterpreter.TryExtractScanSummary(""));
        Assert.Null(BackendResultInterpreter.TryExtractScanSummary("   "));
        Assert.Null(BackendResultInterpreter.TryExtractScanSummary("plain text without JSON"));
    }

    [Fact]
    public void TryExtractScanSummary_FindsLastJsonWithResultsAndTotal()
    {
        string output = """
            [info] processing...
            {"results": [], "total": 5, "alive": 3, "account_deactivated": 1}
            """;
        var summary = BackendResultInterpreter.TryExtractScanSummary(output);
        Assert.NotNull(summary);
        Assert.Equal("5", BackendJson.GetString(summary, "total"));
        Assert.Equal("3", BackendJson.GetString(summary, "alive"));
    }

    [Fact]
    public void TryExtractScanSummary_IgnoresJsonWithoutResults()
    {
        string output = """
            {"some": "data"}
            """;
        Assert.Null(BackendResultInterpreter.TryExtractScanSummary(output));
    }

    // ── Deactivation detection ──────────────────────────────────────────

    [Fact]
    public void IsProbeDeactivated_DetectsDeactivatedInRow()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "account_deactivated"
        };
        Assert.True(BackendResultInterpreter.IsProbeDeactivated(row));
    }

    [Fact]
    public void IsProbeDeactivated_DetectsDeactivatedInProbe()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["probe"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = "deactivated"
            }
        };
        Assert.True(BackendResultInterpreter.IsProbeDeactivated(row));
    }

    [Fact]
    public void IsProbeDeactivated_DetectsDeactivatedInRelogin()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["relogin"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["error"] = "account has been deactivated"
            }
        };
        Assert.True(BackendResultInterpreter.IsProbeDeactivated(row));
    }

    [Fact]
    public void IsProbeDeactivated_ReturnsFalseForAliveRow()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "alive"
        };
        Assert.False(BackendResultInterpreter.IsProbeDeactivated(row));
    }

    [Fact]
    public void IsProbeDeactivated_ReturnsFalseForNull()
    {
        Assert.False(BackendResultInterpreter.IsProbeDeactivated(null!));
    }

    // ── Probe status labels ─────────────────────────────────────────────

    [Fact]
    public void ProbeStatusLabel_ReturnsDeactivated()
    {
        var probe = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "account_deactivated"
        };
        Assert.Equal("Tài khoản bị dừng", BackendResultInterpreter.ProbeStatusLabel(probe));
    }

    [Fact]
    public void ProbeStatusLabel_Returns401()
    {
        var probe = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["status_code"] = "401"
        };
        Assert.Equal("AT hết hiệu lực / HTTP 401", BackendResultInterpreter.ProbeStatusLabel(probe));
    }

    [Fact]
    public void ProbeStatusLabel_ReturnsOkWithStatusCode()
    {
        var probe = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = true,
            ["status_code"] = "200"
        };
        Assert.Equal("AT hợp lệ / HTTP 200", BackendResultInterpreter.ProbeStatusLabel(probe));
    }

    [Fact]
    public void ProbeStatusLabel_ReturnsOkWithoutStatusCode()
    {
        var probe = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = true
        };
        Assert.Equal("AT hợp lệ", BackendResultInterpreter.ProbeStatusLabel(probe));
    }

    [Fact]
    public void ProbeStatusLabel_ReturnsFailedWithStatusCode()
    {
        var probe = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["status_code"] = "500"
        };
        Assert.Equal("Kiểm tra sống thất bại / HTTP 500", BackendResultInterpreter.ProbeStatusLabel(probe));
    }

    // ── IsProbeSucceeded / IsProbeReturned401 ───────────────────────────

    [Fact]
    public void IsProbeSucceeded_ReturnsTrueWhenOk()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["probe"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["ok"] = true
            }
        };
        Assert.True(BackendResultInterpreter.IsProbeSucceeded(row));
    }

    [Fact]
    public void IsProbeSucceeded_ReturnsFalseWhenMissing()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        Assert.False(BackendResultInterpreter.IsProbeSucceeded(row));
    }

    [Fact]
    public void IsProbeReturned401_ReturnsTrueForStatusCode401()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["probe"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["status_code"] = "401"
            }
        };
        Assert.True(BackendResultInterpreter.IsProbeReturned401(row));
    }

    [Fact]
    public void IsProbeReturned401_ReturnsFalseWithoutProbe()
    {
        Assert.False(BackendResultInterpreter.IsProbeReturned401(
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)));
    }

    // ── ScanResultError ─────────────────────────────────────────────────

    [Fact]
    public void ScanResultError_FindsOauthError()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["oauth"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["error"] = "token expired"
            }
        };
        Assert.Equal("token expired", BackendResultInterpreter.ScanResultError(row));
    }

    [Fact]
    public void ScanResultError_FindsRefreshError()
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["refresh"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["error"] = "network error"
            }
        };
        Assert.Equal("network error", BackendResultInterpreter.ScanResultError(row));
    }

    [Fact]
    public void ScanResultError_ReturnsEmptyWhenNoError()
    {
        Assert.Equal("", BackendResultInterpreter.ScanResultError(
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)));
    }

    // ── ScanStatusLabel ─────────────────────────────────────────────────

    [Theory]
    [InlineData("alive", "Bình thường")]
    [InlineData("alive_probe_inconclusive", "RT bình thường / kiểm tra sâu OAuth chưa hoàn tất")]
    [InlineData("account_deactivated", "Tài khoản bị vô hiệu")]
    [InlineData("secondary_phone_verification_required", "Xác minh điện thoại")]
    [InlineData("phone_verification_required", "Thanh toán hoàn tất")]
    [InlineData("scan_failed", "Quét thất bại")]
    [InlineData("unknown_status", "unknown_status")]
    [InlineData("", "Không rõ")]
    public void ScanStatusLabel_ReturnsCorrectLabel(string input, string expected)
    {
        Assert.Equal(expected, BackendResultInterpreter.ScanStatusLabel(input));
    }

    // ── Proxy test results ──────────────────────────────────────────────

    [Fact]
    public void ParseProxyTestResult_ParsesFullResult()
    {
        string json = """
            {
              "ok": true,
              "stages": {
                "checkout": { "ip": "1.2.3.4", "country_code": "US", "expected_country": "US", "error": "" },
                "approve": { "ip": "5.6.7.8", "country_code": "TR", "expected_country": "TR", "error": "" },
                "update": { "ip": "9.10.11.12", "country_code": "TR", "expected_country": "TR", "error": "timeout" }
              }
            }
            """;
        var result = BackendResultInterpreter.ParseProxyTestResult(json);

        Assert.True(result.AllOk);
        Assert.Equal(3, result.Stages.Count);
        Assert.Equal("checkout", result.Stages[0].Stage);
        Assert.Equal("1.2.3.4", result.Stages[0].Ip);
        Assert.Equal("TR", result.Stages[2].ExpectedCountry);
        Assert.Equal("timeout", result.Stages[2].Error);
    }

    [Fact]
    public void ParseProxyTestResult_HandlesNonJson()
    {
        var result = BackendResultInterpreter.ParseProxyTestResult("not json");
        Assert.False(result.AllOk);
        Assert.Empty(result.Stages);
    }

    [Fact]
    public void ParseProxyTestResult_HandlesEmptyStages()
    {
        var result = BackendResultInterpreter.ParseProxyTestResult("{\"ok\": true}");
        Assert.True(result.AllOk);
        Assert.Empty(result.Stages);
    }

    // ── BackendExecutionResult ──────────────────────────────────────────

    [Fact]
    public void Interpret_ReturnsTimedOut()
    {
        var result = new BackendCommandResult(0, "", "", null, TimedOut: true);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.False(interpreted.IsSuccess);
        Assert.Equal("timed_out", interpreted.State);
        Assert.Contains("timeout", interpreted.DisplayText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_ReturnsFailedWithStandardError()
    {
        var result = new BackendCommandResult(1, "", "something went wrong", null, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.False(interpreted.IsSuccess);
        Assert.Equal("failed", interpreted.State);
        Assert.Contains("something went wrong", interpreted.DisplayText);
        Assert.Contains("Tham số", interpreted.DisplayText);
    }

    [Fact]
    public void Interpret_ReturnsFailedWithNonZeroExitCode()
    {
        var result = new BackendCommandResult(-1, "error output", "", null, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.False(interpreted.IsSuccess);
        Assert.Equal("failed", interpreted.State);
    }

    [Fact]
    public void Interpret_ExitTwoSurfacesPreconditionCategory()
    {
        var result = new BackendCommandResult(2, "", "no mailbox account was found", null, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.False(interpreted.IsSuccess);
        Assert.Equal("failed", interpreted.State);
        Assert.Contains("kiểm tra điều kiện", interpreted.DisplayText);
        Assert.Contains("no mailbox account was found", interpreted.DisplayText);
    }

    [Fact]
    public void Interpret_ExitThreeSurfacesRuntimeCategory()
    {
        var result = new BackendCommandResult(3, "", "extraction failed", null, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.False(interpreted.IsSuccess);
        Assert.Equal("failed", interpreted.State);
        Assert.Contains("runtime", interpreted.DisplayText);
    }

    [Fact]
    public void Interpret_ExitZeroWithStderrRemainsSuccess()
    {
        // Progress/diagnostics on stderr are normal for successful backend
        // runs (e.g. --view-inbox redirects progress output to stderr).
        var payload = JsonDocument.Parse("{\"ok\": true}").RootElement;
        var result = new BackendCommandResult(0, "", "[*] fetching messages", payload, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.True(interpreted.IsSuccess);
        Assert.Equal("completed", interpreted.State);
    }

    [Fact]
    public void Interpret_ExitZeroPlainOutputWithStderrRemainsSuccess()
    {
        var result = new BackendCommandResult(0, "plain output", "diagnostic line", null, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.True(interpreted.IsSuccess);
        Assert.Equal("plain output", interpreted.DisplayText);
    }

    [Fact]
    public void Interpret_ExitZeroWithoutOutputFallsBackToStderrText()
    {
        var result = new BackendCommandResult(0, "", "only diagnostics", null, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.True(interpreted.IsSuccess);
        Assert.Equal("only diagnostics", interpreted.DisplayText);
    }

    [Fact]
    public void Interpret_NonZeroExitCodeRetainsStructuredPayload()
    {
        var payload = JsonDocument.Parse(
            "{\"ok\": false, \"decision_text\": \"account is not eligible\"}").RootElement;
        var result = new BackendCommandResult(3, payload.GetRawText(), "", payload, false);

        BackendExecutionResult interpreted = BackendResultInterpreter.Interpret(result, "payment");

        Assert.False(interpreted.IsSuccess);
        Assert.True(interpreted.Payload.HasValue);
        Assert.Equal("account is not eligible", interpreted.Payload.Value.GetProperty("decision_text").GetString());
    }

    [Fact]
    public void Interpret_ReturnsPayloadJson()
    {
        var payload = JsonDocument.Parse("{\"ok\": true, \"url\": \"https://example.com\"}").RootElement;
        var result = new BackendCommandResult(0, "", "", payload, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.True(interpreted.IsSuccess);
        Assert.Equal("completed", interpreted.State);
        Assert.NotNull(interpreted.Payload);
        Assert.True(interpreted.Payload.Value.TryGetProperty("ok", out var ok) && ok.GetBoolean());
    }

    [Fact]
    public void Interpret_ReturnsStandardOutputWhenNoPayload()
    {
        var result = new BackendCommandResult(0, "plain output", "", null, false);
        var interpreted = BackendResultInterpreter.Interpret(result, "test");

        Assert.True(interpreted.IsSuccess);
        Assert.Equal("plain output", interpreted.DisplayText);
    }

    [Fact]
    public void Cancelled_ReturnsCancelledState()
    {
        var cancelled = BackendResultInterpreter.Cancelled("test");
        Assert.False(cancelled.IsSuccess);
        Assert.Equal("cancelled", cancelled.State);
        Assert.Contains("Đã hủy", cancelled.DisplayText);
    }

    [Fact]
    public void StartupFailed_ReturnsFailedState()
    {
        var failed = BackendResultInterpreter.StartupFailed("test", "cannot find python");
        Assert.False(failed.IsSuccess);
        Assert.Equal("failed", failed.State);
        Assert.Contains("cannot find python", failed.DisplayText);
    }
}
