using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
namespace ShowDevice;
internal static class UpdateIntegration {
    private static DispatcherTimer? timer;
    private static Process? checker;
    public static string? InstallRoot {
        get {
            var root=Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Parent?.FullName;
            return root!=null&&File.Exists(Path.Combine(root,"install-state.json"))&&File.Exists(Path.Combine(root,"ShowDevice.Launcher.exe"))?root:null;
        }
    }
    public static string VersionLabel=>File.Exists(Path.Combine(AppContext.BaseDirectory,"release-version.txt"))?File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"release-version.txt")).Trim():"dev";
    public static bool RedirectToLauncher(){
        var root=InstallRoot;var launched=Environment.GetEnvironmentVariable("SHOWDEVICE_INSTALL_ROOT");
        if(root==null||launched?.TrimEnd(Path.DirectorySeparatorChar)==root)return false;
        var start=new ProcessStartInfo(Path.Combine(root,"ShowDevice.Launcher.exe")){UseShellExecute=false,CreateNoWindow=true};
        if(Environment.GetCommandLineArgs().Contains("--tray"))start.ArgumentList.Add("--tray");
        if(Environment.GetCommandLineArgs().Contains("--background"))start.ArgumentList.Add("--background");Process.Start(start);return true;
    }
    public static void Ready(){
        var root=InstallRoot;var health=Environment.GetEnvironmentVariable("SHOWDEVICE_UPDATE_HEALTH");
        if(root==null||string.IsNullOrEmpty(health))return;
        if(Path.GetFullPath(health).StartsWith(Path.Combine(root,"updates")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(health).StartsWith("health-",StringComparison.Ordinal)){
            try{File.WriteAllText(health,"ready");Environment.SetEnvironmentVariable("SHOWDEVICE_UPDATE_HEALTH",null);}catch(IOException){}
        }
    }
    public static void Begin(){
        if(InstallRoot==null)return;
        timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(15)};
        timer.Tick+=(_,_)=>{Check();timer.Interval=TimeSpan.FromHours(6);};timer.Start();
    }
    private static void Check(){
        if(!AppSettings.Load().AutoUpdate||InstallRoot is not {} root||checker is {HasExited:false})return;
        try{checker?.Dispose();checker=Process.Start(new ProcessStartInfo(Path.Combine(root,"ShowDevice.Launcher.exe"),"--check"){UseShellExecute=false,CreateNoWindow=true});}catch(Exception e){Debug.WriteLine(e);}
    }
    public static string Status(){
        if(InstallRoot is not {} root)return L.T("Updates are available in release builds");
        try{
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"updates","status.json")));
            return L.T(doc.RootElement.GetProperty("Status").GetString() switch{
                "ready"=>"Downloaded; installs next time you start the app",
                "downloading"=>"Downloading update…",
                "current" or "updated"=>"Up to date",
                "rolled_back"=>"Previous version restored",
                "not_configured"=>"Update source is not configured",
                "install_failed"=>"Update could not be installed; current version kept",
                _=>"Update check unavailable; will retry later"
            });
        }catch{return L.T("Checks automatically in the background");}
    }
}
