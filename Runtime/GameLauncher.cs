using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text;
namespace Lab.Runtime;
internal static class GameLauncher
{
    internal static string PrepareLighting(SettingsStore store)
    {
        string folder=Path.Combine(store.DirectoryPath,"lighting");Directory.CreateDirectory(folder);
        string source=Path.Combine(AppContext.BaseDirectory,"Native/Lighting/lab-lighting.dll");if(!File.Exists(source))throw new FileNotFoundException("The LAB lighting library is missing. Extract the complete application archive.",source);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)))[..16].ToLowerInvariant();string library=Path.Combine(folder,"lab-lighting-"+hash+".dll");if(!File.Exists(library))File.Copy(source,library);
        var manifest=new{file_format_version="1.1.2",layer=new{name="VK_LAYER_LAB_lighting",type="GLOBAL",library_path=LoaderPath(library),api_version="1.1.0",implementation_version="1",description="LAB lighting barrier correction",functions=new{vkNegotiateLoaderLayerInterfaceVersion="lab_negotiate_layer_interface_version"}}};
        File.WriteAllText(Path.Combine(folder,"lab-lighting.json"),JsonSerializer.Serialize(manifest,SettingsStore.Json));WriteLightingControl(store);return LoaderPath(folder);
    }
    internal static string LoaderPath(string path){var buffer=new StringBuilder(32768);uint count=GetShortPathName(path,buffer,(uint)buffer.Capacity);return count>0&&count<buffer.Capacity?buffer.ToString():path;}
    [DllImport("kernel32.dll",EntryPoint="GetShortPathNameW",CharSet=CharSet.Unicode)]private static extern uint GetShortPathName(string path,StringBuilder output,uint capacity);
    internal static void WriteLightingControl(SettingsStore store){string path=Path.Combine(store.DirectoryPath,"lighting.ini");File.WriteAllText(path+".tmp","[Lighting]\nEnabled="+(store.Snapshot().LightingFix?"1":"0")+"\n");File.Move(path+".tmp",path,true);}
    internal static ProcessStartInfo CreateStartInfo(LabSettings settings,string dataDirectory,string? lightingFolder=null)
    {
        string path=Path.GetFullPath(settings.GamePath);if(!File.Exists(path)||!Path.GetFileName(path).Equals("DOOMEternalx64vk.exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Select an existing DOOMEternalx64vk.exe.");
        var info=new ProcessStartInfo(path){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(path)!};info.ArgumentList.Add("+com_skipIntroVideo");info.ArgumentList.Add("1");info.Environment["LAB_DATA_DIRECTORY"]=dataDirectory;info.Environment["LAB_LIGHTING_FIX"]=settings.LightingFix?"1":"0";
        if(settings.LightingFix){if(lightingFolder is null)throw new InvalidOperationException("Lighting runtime is not prepared.");info.Environment.TryGetValue("VK_INSTANCE_LAYERS",out string? oldLayers);info.Environment["VK_INSTANCE_LAYERS"]=string.Join(';',(oldLayers??"").Split(';',StringSplitOptions.RemoveEmptyEntries).Append("VK_LAYER_LAB_lighting").Distinct());info.Environment.TryGetValue("VK_LAYER_PATH",out string? oldPath);info.Environment["VK_LAYER_PATH"]=string.IsNullOrEmpty(oldPath)?lightingFolder:lightingFolder+";"+oldPath;info.Environment["SteamAppId"]="782330";}
        return info;
    }
    internal static void Start(SettingsStore store){var s=store.Snapshot();string? folder=s.LightingFix?PrepareLighting(store):null;Process.Start(CreateStartInfo(s,store.DirectoryPath,folder))?.Dispose();}
}
