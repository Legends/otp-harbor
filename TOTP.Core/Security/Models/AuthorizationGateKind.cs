namespace TOTP.Core.Security.Models;

public enum AuthorizationGateKind
{
    None = 0,
    Hello = 1,
    Password = 2,
    DeviceCredential = 3
}
