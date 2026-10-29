using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentResults;
using TOTP.Core.Enums;

namespace TOTP.Core.Models;

public static class AppPreferencesV1Codec
{
    public const int MaximumPayloadSize = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static Result<byte[]> Serialize(AppPreferencesV1 preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var validation = Validate(preferences);
        if (validation.IsFailed) return Result.Fail<byte[]>(validation.Errors);

        var payload = JsonSerializer.SerializeToUtf8Bytes(preferences, JsonOptions);
        return payload.Length <= MaximumPayloadSize
            ? Result.Ok(payload)
            : Fail<byte[]>(AppPreferencesErrorCode.TooLarge, "Preferences exceed the size limit.");
    }

    public static Result<AppPreferencesV1> Deserialize(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty) return Fail<AppPreferencesV1>(AppPreferencesErrorCode.Empty, "Preferences are empty.");
        if (payload.Length > MaximumPayloadSize) return Fail<AppPreferencesV1>(AppPreferencesErrorCode.TooLarge, "Preferences exceed the size limit.");

        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            if (HasDuplicateProperty(document.RootElement))
                return Fail<AppPreferencesV1>(AppPreferencesErrorCode.Malformed, "Preferences contain duplicate properties.");

            var preferences = JsonSerializer.Deserialize<AppPreferencesV1>(payload.Span, JsonOptions);
            if (preferences is null) return Fail<AppPreferencesV1>(AppPreferencesErrorCode.Malformed, "Preferences are null.");
            var validation = Validate(preferences);
            return validation.IsSuccess ? Result.Ok(preferences) : Result.Fail<AppPreferencesV1>(validation.Errors);
        }
        catch (JsonException)
        {
            return Fail<AppPreferencesV1>(AppPreferencesErrorCode.Malformed, "Preferences JSON is invalid.");
        }
    }

    private static Result Validate(AppPreferencesV1 preferences)
    {
        if (!string.Equals(preferences.Format, AppPreferencesV1.FormatIdentifier, StringComparison.Ordinal))
            return Fail(AppPreferencesErrorCode.UnsupportedFormat, "Preferences format is unsupported.");
        if (preferences.Version != AppPreferencesV1.CurrentVersion)
            return Fail(AppPreferencesErrorCode.UnsupportedVersion, "Preferences version is unsupported.");
        if (!Enum.IsDefined(preferences.MinimumLogLevel)
            || !Enum.IsDefined(preferences.PreferredUnlockMethod)
            || preferences.IdleTimeoutMinutes is < 0 or > 1440
            || preferences.IdleTimeoutSeconds is < 1 or > 86400
            || preferences.ClearClipboardSeconds is < 1 or > 300
            || !double.IsFinite(preferences.QrPreviewScaleFactor)
            || preferences.QrPreviewScaleFactor is < 1.0 or > 6.0
            || Math.Abs(preferences.QrPreviewScaleFactor * 2 - Math.Round(preferences.QrPreviewScaleFactor * 2)) > 0.0001
            || !AppSettings.IsSupportedInterfaceScale(preferences.InterfaceScalePercent)
            || !IsValidCulture(preferences.CultureName))
        {
            return Fail(AppPreferencesErrorCode.InvalidValue, "Preferences contain an invalid value.");
        }

        return Result.Ok();
    }

    private static bool IsValidCulture(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName) || cultureName.Length > 32) return false;
        try
        {
            _ = CultureInfo.GetCultureInfo(cultureName);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool HasDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static Result Fail(AppPreferencesErrorCode code, string message) =>
        Result.Fail(new AppPreferencesError(code, message));

    private static Result<T> Fail<T>(AppPreferencesErrorCode code, string message) =>
        Result.Fail<T>(new AppPreferencesError(code, message));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { MaxDepth = 8, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter<AppLogLevel>(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<PreferredUnlockMethod>(namingPolicy: null, allowIntegerValues: false));
        return options;
    }
}
