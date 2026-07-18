using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Monica.App.Services;

public interface IWindowPrivacyService
{
    bool SetCaptureProtection(bool enabled);
}

public sealed class WindowPrivacyService(Func<Window?> windowProvider) : IWindowPrivacyService
{
    private const uint NoAffinity = 0x00000000;
    private const uint MonitorOnlyAffinity = 0x00000001;
    private const uint ExcludeFromCaptureAffinity = 0x00000011;

    public bool SetCaptureProtection(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = windowProvider()?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (!enabled)
        {
            return SetWindowDisplayAffinity(handle, NoAffinity);
        }

        return SetWindowDisplayAffinity(handle, ExcludeFromCaptureAffinity) ||
               SetWindowDisplayAffinity(handle, MonitorOnlyAffinity);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr windowHandle, uint affinity);
}

internal sealed class DisabledWindowPrivacyService : IWindowPrivacyService
{
    public bool SetCaptureProtection(bool enabled) => false;
}
