using System.Diagnostics;
using System.Text.Json;

namespace Lab.Runtime;

internal sealed record GameLaunchStamp(int Pid,long Started,string Path)
{
    internal bool Matches(int pid,long started,string path)=>Pid==pid&&Started==started&&Path.Equals(path,StringComparison.OrdinalIgnoreCase);
}

internal static class GameLaunchHistory
{
    private static string FilePath(SettingsStore store)=>System.IO.Path.Combine(store.DirectoryPath,"game-launch.json");
    internal static void Record(Process game,string path,SettingsStore store)
    {
        var stamp=new GameLaunchStamp(game.Id,game.StartTime.ToUniversalTime().ToFileTimeUtc(),System.IO.Path.GetFullPath(path));
        string file=FilePath(store);
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(stamp,SettingsStore.Json));
        File.Move(file+".tmp",file,true);
    }
    internal static bool Matches(Process game,SettingsStore store)
    {
        string file=FilePath(store);if(!File.Exists(file))return false;
        try{
            var stamp=JsonSerializer.Deserialize<GameLaunchStamp>(File.ReadAllText(file),SettingsStore.Json);
            return stamp?.Matches(game.Id,game.StartTime.ToUniversalTime().ToFileTimeUtc(),game.MainModule!.FileName)==true;
        }catch(Exception e)when(e is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception){
            Log.Warning(e,"Game launch identity");return false;
        }
    }
}
