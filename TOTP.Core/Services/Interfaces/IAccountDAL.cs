using FluentResults;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TOTP.Core.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IAccountDAL : IDisposable
{
    Task<Result<List<Account>>> GetAllAsync();
    Task<Result> AddNewAsync(Account newItem);
    Task<Result> UpdateAsync(Account updated);
    Task<Result> SaveGroupAsync(AccountGroup group, IReadOnlyCollection<Guid> accountIds);
    Task<Result> DeleteGroupAsync(Guid groupId);
    Task<Result> DeleteAsync(Account otp);
    Task<Result> BackupOtpEntriesStorageFileAsync();
    // Added for professional key rotation
    Task<Result> ReEncryptStorageAsync();
}
