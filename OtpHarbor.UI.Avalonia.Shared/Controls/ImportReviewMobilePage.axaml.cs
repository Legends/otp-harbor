using Avalonia.Controls;

namespace TOTP.Avalonia.Shared.Controls;

public partial class ImportReviewMobilePage : UserControl
{
    public ImportReviewMobilePage() => InitializeComponent();

    public void UseNativeList(Control nativeList)
    {
        ArgumentNullException.ThrowIfNull(nativeList);
        ReviewPageScroll.IsVisible = false;
        ReviewPageScroll.IsHitTestVisible = false;
        ReviewItems.ItemsSource = null;
        NativeReviewPresenter.Content = nativeList;
        NativeReviewPresenter.IsVisible = true;
    }
}
