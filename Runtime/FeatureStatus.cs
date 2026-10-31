namespace Lab.Runtime;

// Availability is separate from the in-game letters, which mirror preferences.
internal sealed record FeatureStatus(string Lighting,string Modbot,string Tint)
{
    internal static FeatureStatus Restart {get;}=new("Restart game","Restart game","Restart game");

    internal static FeatureStatus Pending(LabSettings settings)=>new(settings.LightingFix?"Waiting":"Off",settings.ModbotFix?"Waiting":"Off",settings.TintOpacity==100?"Default · 100%":"Waiting");

    internal static FeatureStatus Describe(LabSettings settings,string version,string driver,bool lightingActive,
        bool modbotAttached,int maxHz,string? modbotFailure,bool tintAttached)
    {
        string lighting=!settings.LightingFix?"Off":lightingActive?"Active":
            version is not ("1.0 (Release)" or "1.1" or "2.0")?"Unavailable · requires game 1.0, 1.1 or 2.0":
            driver.Contains("AMD",StringComparison.OrdinalIgnoreCase)||driver.Contains("Intel",StringComparison.OrdinalIgnoreCase)?"Unavailable · NVIDIA GPU required":
            "Inactive · launch through LAB on NVIDIA";
        string modbot=!settings.ModbotFix?"Off":!modbotAttached?
            (modbotFailure is null?"Waiting for input protection":"Unavailable"):
            maxHz is >0 and <1000?"Active":maxHz>=1000?$"Standby · internal FPS limit is {maxHz}":
            "Standby · internal FPS limit unavailable";
        string tint=settings.TintOpacity==100?"Default · 100%":tintAttached?"Active":"Unavailable · tint adapter not ready";
        return new(lighting,modbot,tint);
    }
}
