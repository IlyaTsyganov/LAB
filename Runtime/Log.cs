namespace Lab.Runtime;
internal static class Log
{
    private static readonly object Gate=new();
    internal static string DirectoryPath { get; }=Path.Combine(AppContext.BaseDirectory,"logs");
    internal static string FilePath=>Path.Combine(DirectoryPath,"lab.log");
    internal static void Information(string text,params object?[] args)=>Write("INFO",text,args);
    internal static void Warning(string text,params object?[] args)=>Write("WARN",text,args);
    internal static void Warning(Exception error,string text)=>Write("WARN",text+": "+error.Message,[]);
    internal static void Error(Exception error,string text)=>Write("ERROR",text+": "+error,[]);
    private static void Write(string level,string text,object?[] args)
    {lock(Gate){try{Directory.CreateDirectory(DirectoryPath);File.AppendAllText(FilePath,$"{DateTimeOffset.Now:O} {level} {text} {string.Join(" ",args)}\n");}catch(IOException){}catch(UnauthorizedAccessException){}}}
}
