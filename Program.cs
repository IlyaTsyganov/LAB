using Lab.Runtime;
using System.Globalization;
namespace Lab;
internal static class Program
{
    [STAThread]private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture=CultureInfo.GetCultureInfo("en-US");CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("en-US");
        bool preview=args.Contains("--preview")||args.Contains("--qa-output")||Environment.GetEnvironmentVariable("LAB_PREVIEW")=="1";if(preview&&Environment.GetEnvironmentVariable("LAB_DATA_DIRECTORY") is null)Environment.SetEnvironmentVariable("LAB_DATA_DIRECTORY",Path.Combine(Path.GetTempPath(),"lab-preview-"+Guid.NewGuid().ToString("N")));
        try
        {
            ApplicationConfiguration.Initialize();
            if(args is ["--apply-update",var job]){UpdateHelper.Run(job);return 0;}
            if(args is ["--finish-update",var completed])UpdateHelper.CleanCompleted(completed);
            if(preview&&Environment.GetEnvironmentVariable("LAB_UPDATE_QA_OUTPUT") is {Length:>0} output)args=["--qa-output",output];
            using var context=new Updates.StartupContext(preview,args);Application.Run(context);return context.StartupError?1:0;
        }
        catch(Exception ex){Log.Error(ex,"LAB startup failed");return 1;}
    }
}
