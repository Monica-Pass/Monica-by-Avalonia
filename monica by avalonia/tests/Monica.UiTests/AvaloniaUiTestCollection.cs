using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(Monica.App.App))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
[assembly: AvaloniaTestFramework]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true)]

namespace Monica.UiTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaUiTestCollection
{
    public const string Name = "Avalonia UI";
}
