using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TOTP.Core.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IVaultService
{
    byte[] EncryptVault(List<Account> entries);
    List<Account> DecryptVault(byte[] encryptedBlob);
}