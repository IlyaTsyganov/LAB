using System.Globalization;
using Lab.Overlay;
namespace Lab.Runtime;
internal static class ConsoleControls
{
    private static readonly string[] Targets=["current","landing","vertical"];
    internal static bool Apply(LabSettings s,int command,string[] args)
    {
        if(command==6){if(args.Length is not(3 or 4))return false;string target=args.Length==4?args[3].ToLowerInvariant():"";if(target!=""&&!Targets.Contains(target))return false;var rgb=new int[3];for(int i=0;i<3;i++)if(!int.TryParse(args[i],NumberStyles.None,CultureInfo.InvariantCulture,out rgb[i])||rgb[i]<0||rgb[i]>255)return false;foreach(string key in target==""?Targets:[target])s.Meter(key).Color=$"#{rgb[0]:x2}{rgb[1]:x2}{rgb[2]:x2}";return true;}
        if(args.Length is not(1 or 2)||!double.TryParse(args[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n))return false;
        string t=args.Length==2?args[1].ToLowerInvariant():"";if(t!=""&&!Targets.Contains(t))return false;
        if(command is 4 or 5 or 7){if(t!="")return false;if(command==4&&n>=0&&n<=100){s.TintOpacity=n;return true;}if(n is not(0 or 1))return false;if(command==5)s.Landing.Enabled=n==1;else if(command==7)s.Vertical.Enabled=n==1;else return false;return true;}
        if(command==0){if(n is not(0 or 1))return false;if(t==""){s.SpeedometersEnabled=n==1;if(n==1)s.Current.Enabled=true;}else s.Meter(t).Enabled=n==1;return true;}
        if(command is 1 or 2){if(n<0||n>int.MaxValue)return false;foreach(string key in t==""?Targets:[t]){if(command==1)s.Meter(key).X=(int)Math.Round(n);else s.Meter(key).Y=(int)Math.Round(n);}return true;}
        if(command==3&&n>=.1&&n<=10){foreach(string key in t==""?Targets:[t])s.Meter(key).Size=n;return true;}return false;
    }
    internal static string Help(LabSettings s,int command)
    {
        if(command<0||command>=NativeSpeedometerCode.Commands.Length)return "Unknown LAB command.\n";
        string F(double v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
        string value=command switch{0=>$"overall={(s.SpeedometersEnabled?1:0)} current={(s.Current.Enabled?1:0)} landing={(s.Landing.Enabled?1:0)} vertical={(s.Vertical.Enabled?1:0)}",1=>string.Join(" ",Targets.Select(k=>$"{k}={s.Meter(k).X}")),2=>string.Join(" ",Targets.Select(k=>$"{k}={s.Meter(k).Y}")),3=>string.Join(" ",Targets.Select(k=>$"{k}={F(s.Meter(k).Size)}")),4=>F(s.TintOpacity),5=>s.Landing.Enabled?"1":"0",6=>string.Join(" ",Targets.Select(k=>$"{k}={s.Meter(k).Color}")),7=>s.Vertical.Enabled?"1":"0",_=>""};
        string help=command switch{0=>"<0|1> [current|landing|vertical]: visibility; main switch preserves secondary choices.",1 or 2=>"<pixels> [current|landing|vertical]: physical-pixel position; omitted target changes all.",3=>"<0.1..10> [current|landing|vertical]: independent text size; omitted target changes all.",4=>"<0..100>: opacity of both low-health red layers; 100 is original.",5=>"<0|1>: landing horizontal speed, including brief impact peak.",6=>"<R G B> [current|landing|vertical]: RGB 0..255; omitted target changes all.",7=>"<0|1>: vertical speed magnitude, without a direction sign.",_=>""};
        return $"{NativeSpeedometerCode.Commands[command]} is: {value}\n{help}\n";
    }
}
