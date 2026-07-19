using Avalonia.Controls;

namespace Monica.App.Features.Timeline;

public partial class TimelineEntryListView : UserControl
{
    public TimelineEntryListView()
    {
        InitializeComponent();
    }

    public ListBox EntryList => TimelineEntryList;
}
