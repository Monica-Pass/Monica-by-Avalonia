using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Monica.App.Services;

public interface IWindowPrivacyService
{
    bool EnableCaptureProtection();
}

public sealed class WindowPrivacyService(Func<Window?> windowProvider) : IWindowPrivacyService
{
    private const uint MonitorOnlyAffinity = 0x00000001;
    private const uint ExcludeFromCaptureAffinity = 0x00000011;

    public bool EnableCaptureProtection()
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

        return SetWindowDisplayAffinity(handle, ExcludeFromCaptureAffinity) ||
            SetWindowDisplayAffinity(handle, MonitorOnlyAffinity);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr windowHandle, uint affinity);
}

internal sealed class DisabledWindowPrivacyService : IWindowPrivacyService
{
    public bool EnableCaptureProtection() => false;
}
