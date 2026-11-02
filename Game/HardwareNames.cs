using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;

namespace Lab.Game;

// Query actual hardware labels, without vendor/model rules. DESRU can replace
// the game's CPU/GPU buffers with arbitrary trainer text; only real labels may
// be suppressed in those shared rows while DESRU is present.
internal static class HardwareNames
{
    private static readonly Lazy<string[]> Names = new(Read);
    internal static IReadOnlyList<string> Current => Names.Value;
    private static string[] Read()
    {
        var names=new List<string>();
        try { using var cpu=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"); if(cpu?.GetValue("ProcessorNameString") is string value) names.Add(value); }
        catch(Exception e) when(e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        try { names.AddRange(VulkanNames()); } catch(Exception e) when(e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { }
        names.AddRange(LightingFixPolicy.DisplayNames());
        return names.SelectMany(n=>new[]{n.TrimEnd('\0'),n.Trim('\0',' ')})
            .Where(n=>n.Length>0&&Encoding.UTF8.GetByteCount(n)<256).Distinct(StringComparer.OrdinalIgnoreCase).Take(14).ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private struct InstanceInfo
    { public uint Type; public IntPtr Next; public uint Flags; public IntPtr Application; public uint LayerCount; public IntPtr Layers; public uint ExtensionCount; public IntPtr Extensions; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Create(ref InstanceInfo info,IntPtr allocator,out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Destroy(IntPtr instance,IntPtr allocator);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Enumerate(IntPtr instance,ref uint count,IntPtr devices);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Properties(IntPtr device,IntPtr properties);
    private static string[] VulkanNames()
    {
        IntPtr library=NativeLibrary.Load("vulkan-1.dll"),instance=IntPtr.Zero,devices=IntPtr.Zero,properties=IntPtr.Zero;
        try {
            T Export<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
            var info=new InstanceInfo { Type=1 };
            if(Export<Create>("vkCreateInstance")(ref info,IntPtr.Zero,out instance)!=0) { instance=IntPtr.Zero;return []; }
            var enumerate=Export<Enumerate>("vkEnumeratePhysicalDevices");uint count=0;
            if(enumerate(instance,ref count,IntPtr.Zero)!=0||count==0||count>64) return [];
            uint capacity=count;devices=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size));
            if(enumerate(instance,ref count,devices)!=0||count>capacity) return [];
            properties=Marshal.AllocHGlobal(4096);var get=Export<Properties>("vkGetPhysicalDeviceProperties");var result=new List<string>();
            for(int i=0;i<count;i++) {
                get(Marshal.ReadIntPtr(devices,i*IntPtr.Size),properties);
                var bytes=new byte[256];Marshal.Copy(properties+20,bytes,0,bytes.Length);
                result.Add(Encoding.UTF8.GetString(bytes).Split('\0')[0]);
            }
            return result.ToArray();
        } finally {
            if(properties!=IntPtr.Zero)Marshal.FreeHGlobal(properties);
            if(devices!=IntPtr.Zero)Marshal.FreeHGlobal(devices);
            if(instance!=IntPtr.Zero)Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library,"vkDestroyInstance"))(instance,IntPtr.Zero);
            NativeLibrary.Free(library);
        }
    }
}
