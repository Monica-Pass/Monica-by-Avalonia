using System.Runtime.InteropServices;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class WindowsAutoTypeServiceTests
{
    [Fact]
    public void Input_record_matches_the_native_size_that_sendinput_demands()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        {
            return;
        }

        // SendInput silently refuses the entire batch when cbSize is not exactly sizeof(INPUT), and
        // sizeof(INPUT) is wider than the keyboard record alone because its union is sized by
        // MOUSEINPUT. A marshalling layout that looks right in C# is the whole failure mode here.
        Assert.Equal(40, WindowsAutoTypeService.InputRecordByteSize);
    }

    [Fact]
    public void Native_auto_type_service_reports_its_capability_and_refuses_empty_batches()
    {
        var integration = new PlatformIntegrationService(
            "Windows",
            [PlatformIntegrationService.Available(PlatformFeatureKeys.AutoType, "SendInput is available.")]);
        var service = new WindowsAutoTypeService(integration);

        Assert.True(service.Capability.IsUsable);
        Assert.False(service.TryType([]));
        Assert.Equal("There is nothing to type.", service.LastError);
    }

    [Fact]
    public void Capability_only_auto_type_service_refuses_and_names_the_reason()
    {
        var integration = new PlatformIntegrationService(
            "TestOS",
            [PlatformIntegrationService.Unsupported(PlatformFeatureKeys.AutoType, "No input adapter.")]);
        var service = new CapabilityOnlyAutoTypeService(integration);

        Assert.False(service.TryType([AutoTypeToken.Text("value"), AutoTypeToken.Tab]));
        Assert.Equal("No input adapter.", service.LastError);
        Assert.Equal(IntPtr.Zero, service.GetForegroundWindow());
        Assert.Equal("", service.GetWindowTitle(new IntPtr(1)));
        Assert.False(service.IsWindowOwnedByThisProcess(new IntPtr(1)));
    }

    [Fact]
    public void Auto_type_token_helpers_keep_delays_inside_the_supported_range()
    {
        Assert.Equal(0, AutoTypeToken.Delay(-5).DelayMilliseconds);
        Assert.Equal(250, AutoTypeToken.Delay(250).DelayMilliseconds);
        Assert.Equal("", AutoTypeToken.Tab.Value);
        Assert.Equal(AutoTypeTokenKind.Tab, AutoTypeToken.Tab.Kind);
    }

    [Fact]
    public void Ownership_check_is_true_only_for_a_window_this_process_created()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var integration = new PlatformIntegrationService(
            "Windows",
            [PlatformIntegrationService.Available(PlatformFeatureKeys.AutoType, "SendInput is available.")]);
        var service = new WindowsAutoTypeService(integration);

        Assert.False(service.IsWindowOwnedByThisProcess(IntPtr.Zero));

        var shell = GetShellWindow();
        Assert.True(shell != IntPtr.Zero, "Expected a shell window to stand in for a foreign process.");
        Assert.False(service.IsWindowOwnedByThisProcess(shell));

        using var own = TestMessageWindow.Create();
        Assert.True(service.IsWindowOwnedByThisProcess(own.Handle));
    }

    // The auto-type guard has to tell Monica's own windows apart from the target application's, so the
    // true branch needs a window this test process really owns. A message-only window is one, and it
    // never appears on screen.
    private sealed class TestMessageWindow : IDisposable
    {
        private const int HwndMessageParent = -3;
        private static readonly WndProc WindowProcedure = HandleMessage;
        private static readonly string ClassName = "MonicaAutoTypeTestWindowClass";
        private readonly IntPtr _instance;
        private readonly bool _registeredAsNew;

        private TestMessageWindow(IntPtr instance, IntPtr handle, bool registeredAsNew)
        {
            _instance = instance;
            _registeredAsNew = registeredAsNew;
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public static TestMessageWindow Create()
        {
            var instance = GetModuleHandle(null);
            var claw = new WndClass
            {
                style = 0,
                lpfnWndProc = WindowProcedure,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = instance,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = ClassName
            };

            const int errorClassAlreadyExists = 1410;
            var registeredAsNew = RegisterClassW(ref claw) != 0;
            if (!registeredAsNew && Marshal.GetLastWin32Error() != errorClassAlreadyExists)
            {
                throw new InvalidOperationException($"RegisterClassW failed ({Marshal.GetLastWin32Error()}).");
            }

            var handle = CreateWindowExW(
                dwExStyle: 0,
                lpClassName: ClassName,
                lpWindowName: "Monica auto-type test window",
                dwStyle: 0,
                x: 0,
                y: 0,
                nWidth: 0,
                nHeight: 0,
                hWndParent: new IntPtr(HwndMessageParent),
                hMenu: IntPtr.Zero,
                hInstance: instance,
                lpParam: IntPtr.Zero);

            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CreateWindowExW failed ({Marshal.GetLastWin32Error()}).");
            }

            return new TestMessageWindow(instance, handle, registeredAsNew);
        }

        public void Dispose()
        {
            DestroyWindow(Handle);
            if (_registeredAsNew)
            {
                UnregisterClassW(ClassName, _instance);
            }
        }

        private static IntPtr HandleMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam) =>
            DefWindowProcW(hWnd, message, wParam, lParam);

        private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClass
        {
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern ushort RegisterClassW(ref WndClass lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
}
