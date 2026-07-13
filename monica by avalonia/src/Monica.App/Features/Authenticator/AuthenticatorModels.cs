using Avalonia;

namespace Monica.App.ViewModels;

public sealed record TotpFilterChoice(string Key, string Label, int Count, int Level, bool IsSelected)
{
    public Thickness Indent => new(Math.Max(0, Level) * 12, 0, 0, 0);
}
