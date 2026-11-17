using FluentResults;
using TOTP.Core.Models;

namespace TOTP.Core.Services.Models;

public sealed record AccountImportPasswordRequest(
    bool UsesMasterPassword,
    Func<string, CancellationToken, Task<Result<List<Account>>>> TryReadAsync);
