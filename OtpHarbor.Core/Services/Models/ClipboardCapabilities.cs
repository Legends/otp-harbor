namespace TOTP.Core.Services.Models;

[Flags]
public enum ClipboardCapabilities
{
    None = 0,
    WriteText = 1,
    ConditionalClear = 2
}
