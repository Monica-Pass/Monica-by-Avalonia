using System.Runtime.InteropServices;
using System.Text;

namespace Monica.Platform.Services;

public sealed class WindowsAutoTypeService(IPlatformIntegrationService platformIntegrationService) : IAutoTypeService
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const ushort VirtualKeyTab = 0x09;
    private const ushort VirtualKeyReturn = 0x0D;
    private const int MaxChunkEvents = 64;
    private const int MaxDelayMilliseconds = 5_000;

    public PlatformIntegrationCapability Capability { get; } =
        platformIntegrationService.GetCapability(PlatformFeatureKeys.AutoType);

    public string LastError { get; private set; } = "";

    public IntPtr GetForegroundWindow() =>
        OperatingSystem.IsWindows() ? GetForegroundWindowNative() : IntPtr.Zero;

    public string GetWindowTitle(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return "";
        }

        var length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return "";
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    public bool IsWindowOwnedByThisProcess(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(windowHandle, out var windowProcessId);
        return windowProcessId == unchecked((uint)Environment.ProcessId);
    }

    public bool TryType(IReadOnlyList<AutoTypeToken> tokens)
    {
        LastError = "";

        if (!Capability.IsUsable)
        {
            LastError = Capability.UnsupportedReason ?? "Auto-typing is unavailable on this platform.";
            return false;
        }

        if (tokens is not { Count: > 0 })
        {
            LastError = "There is nothing to type.";
            return false;
        }

        var batch = new List<Input>(MaxChunkEvents);
        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case AutoTypeTokenKind.Text:
                    foreach (var character in token.Value)
                    {
                        batch.Add(TextInput(character, up: false));
                        batch.Add(TextInput(character, up: true));
                        if (!FlushIfNeeded(ref batch))
                        {
                            return false;
                        }
                    }

                    break;
                case AutoTypeTokenKind.Tab:
                    if (!TypeVirtualKey(VirtualKeyTab, ref batch))
                    {
                        return false;
                    }

                    break;
                case AutoTypeTokenKind.Enter:
                    if (!TypeVirtualKey(VirtualKeyReturn, ref batch))
                    {
                        return false;
                    }

                    break;
                case AutoTypeTokenKind.Delay:
                    if (!Flush(ref batch))
                    {
                        return false;
                    }

                    Thread.Sleep(Math.Clamp(token.DelayMilliseconds, 0, MaxDelayMilliseconds));
                    break;
                default:
                    LastError = "Unsupported auto-typing instruction.";
                    return false;
            }
        }

        return Flush(ref batch);
    }

    private bool TypeVirtualKey(ushort virtualKey, ref List<Input> batch)
    {
        batch.Add(VirtualKeyInput(virtualKey, 0));
        batch.Add(VirtualKeyInput(virtualKey, KeyeventfKeyup));
        return FlushIfNeeded(ref batch);
    }

    private bool FlushIfNeeded(ref List<Input> batch)
    {
        return batch.Count < MaxChunkEvents || Flush(ref batch);
    }

    private bool Flush(ref List<Input> batch)
    {
        if (batch.Count == 0)
        {
            return true;
        }

        var inputs = batch.ToArray();
        batch.Clear();

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != (uint)inputs.Length)
        {
            LastError = $"Only {sent} of {inputs.Length} keystrokes reached the focused application.";
            return false;
        }

        return true;
    }

    private static Input TextInput(char character, bool up) => KeyboardInputRecord(0, character, up
        ? KeyeventfUnicode | KeyeventfKeyup
        : KeyeventfUnicode);

    private static Input VirtualKeyInput(ushort virtualKey, uint flags) =>
        KeyboardInputRecord(virtualKey, 0, flags);

    private static Input KeyboardInputRecord(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = InputKeyboard,
        U = new InputUnion
        {
            Ki = new KeyboardInput
            {
                Vk = virtualKey,
                Scan = scanCode,
                Flags = flags,
                Time = 0,
                ExtraInformation = IntPtr.Zero
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Ki;

        // SendInput checks cbSize against the native INPUT, whose union is as wide as its largest
        // member (MOUSEINPUT). Only the keyboard record is modelled here, so the union is padded out
        // to that width; without it Marshal.SizeOf reports 32 on x64 and SendInput refuses every
        // single keystroke with ERROR_INVALID_SIZE.
        [FieldOffset(24)]
        private readonly long _widestMemberTail;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    // SendInput refuses the whole batch when cbSize is not exactly sizeof(INPUT), which on x64 is 40
    // because the union is as wide as MOUSEINPUT. Exposed for the guard test that pins that number.
    public static int InputRecordByteSize => Marshal.SizeOf<Input>();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetForegroundWindowNative();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
}
