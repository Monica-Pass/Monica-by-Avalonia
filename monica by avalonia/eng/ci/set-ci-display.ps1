param(
    [int] $Width = 1920,
    [int] $Height = 1080,
    [switch] $InspectOnly
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'The display probe requires Windows.'
}
if (-not $InspectOnly -and $env:GITHUB_ACTIONS -ne 'true') {
    throw 'Display changes are restricted to ephemeral GitHub Actions runners.'
}

# DEVMODEW uses the Win32 public 220-byte layout. Unmodeled fields remain in the
# blittable buffer returned by EnumDisplaySettingsW; only pixel dimensions are changed.
# https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-devmodew
if (-not ('MonicaCiDisplay' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class MonicaCiDisplay
{
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    public struct Mode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(70)] public ushort DriverExtra;
        [FieldOffset(72)] public uint Fields;
        [FieldOffset(172)] public uint Width;
        [FieldOffset(176)] public uint Height;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(IntPtr device, int number, ref Mode mode);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsW", ExactSpelling = true)]
    private static extern int ChangeDisplaySettings(ref Mode mode, uint flags);

    public static Mode Current()
    {
        var mode = new Mode { Size = 220 };
        if (!EnumDisplaySettings(IntPtr.Zero, -1, ref mode))
            throw new InvalidOperationException("The runner display mode could not be read.");
        return mode;
    }

    public static void Set(uint width, uint height)
    {
        var mode = Current();
        mode.DriverExtra = 0;
        mode.Fields = 0x00080000 | 0x00100000;
        mode.Width = width;
        mode.Height = height;
        var test = ChangeDisplaySettings(ref mode, 2); // CDS_TEST: check driver support first.
        if (test != 0)
            throw new InvalidOperationException("The requested CI display mode is unsupported: " + test);
        var result = ChangeDisplaySettings(ref mode, 0); // Session only; do not update the registry.
        if (result != 0)
            throw new InvalidOperationException("The CI display mode could not be applied: " + result);
    }
}
'@
}

$before = [MonicaCiDisplay]::Current()
Write-Host "Runner display: $($before.Width)x$($before.Height)"
if ($InspectOnly) { return }
if ($before.Width -lt $Width -or $before.Height -lt $Height) {
    [MonicaCiDisplay]::Set([uint32]$Width, [uint32]$Height)
    Start-Sleep -Milliseconds 500
}
$after = [MonicaCiDisplay]::Current()
if ($after.Width -lt $Width -or $after.Height -lt $Height) {
    throw "The display remains $($after.Width)x$($after.Height), below the required ${Width}x${Height}."
}
Write-Host "Runner display ready: $($after.Width)x$($after.Height)"
