namespace TOTP.Core.Services.Interfaces;

/// <summary>Prepares local icon matches for every loaded account without building the icon picker.</summary>
public interface IAccountBrandIconInitializer
{
    Task InitializeForAccountsAsync(
        IReadOnlyCollection<string> issuers,
        CancellationToken cancellationToken = default);
}
