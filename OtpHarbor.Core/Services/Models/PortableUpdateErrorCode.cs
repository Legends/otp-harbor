namespace TOTP.Core.Services.Models;

public enum PortableUpdateErrorCode
{
    Unknown = 0,
    ConfigurationInvalid,
    FeedUnavailable,
    FeedVerificationFailed,
    OfferIncomplete
}
