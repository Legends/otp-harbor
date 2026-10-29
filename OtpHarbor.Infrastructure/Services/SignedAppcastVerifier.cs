using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using NSec.Cryptography;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class SignedAppcastVerifier : ISignedAppcastVerifier
{
    private const int MaximumAppcastBytes = 256 * 1024;
    private const int MaximumItems = 128;
    private static readonly XNamespace Sparkle =
        "http://www.andymatuschak.org/xml-namespaces/sparkle";

    public SignedAppcastCheckResult Verify(SignedAppcastCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AppcastBytes.IsEmpty
            || request.AppcastBytes.Length > MaximumAppcastBytes
            || request.CurrentVersion is null
            || string.IsNullOrWhiteSpace(request.OperatingSystem)
            || string.IsNullOrWhiteSpace(request.Architecture)
            || !IsSupportedChannel(request.Channel))
        {
            return new(SignedAppcastCheckStatus.InvalidFormat);
        }

        if (!VerifySignature(request))
            return new(SignedAppcastCheckStatus.InvalidSignature);

        var parsingCopy = request.AppcastBytes.ToArray();
        try
        {
            using var stream = new MemoryStream(parsingCopy, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumAppcastBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            var items = document.Root?
                .Element("channel")?
                .Elements("item")
                .Take(MaximumItems + 1)
                .ToArray();
            if (items is null || items.Length == 0 || items.Length > MaximumItems)
                return new(SignedAppcastCheckStatus.InvalidFormat);

            var candidates = items
                .Select(ParseCandidate)
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .Where(candidate => MatchesTarget(
                    candidate,
                    request.OperatingSystem,
                    request.Architecture,
                    request.RequireExplicitTarget,
                    request.Channel))
                .Where(candidate => candidate.Version > request.CurrentVersion)
                .OrderByDescending(candidate => candidate.Version)
                .ToArray();

            if (candidates.Length == 0)
                return new(SignedAppcastCheckStatus.NoApplicableUpdate);

            var selected = candidates[0];
            return new(
                SignedAppcastCheckStatus.UpdateAvailable,
                selected.Version,
                selected.ArtifactUri,
                selected.ArtifactSignature,
                selected.ReleaseNotes);
        }
        catch (Exception) when (parsingCopy.Length <= MaximumAppcastBytes)
        {
            return new(SignedAppcastCheckStatus.InvalidFormat);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(parsingCopy);
        }
    }

    private static bool VerifySignature(SignedAppcastCheckRequest request)
    {
        byte[]? publicKeyBytes = null;
        byte[]? signatureBytes = null;
        try
        {
            publicKeyBytes = Convert.FromBase64String(request.PublicKey.Trim());
            signatureBytes = Convert.FromBase64String(request.Signature.Trim());
            if (publicKeyBytes.Length != 32 || signatureBytes.Length != 64)
                return false;

            var publicKey = PublicKey.Import(
                SignatureAlgorithm.Ed25519,
                publicKeyBytes,
                KeyBlobFormat.RawPublicKey);
            return SignatureAlgorithm.Ed25519.Verify(
                publicKey,
                request.AppcastBytes.Span,
                signatureBytes);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            if (publicKeyBytes is not null) CryptographicOperations.ZeroMemory(publicKeyBytes);
            if (signatureBytes is not null) CryptographicOperations.ZeroMemory(signatureBytes);
        }
    }

    private static AppcastCandidate? ParseCandidate(XElement item)
    {
        var enclosure = item.Element("enclosure");
        var versionText = item.Element(Sparkle + "version")?.Value
            ?? enclosure?.Attribute(Sparkle + "version")?.Value;
        var uriText = enclosure?.Attribute("url")?.Value;
        if (!Version.TryParse(versionText, out var version)
            || version.Major < 0
            || !Uri.TryCreate(uriText, UriKind.Absolute, out var artifactUri)
            || artifactUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return new AppcastCandidate(
            version,
            artifactUri,
            enclosure?.Attribute(Sparkle + "edSignature")?.Value,
            BoundReleaseNotes(item.Element("description")?.Value),
            item.Element(Sparkle + "os")?.Value ?? enclosure?.Attribute(Sparkle + "os")?.Value,
            item.Element(Sparkle + "architecture")?.Value
                ?? enclosure?.Attribute(Sparkle + "architecture")?.Value,
            item.Element(Sparkle + "channel")?.Value
                ?? enclosure?.Attribute(Sparkle + "channel")?.Value
                ?? "stable");
    }

    private static bool MatchesTarget(
        AppcastCandidate candidate,
        string operatingSystem,
        string architecture,
        bool requireExplicitTarget,
        string channel) =>
        (!requireExplicitTarget
         || (!string.IsNullOrWhiteSpace(candidate.OperatingSystem)
             && !string.IsNullOrWhiteSpace(candidate.Architecture)))
        && (string.IsNullOrWhiteSpace(candidate.OperatingSystem)
         || string.Equals(candidate.OperatingSystem, operatingSystem, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(candidate.Architecture)
            || string.Equals(candidate.Architecture, architecture, StringComparison.OrdinalIgnoreCase))
        && IsSupportedChannel(candidate.Channel)
        && MatchesChannel(candidate.Channel, channel);

    private static bool MatchesChannel(string candidateChannel, string requestedChannel) =>
        string.Equals(candidateChannel, requestedChannel, StringComparison.OrdinalIgnoreCase)
        || (string.Equals(requestedChannel, "rc", StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidateChannel, "stable", StringComparison.OrdinalIgnoreCase));

    private static bool IsSupportedChannel(string? channel) =>
        string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase)
        || string.Equals(channel, "rc", StringComparison.OrdinalIgnoreCase);

    private static string BoundReleaseNotes(string? releaseNotes)
    {
        const int maximumCharacters = 20_000;
        var normalized = string.IsNullOrWhiteSpace(releaseNotes)
            ? string.Empty
            : releaseNotes.Trim();
        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..maximumCharacters];
    }

    private sealed record AppcastCandidate(
        Version Version,
        Uri ArtifactUri,
        string? ArtifactSignature,
        string ReleaseNotes,
        string? OperatingSystem,
        string? Architecture,
        string Channel);
}
