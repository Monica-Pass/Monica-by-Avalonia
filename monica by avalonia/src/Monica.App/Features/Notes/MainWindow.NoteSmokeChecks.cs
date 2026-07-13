using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    public async Task<bool> RunSmokeUiNoteEditorChecksAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(RunSmokeUiNoteEditorChecksAsync);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI note editor failed. reason=no-view-model");
            return false;
        }

        var success = await NoteEditorView.RunSmokeChecksAsync(viewModel);

        AppDiagnostics.Info(
            $"Smoke UI note editor checks completed. success={success}, " +
            $"lineCount={viewModel.NoteLineCount}, outline={viewModel.NoteOutlineCount}, " +
            $"references={viewModel.NoteReferenceCount}, selectedNote={viewModel.SelectedNote?.Title}");
        return success;
    }
}
