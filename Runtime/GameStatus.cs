namespace Lab.Runtime;

internal static class GameStatus
{
    internal const string NotRunning="Game not running";
    internal static string Format(bool running,string version,bool preexisting,bool launchedByLab)=>
        !running?NotRunning:preexisting&&!launchedByLab?"Restart game":"Running "+version;
}
