using System.Runtime.InteropServices;
namespace Lab.Game;

internal static class LightingFixPolicy
{
    internal const string EnvironmentVariable = "LAB_LIGHTING_FIX";

    internal static bool NeedsFix(string gamePath)
    {
        try { return GameExecutableInfo.Read(gamePath)?.NeedsLightingFix == true; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
    private static readonly Lazy<uint> Gpu = new(ReadPrimaryGpuVendor);
    internal static uint PrimaryGpuVendor => Gpu.Value;
    internal static bool CanUse(string gamePath, uint? vendor = null) => (vendor ?? PrimaryGpuVendor) == 0x10de && NeedsFix(gamePath);

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Key;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device,uint index,ref DisplayDevice result,uint flags);
    private static uint ReadPrimaryGpuVendor()
    {
        for(uint index=0;index<32;index++)
        {
            var device=new DisplayDevice { Size=Marshal.SizeOf<DisplayDevice>(),Name="",Description="",Id="",Key="" };
            if(!EnumDisplayDevices(null,index,ref device,0)) break;
            if((device.Flags & 4)==0) continue;
            if(device.Id.Contains("VEN_10DE",StringComparison.OrdinalIgnoreCase)) return 0x10de;
            if(device.Id.Contains("VEN_1002",StringComparison.OrdinalIgnoreCase)) return 0x1002;
            if(device.Id.Contains("VEN_8086",StringComparison.OrdinalIgnoreCase)) return 0x8086;
            return 0;
        }
        return 0; // Unknown hardware cannot opt in to a graphics correction.
    }

    internal static IEnumerable<string> DisplayNames()
    {
        for(uint index=0;index<32;index++) {
            var device=new DisplayDevice { Size=Marshal.SizeOf<DisplayDevice>(),Name="",Description="",Id="",Key="" };
            if(!EnumDisplayDevices(null,index,ref device,0)) yield break;
            if(!string.IsNullOrWhiteSpace(device.Description)) yield return device.Description;
        }
    }
}
