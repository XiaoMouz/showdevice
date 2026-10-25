using Microsoft.Win32;
namespace ShowDevice;
internal static class StartupRegistration {
    private const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name="ShowDevice";
    public static bool Enabled {get{using var key=Registry.CurrentUser.OpenSubKey(Key);return key?.GetValue(Name) is string text&&!string.IsNullOrWhiteSpace(text);}}
    public static void Set(bool enabled){
        if(Environment.GetEnvironmentVariable("SHOWDEVICE_DEMO")=="1")return;
        using var key=Registry.CurrentUser.CreateSubKey(Key,true);
        if(!enabled){key.DeleteValue(Name,false);return;}
        // Stable launcher survives version-directory updates and rollback.
        string executable=UpdateIntegration.InstallRoot is string root?Path.Combine(root,"ShowDevice.Launcher.exe"):Environment.ProcessPath??throw new IOException("Application path unavailable");
        key.SetValue(Name,$"\"{executable}\" --background",RegistryValueKind.String);
    }
}
