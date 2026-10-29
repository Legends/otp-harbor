using System.Text.Json.Serialization;

namespace TOTP.Core.Models;

public sealed record AccountGroup
{
    [JsonPropertyName("id")]
    public Guid Id { get; }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("color")]
    public string Color { get; }

    [JsonConstructor]
    public AccountGroup(Guid id, string name, string color)
    {
        Id = id;
        Name = name;
        Color = color;
    }
}
