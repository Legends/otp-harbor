using FluentResults;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TOTP.Core.Enums;
using TOTP.Core.Models;

namespace TOTP.Core.Services.Interfaces;

/// <summary>
/// Basically a higher level manager that uses ISecretsManager to perform TOTP related operations.
/// Operations:
/// Add new
/// Update
/// Delete
/// </summary>
public interface IAccountManager
{
    Task<Result> BackupOtpEntriesStorageFileAsync();
    Task<Result> AddNewAsync(Account newItem);
    Task<Result<IReadOnlyList<Account>>> GetAllOtpEntriesSortedAsync();

    ///// <summary>
    ///// Adds a new secret item by prompting the user for key and value.
    ///// It writes the new item to the secrets file and returns the item if successful.
    ///// </summary>
    ///// <returns></returns>
    //Task<(bool isSuccess, SecretItem? item)> AddNewSecretAsync();

    /// <summary>
    /// Updates an existing secret item in the encrypted secrets file.
    /// And manages user message handling via OnMessageSend event.
    /// </summary>
    /// <param name="previous"></param>
    /// <param name="updated"></param>
    /// <returns></returns>
    Task<Result> UpdateAsync(Account previous, Account updated);
    Task<Result> SaveGroupAsync(AccountGroup group, IReadOnlyCollection<Guid> accountIds);
    Task<Result> DeleteGroupAsync(Guid groupId);

    /// <summary>
    /// Deletes a secret item from the encrypted secrets file.
    /// </summary>
    /// <param name="item">SecretItem</param>
    /// <returns>true/false</returns>
    Task<Result> DeleteAsync(Account item);


}
