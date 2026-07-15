using System.Collections.ObjectModel;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static void ClearItems<T>(ObservableCollection<T> target)
    {
        if (target.Count > 0)
        {
            target.Clear();
        }
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        if (target is ObservableRangeCollection<T> range)
        {
            range.ReplaceRange(items);
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
