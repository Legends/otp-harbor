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

        [JsonPropertyName("groups")]
        public IReadOnlyList<AccountGroup> Groups { get; }

        [JsonPropertyName("favorite")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsFavorite { get; }

        // JsonConstructor wird benötigt, da die Properties nur 'get' haben
        [JsonConstructor]
        public Account(
            Guid id,
            string issuer,
            string secret,
            string? accountName = null,
            int periodSeconds = TotpPeriodPolicy.DefaultSeconds,
            AccountGroup? group = null,
            bool isFavorite = false,
            IReadOnlyList<AccountGroup>? groups = null)
        {
            ID = id;
            Issuer = issuer;
            Secret = secret;
            AccountName = accountName;
            PeriodSeconds = periodSeconds;
            var resolvedGroups = new List<AccountGroup>();
            if (group is not null) resolvedGroups.Add(group);
            if (groups is not null)
            {
                foreach (var candidate in groups)
                {
                    if (candidate is not null
                        && resolvedGroups.All(existing => existing.Id != candidate.Id))
                    {
                        resolvedGroups.Add(candidate);
                    }
                }
            }
            Groups = resolvedGroups.ToArray();
            Group = Groups.FirstOrDefault();
            IsFavorite = isFavorite;
        }

        public Account WithGroup(AccountGroup? group) =>
            WithGroups(group is null ? [] : [group]);

        public Account WithGroups(IEnumerable<AccountGroup> groups) =>
            new(
                ID,
                Issuer,
                Secret,
                AccountName,
                PeriodSeconds,
                isFavorite: IsFavorite,
                groups: groups.ToArray());

        public Account WithGroupMembership(AccountGroup group, bool isMember)
        {
            ArgumentNullException.ThrowIfNull(group);
            var updated = Groups
                .Where(existing => existing.Id != group.Id)
                .ToList();
            if (isMember) updated.Add(group);
            return WithGroups(updated);
        }

        public bool IsInGroup(Guid groupId) => Groups.Any(group => group.Id == groupId);

        public Account WithFavorite(bool isFavorite) =>
            new(
                ID,
                Issuer,
                Secret,
                AccountName,
                PeriodSeconds,
                isFavorite: isFavorite,
                groups: Groups);

        public bool Equals(Account? other) => other is not null && ID == other.ID;
        public override bool Equals(object? obj) => Equals(obj as Account);
        public override int GetHashCode() => ID.GetHashCode();
    }
}
