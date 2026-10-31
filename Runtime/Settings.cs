using System.Text.Json;
using System.Globalization;
namespace Lab.Runtime;
internal sealed class MeterSettings
{
    public bool Enabled {get;set;}=true;
    public int X {get;set;}
    public int Y {get;set;}=860;
    public double Size {get;set;}=3;
    public string Color {get;set;}="#ffffff";
}
internal sealed class LabSettings
{
    public string GamePath {get;set;}="";
    public bool LightingFix {get;set;}=true;
    public bool ModbotFix {get;set;}=true;
    public bool HideDriverName {get;set;}=true;
    public bool SpeedometersEnabled {get;set;}=false;
    public double TintOpacity {get;set;}=100;
    public bool VerticalShowSign {get;set;}=true;
    public MeterSettings Current {get;set;}=new(){X=160,Color="#f1f1f1"};
    public MeterSettings Landing {get;set;}=new(){X=720,Color="#e4ba73"};
    public MeterSettings Vertical {get;set;}=new(){X=1280,Color="#8ad5e9"};
    internal const string DefaultGamePath=@"C:\Program Files (x86)\Steam\steamapps\common\DOOMEternal\DOOMEternalx64vk.exe";
    internal static LabSettings CreateDefaults(Func<string,bool>? fileExists=null)=>new(){GamePath=(fileExists??File.Exists)(DefaultGamePath)?DefaultGamePath:""};
    internal MeterSettings Meter(string key)=>key switch{"current"=>Current,"landing"=>Landing,"vertical"=>Vertical,_=>throw new ArgumentException("Unknown speedometer")};
}
internal sealed class SettingsStore
{
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,PropertyNameCaseInsensitive=true};
    private readonly object _gate=new();
    private LabSettings _value=new();
    internal string DirectoryPath {get;}
    internal string FilePath=>Path.Combine(DirectoryPath,"settings.json");
    internal SettingsStore(string? directory=null)
    {
        directory??=Environment.GetEnvironmentVariable("LAB_DATA_DIRECTORY");
        if(directory is not null&&!Path.IsPathFullyQualified(directory))throw new ArgumentException("LAB_DATA_DIRECTORY must be an absolute path.");
        DirectoryPath=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"lab");
        Directory.CreateDirectory(DirectoryPath);
        if(!File.Exists(FilePath)){_value=LabSettings.CreateDefaults();return;}
        if(File.Exists(FilePath))try{var loaded=JsonSerializer.Deserialize<LabSettings>(File.ReadAllText(FilePath),Json)??new();Normalize(loaded);_value=loaded;}catch(Exception ex)when(ex is JsonException or IOException or ArgumentException){Log.Warning(ex,"Could not read saved preferences; original file retained");}
    }
    internal LabSettings Snapshot(){lock(_gate)return Clone(_value);}
    internal bool Update(Func<LabSettings,bool> change)
    {
        lock(_gate){var next=Clone(_value);if(!change(next))return false;Normalize(next);var temp=FilePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(next,Json));File.Move(temp,FilePath,true);_value=next;return true;}
    }
    internal bool Apply(string key,JsonElement value)=>Update(s=>
    {
        if(key=="gamePath"&&value.ValueKind==JsonValueKind.String){s.GamePath=value.GetString()??"";return true;}
        if(value.ValueKind is JsonValueKind.True or JsonValueKind.False){bool v=value.GetBoolean();switch(key){case "lightingFix":s.LightingFix=v;return true;case "modbotFix":s.ModbotFix=v;return true;case "hideDriverName":s.HideDriverName=v;return true;case "speedometersEnabled":s.SpeedometersEnabled=v;return true;case "verticalShowSign":s.VerticalShowSign=v;return true;}}
        if(key=="tintOpacity"&&Number(value,0,100,out double tint)){s.TintOpacity=tint;return true;}
        string[] parts=key.Split('.');if(parts.Length!=2||!new[]{"current","landing","vertical"}.Contains(parts[0]))return false;
        var m=s.Meter(parts[0]);switch(parts[1]){
            case "enabled" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:m.Enabled=value.GetBoolean();return true;
            case "x" when Number(value,0,int.MaxValue,out double x)&&x==Math.Truncate(x):m.X=(int)x;return true;
            case "y" when Number(value,0,int.MaxValue,out double y)&&y==Math.Truncate(y):m.Y=(int)y;return true;
            case "size" when Number(value,.1,10,out double size):m.Size=size;return true;
            case "color" when value.ValueKind==JsonValueKind.String&&ValidColor(value.GetString()):m.Color=value.GetString()!.ToLowerInvariant();return true;
            default:return false;
        }
    });
    internal static bool ValidColor(string? s)=>s?.Length==7&&s[0]=='#'&&s.Skip(1).All(Uri.IsHexDigit);
    private static bool Number(JsonElement e,double min,double max,out double n){n=0;return e.ValueKind==JsonValueKind.Number&&e.TryGetDouble(out n)&&double.IsFinite(n)&&n>=min&&n<=max;}
    private static LabSettings Clone(LabSettings s)=>JsonSerializer.Deserialize<LabSettings>(JsonSerializer.Serialize(s,Json),Json)!;
    private static void Normalize(LabSettings s)
    {s.GamePath??="";s.Current??=new(){X=160};s.Landing??=new(){X=720};s.Vertical??=new(){X=1280};if(!double.IsFinite(s.TintOpacity))s.TintOpacity=100;s.TintOpacity=Math.Clamp(s.TintOpacity,0,100);foreach(var m in new[]{s.Current,s.Landing,s.Vertical}){m.X=Math.Max(0,m.X);m.Y=Math.Max(0,m.Y);m.Size=double.IsFinite(m.Size)?Math.Clamp(m.Size,.1,10):3;if(!ValidColor(m.Color))m.Color="#ffffff";}}
}
