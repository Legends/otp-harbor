namespace TOTP.Core.Security.Interfaces;

public interface ISecurityContext
{
    bool IsUnlocked { get; }
    void SetDek(byte[] dek);
    byte[] GetDekCopy();
    void Lock();
}
