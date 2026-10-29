namespace TOTP.Core.Models;

public enum ImportConflictStrategy
{
    SkipExisting = 1,
    ReplaceExisting = 2,
    KeepBoth = 3
}
