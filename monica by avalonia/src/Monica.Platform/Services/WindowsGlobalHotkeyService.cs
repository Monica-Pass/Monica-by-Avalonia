using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Monica.Platform.Services;

public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int SlotCount = 2;
    private const int FirstHotkeyId = 0x4D4F;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private readonly IPlatformIntegrationService _platformIntegrationService;
    private readonly Registration?[] _slots = new Registration?[SlotCount];

    public WindowsGlobalHotkeyService(IPlatformIntegrationService platformIntegrationService)
    {
        _platformIntegrationService = platformIntegrationService;
    }

    public PlatformIntegrationCapability Capability =>
        _platformIntegrationService.GetCapability(PlatformFeatureKeys.GlobalHotkey);

    public string LastError(GlobalHotkeySlot slot) => Slot(slot).LastError;

    public bool IsRegistered(GlobalHotkeySlot slot) => Slot(slot).IsRegistered;

    public string RegisteredGesture(GlobalHotkeySlot slot) => Slot(slot).Gesture;

    public bool TryRegister(GlobalHotkeySlot slot, string gesture, Action activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        var registration = Slot(slot);
        registration.Unregister();

        if (!OperatingSystem.IsWindows())
        {
            registration.LastError = Capability.UnsupportedReason ?? "Global hotkeys require Windows.";
            return false;
        }

        if (!TryParseGesture(gesture, out var modifiers, out var virtualKey, out var normalized, out var error))
        {
            registration.LastError = error;
            return false;
        }

        var hotkeyId = FirstHotkeyId + (int)slot;
        var ready = new ManualResetEventSlim();
        var registered = false;
        var registrationError = "";
        var thread = new Thread(() =>
        {
            registration.SetThreadId(GetCurrentThreadId());
            registered = RegisterHotKey(IntPtr.Zero, hotkeyId, modifiers, virtualKey);
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
                    if (message.Message == WmHotkey && message.WParam == (nuint)hotkeyId)
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
                UnregisterHotKey(IntPtr.Zero, hotkeyId);
            }
        })
        {
            IsBackground = true,
            Name = $"Monica global hotkey {slot}"
        };

        registration.Thread = thread;
        thread.Start();
        var registrationCompleted = ready.Wait(TimeSpan.FromSeconds(3));
        if (registrationCompleted)
        {
            ready.Dispose();
        }

        if (!registrationCompleted || !registered)
        {
            registration.LastError = string.IsNullOrWhiteSpace(registrationError)
                ? "Global hotkey registration timed out."
                : registrationError;
            registration.Unregister();
            return false;
        }

        registration.IsRegistered = true;
        registration.Gesture = normalized;
        registration.LastError = "";
        return true;
    }

    public void Unregister(GlobalHotkeySlot slot) => Slot(slot).Unregister();

    public void Dispose()
    {
        foreach (var registration in _slots)
        {
            registration?.Unregister();
        }
    }

    private Registration Slot(GlobalHotkeySlot slot)
    {
        var index = (int)slot;
        if (index is < 0 or >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return _slots[index] ??= new Registration();
    }

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

    private sealed class Registration
    {
        private readonly object _sync = new();
        private Thread? _thread;
        private uint _threadId;

        public bool IsRegistered;
        public string Gesture = "";
        public string LastError = "";

        public Thread? Thread
        {
            get { lock (_sync) return _thread; }
            set { lock (_sync) _thread = value; }
        }

        public void SetThreadId(uint threadId)
        {
            lock (_sync)
            {
                _threadId = threadId;
            }
        }

        public void Unregister()
        {
            Thread? thread;
            lock (_sync)
            {
                thread = _thread;
                _thread = null;

                if (_threadId != 0)
                {
                    PostThreadMessage(_threadId, WmQuit, 0, 0);
                    _threadId = 0;
                }
            }

            if (thread is { IsAlive: true } && !ReferenceEquals(thread, Thread.CurrentThread))
            {
                thread.Join(TimeSpan.FromSeconds(2));
            }

            IsRegistered = false;
            Gesture = "";
        }
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
