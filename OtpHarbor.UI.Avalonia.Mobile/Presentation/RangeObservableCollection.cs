using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace TOTP.Avalonia.Mobile.Presentation;

internal sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var replacement = items as IReadOnlyCollection<T> ?? items.ToArray();

        CheckReentrancy();
        Items.Clear();
        foreach (var item in replacement) Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
