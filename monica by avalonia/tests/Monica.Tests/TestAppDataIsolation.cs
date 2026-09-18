using System.Runtime.CompilerServices;
using Monica.Data;

namespace Monica.Tests;

// ViewModels derive MDBX working-copy paths from MonicaAppDataPaths, so without this the
// suite opens and overwrites the developer's real vault directory.
internal static class TestAppDataIsolation
{
    [ModuleInitializer]
    internal static void IsolateAppData()
    {
        Environment.SetEnvironmentVariable(
            MonicaAppDataPaths.OverrideEnvironmentVariable,
            TestTempPaths.Root);
    }
}
