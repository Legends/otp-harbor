namespace TOTP.Core.Security.Interfaces;

public interface IHelloPromptWindowHandleProvider
{
    nint GetActiveWindowHandle();
    string VerificationMessage { get; }
    IDisposable BeginPrompt();
}
