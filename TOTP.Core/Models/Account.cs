using System;
using System.Text.Json.Serialization;
using TOTP.Core.Validation;

namespace TOTP.Core.Models
{
    public sealed class Account : IEquatable<Account>
    {
        [JsonPropertyName("id")]
        public Guid ID { get; set; }

        [JsonPropertyName("issuer")]
        public string Issuer { get; }

        [JsonPropertyName("secret")]
        public string Secret { get; }

        [JsonPropertyName("account_name")]
        public string? AccountName { get; }

        [JsonPropertyName("period")]
        public int PeriodSeconds { get; }

        [JsonPropertyName("group")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AccountGroup? Group { get; }

        // JsonConstructor wird benötigt, da die Properties nur 'get' haben
        [JsonConstructor]
        public Account(
            Guid id,
            string issuer,
            string secret,
            string? accountName = null,
            int periodSeconds = TotpPeriodPolicy.DefaultSeconds,
            AccountGroup? group = null)
        {
            ID = id;
            Issuer = issuer;
            Secret = secret;
            AccountName = accountName;
            PeriodSeconds = periodSeconds;
            Group = group;
        }

        public Account WithGroup(AccountGroup? group) =>
            new(ID, Issuer, Secret, AccountName, PeriodSeconds, group);

        public bool Equals(Account? other) => other is not null && ID == other.ID;
        public override bool Equals(object? obj) => Equals(obj as Account);
        public override int GetHashCode() => ID.GetHashCode();
    }
}
