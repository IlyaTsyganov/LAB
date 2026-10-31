using System.Runtime.InteropServices;
namespace Lab.Overlay;
internal readonly record struct SpeedometerReading(bool Enabled,int X,int Y,float? Speed,bool Supported=true,double Scale=1,Color TextColor=default,bool ShowSign=false)
{internal Color DisplayColor=>TextColor.IsEmpty?Color.White:Color.FromArgb(TextColor.R,TextColor.G,TextColor.B);}
internal static class GameViewport
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
    internal static bool TryGetPhysicalViewport(IntPtr hwnd,out Rectangle value){value=default;if(hwnd==IntPtr.Zero||!GetClientRect(hwnd,out var r)||r.Right<=0||r.Bottom<=0)return false;value=new(0,0,r.Right,r.Bottom);return true;}
}
