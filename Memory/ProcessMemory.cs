using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace Lab.Memory;
internal enum ReadStringType{ASCII,UTF8,UTF16}
internal static class ExtensionMethods
{
    internal static bool ReadBytes(this Process p,IntPtr at,int count,out byte[] bytes)
    {bytes=[];if(at==IntPtr.Zero||count<0||count>16*1024*1024)return false;var b=new byte[count];if(!WinAPI.ReadProcessMemory(p.Handle,at,b,(UIntPtr)count,out var read)||read.ToUInt64()!=(ulong)count)return false;bytes=b;return true;}
    internal static byte[]? ReadBytes(this Process p,IntPtr at,int count)=>p.ReadBytes(at,count,out var b)?b:null;
    internal static bool ReadValue<T>(this Process p,IntPtr at,out T value)where T:struct
    {value=default;int size=typeof(T)==typeof(bool)?1:Marshal.SizeOf<T>();if(!p.ReadBytes(at,size,out var b))return false;if(typeof(T)==typeof(bool)){value=(T)(object)(b[0]!=0);return true;}var h=GCHandle.Alloc(b,GCHandleType.Pinned);try{value=Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());return true;}finally{h.Free();}}
    internal static T ReadValue<T>(this Process p,IntPtr at,T fallback=default)where T:struct=>p.ReadValue(at,out T v)?v:fallback;
    internal static bool ReadPointer(this Process p,IntPtr at,bool x64,out IntPtr value){value=IntPtr.Zero;if(!p.ReadBytes(at,x64?8:4,out var b))return false;value=new(x64?BitConverter.ToInt64(b):BitConverter.ToInt32(b));return true;}
    internal static IntPtr ReadPointer(this Process p,IntPtr at)=>p.ReadPointer(at,true,out var v)?v:IntPtr.Zero;
    internal static bool ReadPointer(this Process p,IntPtr at,out IntPtr value)=>p.ReadPointer(at,true,out value);
    internal static bool WriteBytes(this Process p,IntPtr at,byte[] b)=>at!=IntPtr.Zero&&WinAPI.WriteProcessMemory(p.Handle,at,b,(UIntPtr)b.Length,out var n)&&n.ToUInt64()==(ulong)b.Length;
    internal static IntPtr AllocateMemory(this Process p,int n)=>WinAPI.VirtualAllocEx(p.Handle,IntPtr.Zero,(UIntPtr)n,0x3000,MemPageProtect.PAGE_READWRITE);
    internal static bool FreeMemory(this Process p,IntPtr at)=>WinAPI.VirtualFreeEx(p.Handle,at,UIntPtr.Zero,0x8000);
    internal static bool VirtualProtect(this Process p,IntPtr at,int size,MemPageProtect protection,out MemPageProtect before)=>WinAPI.VirtualProtectEx(p.Handle,at,(UIntPtr)size,protection,out before);
    internal static bool VirtualProtect(this Process p,IntPtr at,int size,MemPageProtect protection)=>p.VirtualProtect(at,size,protection,out _);
    internal static string ReadString(this Process p,IntPtr at,ReadStringType kind,int count){var b=p.ReadBytes(at,count);if(b is null)return "";return (kind==ReadStringType.UTF16?Encoding.Unicode:kind==ReadStringType.UTF8?Encoding.UTF8:Encoding.ASCII).GetString(b).Split('\0')[0];}
}
