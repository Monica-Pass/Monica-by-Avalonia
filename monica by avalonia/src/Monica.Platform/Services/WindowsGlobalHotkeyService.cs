using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Monica.Platform.Services;

public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyId = 0x4D4F;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private readonly object _sync = new();
    private readonly IPlatformIntegrationService _platformIntegrationService;
    private Thread? _messageThread;
    private uint _messageThreadId;

    public WindowsGlobalHotkeyService(IPlatformIntegrationService platformIntegrationService)
    {
        _platformIntegrationService = platformIntegrationService;
    }

    public PlatformIntegrationCapability Capability =>
        _platformIntegrationService.GetCapability(PlatformFeatureKeys.GlobalHotkey);
    public bool IsRegistered { get; private set; }
    public string RegisteredGesture { get; private set; } = "";
    public string LastError { get; private set; } = "";

    public bool TryRegister(string gesture, Action activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        Unregister();

        if (!OperatingSystem.IsWindows())
        {
            LastError = Capability.UnsupportedReason ?? "Global hotkeys require Windows.";
            return false;
        }

        if (!TryParseGesture(gesture, out var modifiers, out var virtualKey, out var normalized, out var error))
        {
            LastError = error;
            return false;
        }

        var ready = new ManualResetEventSlim();
        var registered = false;
        var registrationError = "";
        var thread = new Thread(() =>
        {
            _messageThreadId = GetCurrentThreadId();
            registered = RegisterHotKey(IntPtr.Zero, HotkeyId, modifiers, virtualKey);
            if (!registered)
            {
                registrationError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                ready.Set();
                return;
            }

            ready.Set();
            try
            {
                while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.Message == WmHotkey && message.WParam == (nuint)HotkeyId)
                    {
                        try
                        {
                            activated();
                        }
                        catch
                        {
                        }
                    }
                }
            }
            finally
            {
                UnregisterHotKey(IntPtr.Zero, HotkeyId);
            }
        })
        {
            IsBackground = true,
            Name = "Monica global hotkey"
        };

        lock (_sync)
        {
            _messageThread = thread;
        }

        thread.Start();
        var registrationCompleted = ready.Wait(TimeSpan.FromSeconds(3));
        if (registrationCompleted)
        {
            ready.Dispose();
        }

        if (!registrationCompleted || !registered)
        {
            LastError = string.IsNullOrWhiteSpace(registrationError)
                ? "Global hotkey registration timed out."
                : registrationError;
            Unregister();
            return false;
        }

        IsRegistered = true;
        RegisteredGesture = normalized;
        LastError = "";
        return true;
    }

    public void Unregister()
    {
        Thread? thread;
        uint threadId;
        lock (_sync)
        {
            thread = _messageThread;
            threadId = _messageThreadId;
            _messageThread = null;
            _messageThreadId = 0;
        }

        if (threadId != 0)
        {
            PostThreadMessage(threadId, WmQuit, 0, 0);
        }

        if (thread is { IsAlive: true } && !ReferenceEquals(thread, Thread.CurrentThread))
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        IsRegistered = false;
        RegisteredGesture = "";
    }

    public void Dispose() => Unregister();

    public static bool TryParseGesture(
        string gesture,
        out uint modifiers,
        out uint virtualKey,
        out string normalized,
        out string error)
    {
        modifiers = 0;
        virtualKey = 0;
        normalized = "";
        error = "";
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "Use at least one modifier and one key, for example Ctrl+Shift+Space.";
            return false;
        }

        var normalizedParts = new List<string>();
        foreach (var part in parts[..^1])
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= 0x0002;
                    if (!normalizedParts.Contains("Ctrl")) normalizedParts.Add("Ctrl");
                    break;
                case "SHIFT":
                    modifiers |= 0x0004;
                    if (!normalizedParts.Contains("Shift")) normalizedParts.Add("Shift");
                    break;
                case "ALT":
                    modifiers |= 0x0001;
                    if (!normalizedParts.Contains("Alt")) normalizedParts.Add("Alt");
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= 0x0008;
                    if (!normalizedParts.Contains("Win")) normalizedParts.Add("Win");
                    break;
                default:
                    error = $"Unsupported modifier: {part}.";
                    return false;
            }
        }

        if (modifiers == 0 || !TryParseVirtualKey(parts[^1], out virtualKey, out var keyName))
        {
            error = modifiers == 0 ? "A modifier key is required." : $"Unsupported key: {parts[^1]}.";
            return false;
        }

        normalizedParts.Add(keyName);
        modifiers |= 0x4000;
        normalized = string.Join('+', normalizedParts);
        return true;
    }

    private static bool TryParseVirtualKey(string value, out uint virtualKey, out string normalized)
    {
        virtualKey = 0;
        normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.Length == 1 && char.IsLetterOrDigit(normalized[0]))
        {
            virtualKey = normalized[0];
            return true;
        }

        if (normalized.StartsWith('F') && int.TryParse(normalized[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = normalized switch
        {
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "TAB" => 0x09,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "INSERT" => 0x2D,
            _ => 0
        };
        normalized = normalized switch
        {
            "PAGEUP" => "PageUp",
            "PAGEDOWN" => "PageDown",
            _ => char.ToUpperInvariant(normalized[0]) + normalized[1..].ToLowerInvariant()
        };
        return virtualKey != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr HWnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint minFilter, uint maxFilter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
