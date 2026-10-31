using System.Diagnostics;
using System.Text;
namespace Lab.Memory;
internal sealed class DriverLineControl:IDisposable
{
    private readonly Process _game;private readonly IntPtr _inline;private readonly Dictionary<IntPtr,(byte First,int Length)> _owned=new();
    internal DriverLineControl(Process game,IntPtr inline){_game=game;_inline=inline;}
    internal bool Update(bool hide)
    {
        if(!hide){Restore();return true;}
        if(!_game.ReadBytes(_inline-16,16,out var h))return false;IntPtr data=new(BitConverter.ToInt64(h));int length=BitConverter.ToInt32(h,8),capacity=BitConverter.ToInt32(h,12)&0x3fffffff;
        if(data==IntPtr.Zero||length<1||length>4096||capacity<=length||capacity>4096||(data==_inline?capacity!=20:capacity<20))return false;
        if(!_game.ReadBytes(data,length+1,out var bytes))return false;
        if(bytes[0]==0)return _owned.ContainsKey(data);
        string text=Encoding.UTF8.GetString(bytes).TrimEnd('\0');if(!text.Contains("AMD proprietary driver",StringComparison.OrdinalIgnoreCase))return false;
        if(!_game.WriteBytes(data,[0]))return false;_owned[data]=(bytes[0],length);return true;
    }
    private void Restore(){foreach(var entry in _owned.ToArray()){if(_game.ReadBytes(_inline-16,16,out var h)&&new IntPtr(BitConverter.ToInt64(h))==entry.Key&&BitConverter.ToInt32(h,8)==entry.Value.Length&&_game.ReadValue<byte>(entry.Key,out byte first)&&first==0)_game.WriteBytes(entry.Key,[entry.Value.First]);}_owned.Clear();}
    public void Dispose(){try{if(!_game.HasExited)Restore();}catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){}}
}
