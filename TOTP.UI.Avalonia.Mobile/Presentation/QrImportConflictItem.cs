using System.ComponentModel;
using System.Runtime.CompilerServices;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed class QrImportConflictItem : INotifyPropertyChanged
{
    private QrAccountConflictDecision _decision = QrAccountConflictDecision.Skip;

    public QrImportConflictItem(
        QrAccountConflict conflict,
        string changedFieldsFormat,
        string secretField,
        string periodField,
        string skipAutomationFormat,
        string replaceAutomationFormat,
        string keepBothAutomationFormat)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ImportIndex = conflict.ImportIndex;
        DisplayName = string.IsNullOrWhiteSpace(conflict.AccountName)
            ? conflict.Issuer
            : $"{conflict.Issuer}: {conflict.AccountName}";
        var changedFields = new List<string>(2);
        if (conflict.SecretChanged) changedFields.Add(secretField);
        if (conflict.PeriodChanged) changedFields.Add(periodField);
        ChangedFieldsText = string.Format(
            changedFieldsFormat,
            string.Join(", ", changedFields));
        SkipAutomationName = string.Format(skipAutomationFormat, DisplayName);
        ReplaceAutomationName = string.Format(replaceAutomationFormat, DisplayName);
        KeepBothAutomationName = string.Format(keepBothAutomationFormat, DisplayName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int ImportIndex { get; }

    public string DisplayName { get; }

    public string ChangedFieldsText { get; }

    public string SkipAutomationName { get; }

    public string ReplaceAutomationName { get; }

    public string KeepBothAutomationName { get; }

    public bool IsSkipSelected
    {
        get => _decision == QrAccountConflictDecision.Skip;
        set
        {
            if (value) Select(QrAccountConflictDecision.Skip);
        }
    }

    public bool IsReplaceSelected
    {
        get => _decision == QrAccountConflictDecision.UpdateExisting;
        set
        {
            if (value) Select(QrAccountConflictDecision.UpdateExisting);
        }
    }

    public bool IsKeepBothSelected
    {
        get => _decision == QrAccountConflictDecision.KeepBoth;
        set
        {
            if (value) Select(QrAccountConflictDecision.KeepBoth);
        }
    }

    public QrAccountImportConflictResolution ToResolution() =>
        new(ImportIndex, _decision);

    public void Select(QrAccountConflictDecision decision)
    {
        if (_decision == decision) return;
        _decision = decision;
        OnPropertyChanged(nameof(IsSkipSelected));
        OnPropertyChanged(nameof(IsReplaceSelected));
        OnPropertyChanged(nameof(IsKeepBothSelected));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
