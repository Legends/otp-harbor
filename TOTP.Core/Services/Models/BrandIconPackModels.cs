namespace TOTP.Core.Services.Models;

public sealed record BrandDefinition(
    string Id,
    string DisplayName,
    string BackgroundColor,
    string IconFileName);

public sealed record BrandIconPackStatus(
    bool IsInstalled,
    string? Version,
    int BrandCount);

public sealed record BrandIconPackImportResult(
    string Version,
    int BrandCount);
