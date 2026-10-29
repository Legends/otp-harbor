namespace TOTP.Core.Services.Interfaces;

public interface IApplicationVersionProvider
{
    Version CurrentVersion { get; }
}
