using Lab.Runtime;
using System.Globalization;
namespace Lab;
internal static class Program
{
    [STAThread]private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture=CultureInfo.GetCultureInfo("en-US");CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("en-US");
        bool preview=args.Contains("--preview")||args.Contains("--qa-output");if(preview&&Environment.GetEnvironmentVariable("LAB_DATA_DIRECTORY") is null)Environment.SetEnvironmentVariable("LAB_DATA_DIRECTORY",Path.Combine(Path.GetTempPath(),"lab-preview-"+Guid.NewGuid().ToString("N")));
        try{ApplicationConfiguration.Initialize();using var storeOwner=new LabHost(new SettingsStore(),preview,args);Application.Run(storeOwner);return storeOwner.StartupError?1:0;}
        catch(Exception ex){Log.Error(ex,"LAB startup failed");return 1;}
    }
}
