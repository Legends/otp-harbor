using System.Text.Json;
using FluentResults;

namespace TOTP.Core.Backup;

public static class AutomaticBackupConfigurationV1Codec
{
    public const int MaximumPayloadSize = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 8,
        WriteIndented = true
    };

    public static Result<byte[]> Serialize(AutomaticBackupConfigurationV1 configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var validation = Validate(configuration);
        if (validation.IsFailed) return Result.Fail<byte[]>(validation.Errors);

        var payload = JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions);
        return payload.Length <= MaximumPayloadSize
            ? Result.Ok(payload)
            : Fail<byte[]>(AutomaticBackupErrorCode.TooLarge, "Automatic backup configuration exceeds the size limit.");
    }

    public static Result<AutomaticBackupConfigurationV1> Deserialize(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadSize)
            return Fail<AutomaticBackupConfigurationV1>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup configuration size is invalid.");

        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            if (HasDuplicateProperty(document.RootElement))
                return Fail<AutomaticBackupConfigurationV1>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup configuration contains duplicate properties.");

            var configuration = JsonSerializer.Deserialize<AutomaticBackupConfigurationV1>(payload.Span, JsonOptions);
            if (configuration is null)
                return Fail<AutomaticBackupConfigurationV1>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup configuration is null.");

            var validation = Validate(configuration);
            return validation.IsSuccess
                ? Result.Ok(configuration)
                : Result.Fail<AutomaticBackupConfigurationV1>(validation.Errors);
        }
        catch (JsonException)
        {
            return Fail<AutomaticBackupConfigurationV1>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup configuration JSON is invalid.");
        }
    }

    private static bool HasDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperty(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicateProperty);
        }

        return false;
    }

    private static Result Validate(AutomaticBackupConfigurationV1 configuration)
    {
        if (!string.Equals(configuration.Format, AutomaticBackupConfigurationV1.FormatIdentifier, StringComparison.Ordinal)
            || configuration.Version != AutomaticBackupConfigurationV1.CurrentVersion
            || string.IsNullOrWhiteSpace(configuration.DestinationKind)
            || configuration.DestinationKind.Length > 64
            || string.IsNullOrWhiteSpace(configuration.DestinationLocation)
            || configuration.DestinationLocation.Length > 4096
            || string.IsNullOrWhiteSpace(configuration.DestinationDisplayName)
            || configuration.DestinationDisplayName.Length > 512)
        {
            return Fail(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup configuration is invalid.");
        }

        if (configuration.LastFingerprint is { } fingerprint
            && (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)))
        {
            return Fail(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup fingerprint is invalid.");
        }

        if (configuration.LastSuccessfulFileName is { } fileName
            && (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 512))
        {
            return Fail(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup file name is invalid.");
        }

        if (!Enum.IsDefined(configuration.DestinationAccess)
            || (configuration.LastFailureKind.HasValue
                && !Enum.IsDefined(configuration.LastFailureKind.Value))
            || configuration.LastFailureKind.HasValue != configuration.LastFailureUtc.HasValue)
        {
            return Fail(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup health information is invalid.");
        }

        return Result.Ok();
    }

    private static Result Fail(AutomaticBackupErrorCode code, string message) =>
        Result.Fail(new AutomaticBackupError(code, message));

    private static Result<T> Fail<T>(AutomaticBackupErrorCode code, string message) =>
        Result.Fail<T>(new AutomaticBackupError(code, message));
}
