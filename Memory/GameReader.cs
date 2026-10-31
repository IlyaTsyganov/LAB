using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace Lab.Memory;
internal sealed record GameProfile(string Name,int ModuleSize,string MD5,int Velocity,int GPUVendor,int MaxHz,int IsLoading,int IsLoading2,int IsInGame)
{
    internal static GameProfile Resolve(string path)
    {
        using var file=File.OpenRead(path);string hash=Convert.ToHexString(System.Security.Cryptography.MD5.HashData(file));
        var profiles=JsonSerializer.Deserialize<GameProfile[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Assets/game-profiles.json")))!;
        return profiles.FirstOrDefault(p=>p.MD5.Equals(hash,StringComparison.OrdinalIgnoreCase))??throw new NotSupportedException("This executable has no verified LAB telemetry profile.");
    }
}
internal sealed class GameReader
{
    private readonly Process _game;private readonly IntPtr _image;private readonly GameProfile _profile;
    internal GameReader(Process game,GameProfile profile){_game=game;_profile=profile;_image=game.MainModule!.BaseAddress;if(game.MainModule.ModuleMemorySize!=profile.ModuleSize&&profile.Name!="1.0 (Release)")throw new NotSupportedException("Loaded game image does not match its telemetry profile.");}
    internal IntPtr DriverInlineBuffer=>_image+_profile.GPUVendor;
    internal int MaxHz=>_game.ReadValue<int>(_image+_profile.MaxHz,-1);
    internal bool Active=>_game.ReadValue<bool>(_image+_profile.IsLoading,out bool a)&&!a&&_game.ReadValue<byte>(_image+_profile.IsLoading2,out byte b)&&b==0&&_game.ReadValue<bool>(_image+_profile.IsInGame,out bool c)&&c;
    internal IntPtr Player
    {get{IntPtr root=_game.ReadPointer(_image+_profile.Velocity),slot=_game.ReadPointer(root+0x1510);int offset=int.TryParse(_profile.Name[..1],out int major)&&major>=3?0x5c0:0x598;return slot==IntPtr.Zero?IntPtr.Zero:_game.ReadPointer(slot+offset);}}
    internal IntPtr Velocity=>_game.ReadPointer(Player+0x1d0) is var body&&body!=IntPtr.Zero?body+0x3f40:IntPtr.Zero;
}
