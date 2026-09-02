using SmsWorkbench;

namespace SmsWorkbench.Tests;

public sealed class AccountCredentialExportTests
{
    [Fact]
    public void CompleteCredentialRecordExportsExactlyThreeFields()
    {
        var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = "apple@example.com",
            ["password"] = "Password!123",
            ["totp_secret"] = "JBSWY3DPEHPK3PXP",
        };

        bool ok = AccountCredentialExport.TryBuildLoginLine(
            data,
            "",
            requireTotp: true,
            out string line,
            out string missing);

        Assert.True(ok);
        Assert.Equal("", missing);
        Assert.Equal("apple@example.com|Password!123|JBSWY3DPEHPK3PXP", line);
    }

    [Fact]
    public void FilteredCredentialExportRejectsAccountWithoutTotp()
    {
        var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = "card@example.com",
            ["password"] = "Password!123",
        };

        bool ok = AccountCredentialExport.TryBuildLoginLine(
            data,
            "",
            requireTotp: true,
            out string line,
            out string missing);

        Assert.False(ok);
        Assert.Equal("", line);
        Assert.Equal("totp_secret", missing);
    }

    [Fact]
    public void ExportRejectsSeparatorInjectionInsteadOfCorruptingFileShape()
    {
        var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["password"] = "bad|password",
            ["totp_secret"] = "JBSWY3DPEHPK3PXP",
        };

        bool ok = AccountCredentialExport.TryBuildLoginLine(
            data,
            "fallback@example.com",
            requireTotp: true,
            out _,
            out string missing);

        Assert.False(ok);
        Assert.Equal("invalid_separator", missing);
    }
}
