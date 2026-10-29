using Avalonia.Media;

namespace TOTP.Avalonia.Desktop.Presentation.Dialogs;

public sealed record QrPreviewDialogViewModel(string Title, IImage Image);
