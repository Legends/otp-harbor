using System.ComponentModel;
using System.Runtime.CompilerServices;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed class BackupImportConflictItem : INotifyPropertyChanged
{
    private bool _replaceExisting;

    public BackupImportConflictItem(
        AccountImportConflict conflict,
        string currentAccountFormat,
        string backupAccountFormat,
        string changedFieldsFormat,
        string issuerField,
        string accountNameField,
        string secretField,
        string periodField,
        string keepAccountAutomationFormat,
        string restoreAccountAutomationFormat)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentAccountFormat);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupAccountFormat);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedFieldsFormat);
        ImportIndex = conflict.ImportIndex;
        CurrentDisplayName = string.Format(
            currentAccountFormat,
            FormatDisplayName(conflict.CurrentIssuer, conflict.CurrentAccountName));
        BackupDisplayName = string.Format(
            backupAccountFormat,
            FormatDisplayName(conflict.BackupIssuer, conflict.BackupAccountName));
        var changedFields = new List<string>(4);
        if (conflict.IssuerChanged) changedFields.Add(issuerField);
        if (conflict.AccountNameChanged) changedFields.Add(accountNameField);
        if (conflict.SecretChanged) changedFields.Add(secretField);
        if (conflict.PeriodChanged) changedFields.Add(periodField);
        ChangedFieldsText = string.Format(
            changedFieldsFormat,
            string.Join(", ", changedFields));
        KeepAutomationName = string.Format(
            keepAccountAutomationFormat,
            CurrentDisplayName);
        RestoreAutomationName = string.Format(
            restoreAccountAutomationFormat,
            BackupDisplayName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int ImportIndex { get; }

    public string CurrentDisplayName { get; }

    public string BackupDisplayName { get; }

    public string ChangedFieldsText { get; }

    public string KeepAutomationName { get; }

    public string RestoreAutomationName { get; }

    public bool IsSkipSelected
    {
        get => !_replaceExisting;
        set
        {
            if (!value || !_replaceExisting) return;
            _replaceExisting = false;
            NotifySelectionChanged();
        }
    }

    public bool IsReplaceSelected
    {
        get => _replaceExisting;
        set
        {
            if (!value || _replaceExisting) return;
            _replaceExisting = true;
            NotifySelectionChanged();
        }
    }

    public AccountImportConflictResolution ToResolution() =>
        new(
            ImportIndex,
            _replaceExisting
                ? AccountImportConflictAction.Replace
                : AccountImportConflictAction.Skip);

    public void Select(AccountImportConflictAction action)
    {
        var replace = action == AccountImportConflictAction.Replace;
        if (_replaceExisting == replace) return;
        _replaceExisting = replace;
        NotifySelectionChanged();
    }

    private static string FormatDisplayName(string issuer, string accountName) =>
        string.IsNullOrWhiteSpace(accountName) ? issuer : $"{issuer}: {accountName}";

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(IsSkipSelected));
        OnPropertyChanged(nameof(IsReplaceSelected));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
