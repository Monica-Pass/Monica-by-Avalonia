using Avalonia.Controls;

namespace Monica.App.Features.Timeline;

public partial class TimelineInspectorView : UserControl
{
    public TimelineInspectorView()
    {
        InitializeComponent();
    }

    public Button BackButton => BackToTimelineListButton;
}
