namespace TOTP.Core.Services.Models;

public sealed record DiagnosticLogEmailRequest(
    string SourcePath,
    string AttachmentFileName,
    string Recipient,
    string Subject,
    string Body,
    string ChooserTitle);

public enum DiagnosticLogEmailOutcome
{
    AttachmentIncluded,
    ManualAttachmentRequired
}

public static class DiagnosticSupportContact
{
    public const string EmailAddress = "otpharbor.support@proton.me";
}
