using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class QrImportConflictItemTests
{
    [Fact]
    public void ConflictItem_DefaultsToSkipAndSupportsEveryExplicitDecision()
    {
        var item = new QrImportConflictItem(
            new QrAccountConflict(4, "Example", "user", true, true),
            "Changed: {0}",
            "secret",
            "period",
            "Skip {0}",
            "Replace {0}",
            "Keep both {0}");

        Assert.True(item.IsSkipSelected);
        Assert.Equal(QrAccountConflictDecision.Skip, item.ToResolution().Decision);

        item.Select(QrAccountConflictDecision.UpdateExisting);

        Assert.True(item.IsReplaceSelected);
        Assert.Equal(QrAccountConflictDecision.UpdateExisting, item.ToResolution().Decision);

        item.Select(QrAccountConflictDecision.KeepBoth);

        Assert.True(item.IsKeepBothSelected);
        Assert.Equal(QrAccountConflictDecision.KeepBoth, item.ToResolution().Decision);
    }

    [Fact]
    public void ConflictItem_DescribesChangedFieldsWithoutSecretValues()
    {
        const string secret = "JBSWY3DPEHPK3PXP";
        var item = new QrImportConflictItem(
            new QrAccountConflict(0, "Example", "user", true, false),
            "Changed: {0}",
            "secret",
            "period",
            "Skip {0}",
            "Replace {0}",
            "Keep both {0}");

        Assert.Equal("Changed: secret", item.ChangedFieldsText);
        Assert.DoesNotContain(secret, item.ChangedFieldsText, StringComparison.Ordinal);
    }
}
