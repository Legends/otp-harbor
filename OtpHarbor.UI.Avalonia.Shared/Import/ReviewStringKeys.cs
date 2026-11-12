using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Import;

public static class ReviewStringKeys
{
    public const string Breadcrumb = "ImportReviewBreadcrumb";
    public const string Title = "ImportReviewTitle";
    public const string Description = "ImportReviewDescription";
    public const string Summary = "ImportReviewSummary";
    public const string Total = "ImportReviewTotal";
    public const string Ready = "ImportReviewReady";
    public const string Conflicts = "ImportReviewConflicts";
    public const string Duplicates = "ImportReviewDuplicates";
    public const string Unsupported = "ImportReviewUnsupported";
    public const string Invalid = "ImportReviewInvalid";
    public const string All = "ImportReviewAll";
    public const string ImportCount = "ImportReviewImportCount";
    public const string Unresolved = "ImportReviewUnresolved";
    public const string Excluded = "ImportReviewExcluded";
    public const string SelectedCount = "ImportReviewSelectedCount";
    public const string Search = "ImportReviewSearch";
    public const string Filter = "ImportReviewFilter";
    public const string Apply = "ImportReviewApply";
    public const string Back = "ImportReviewBack";
    public const string Cancel = "ImportReviewCancel";
    public const string Account = "ImportReviewAccount";
    public const string Issue = "ImportReviewIssue";
    public const string Existing = "ImportReviewExisting";
    public const string Incoming = "ImportReviewIncoming";
    public const string Action = "ImportReviewAction";
    public const string NoResults = "ImportReviewNoResults";
    public const string CategoryCount = "ImportReviewCategoryCount";
    public const string Identity = "ImportReviewIdentity";
    public const string Parameters = "ImportReviewParameters";
    public const string ExistingParameters = "ImportReviewExistingParameters";
    public const string NoExisting = "ImportReviewNoExisting";
    public const string ExactDuplicate = "ImportReviewExactDuplicate";
    public const string NewAccount = "ImportReviewNewAccount";
    public const string SecretDiffers = "ImportReviewSecretDiffers";
    public const string LabelDiffers = "ImportReviewLabelDiffers";
    public const string ParametersDiffer = "ImportReviewParametersDiffer";
    public const string SecretDescription = "ImportReviewSecretDescription";
    public const string ChangeDescription = "ImportReviewChangeDescription";
    public const string DuplicateDescription = "ImportReviewDuplicateDescription";
    public const string ReadyDescription = "ImportReviewReadyDescription";
    public const string ChooseAction = "ImportReviewChooseAction";
    public const string Replace = "ImportReviewReplace";
    public const string KeepBoth = "ImportReviewKeepBoth";
    public const string Skip = "ImportReviewSkip";
    public const string Add = "ImportReviewAdd";
    public const string Steps = "ImportReviewSteps";
    public const string ChoicesHelp = "ImportReviewChoicesHelp";
    public const string ExcludedNonOtp = "ImportReviewExcludedNonOtp";
    public const string Changes = "ImportReviewChanges";
    public const string FieldIssuer = "ImportReviewFieldIssuer";
    public const string FieldAccount = "ImportReviewFieldAccount";
    public const string FieldSecret = "ImportReviewFieldSecret";
    public const string FieldAlgorithm = "ImportReviewFieldAlgorithm";
    public const string FieldDigits = "ImportReviewFieldDigits";
    public const string FieldPeriod = "ImportReviewFieldPeriod";
    public const string FieldFavorite = "ImportReviewFieldFavorite";
    public const string FieldGroups = "ImportReviewFieldGroups";
    public const string StepSource = "ImportReviewStepSource";
    public const string StepExport = "ImportReviewStepExport";
    public const string StepReview = "ImportReviewStepReview";
    public const string StepImport = "ImportReviewStepImport";
    public const string EarlierExport = "ImportReviewEarlierExport";
    public const string EarlierDescription = "ImportReviewEarlierDescription";
    public static string Category(ImportReviewCategory category) => category switch
    {
        ImportReviewCategory.All => All, ImportReviewCategory.Ready => Ready, ImportReviewCategory.Conflicts => Conflicts,
        ImportReviewCategory.Duplicates => Duplicates, ImportReviewCategory.Unsupported => Unsupported, _ => Invalid
    };
    public static string ActionKey(AccountImportConflictAction action) => action switch
    {
        AccountImportConflictAction.Replace => Replace, AccountImportConflictAction.KeepBoth => KeepBoth, _ => Skip
    };
    public static IReadOnlyList<string> AllKeys { get; } = [ EarlierExport, EarlierDescription, StepSource, StepExport, StepReview, StepImport, ExcludedNonOtp, Changes, FieldIssuer, FieldAccount, FieldSecret, FieldAlgorithm, FieldDigits, FieldPeriod, FieldFavorite, FieldGroups, Breadcrumb, Title, Description, Summary, Total, Ready, Conflicts, Duplicates, Unsupported, Invalid, All, ImportCount, Unresolved, Excluded, SelectedCount, Search, Filter, Apply, Back, Cancel, Account, Issue, Existing, Incoming, Action, NoResults, CategoryCount, Identity, Parameters, ExistingParameters, NoExisting, ExactDuplicate, NewAccount, SecretDiffers, LabelDiffers, ParametersDiffer, SecretDescription, ChangeDescription, DuplicateDescription, ReadyDescription, ChooseAction, Replace, KeepBoth, Skip, Add, Steps, ChoicesHelp ];
}
