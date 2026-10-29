namespace TOTP.Core.Enums
{
    public enum ValidationError
    {
        None,
        PlatformRequired,
        PlatformAlreadyExists,
        SecretRequired,
        SecretInvalidFormat,
        IdRequired
    }
}
