namespace TOTP.Core.Services.Interfaces;

public interface IApplicationLifetime
{
    void Shutdown(int exitCode = 0);
    void ExitProcess(int exitCode = 0);
}
