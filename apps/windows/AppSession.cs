using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
namespace ShowDevice;

// Owns the one local core independently of any visible window.
internal sealed class AppSession : IDisposable {
    private readonly DispatcherQueue dispatcher;
    private readonly SemaphoreSlim commands=new(1,1);
    private Process? core;
    private bool disposed;
    private readonly Windows.UI.ViewManagement.UISettings systemUi=new();
    public bool SystemDark {get{var color=systemUi.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);return color.R+color.G+color.B<384;}}
    public event Action? ThemeChanged;
    private string visibility="{\"type\":\"visibility\",\"all\":false,\"device_ids\":[]}";
    public AppSettings Settings {get;private set;}=AppSettings.Load();
    public JsonElement? Latest {get;private set;}
    public event Action<JsonElement>? Snapshot;
    public event Action<string>? Failed;
    public event Action? PreferencesChanged;
    public bool IsRunning=>core is {HasExited:false};
    public AppSession(DispatcherQueue dispatcher){this.dispatcher=dispatcher;L.Language=Settings.Language;systemUi.ColorValuesChanged+=SystemColorsChanged;}
    private void SystemColorsChanged(Windows.UI.ViewManagement.UISettings sender,object args)=>dispatcher.TryEnqueue(()=>{if(!disposed)ThemeChanged?.Invoke();});
    public Microsoft.UI.Xaml.ElementTheme ResolveTheme(string surface="app")=>ThemePolicy.Resolve(Settings.Theme,surface,SystemDark)=="dark"?Microsoft.UI.Xaml.ElementTheme.Dark:Microsoft.UI.Xaml.ElementTheme.Light;
    public async Task Start(){
        try{
            string? root=Environment.GetEnvironmentVariable("SHOWDEVICE_HOME");
            if(string.IsNullOrEmpty(root)){
                var dir=new DirectoryInfo(AppContext.BaseDirectory);
                while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"dist","showdevice.exe")))dir=dir.Parent;
                root=dir?.FullName;
            }
            string executable=Path.Combine(root??AppContext.BaseDirectory,root!=null?"dist/showdevice.exe":"showdevice.exe");
            string plugins=Path.Combine(root??AppContext.BaseDirectory,root!=null?"dist/plugins":"plugins");
            var info=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardInput=true,RedirectStandardError=true,CreateNoWindow=true};
            info.Environment["SHOWDEVICE_POLL_PREFERENCES"]=JsonSerializer.Serialize(Settings.PollPreferences());
            info.Environment["SHOWDEVICE_VISIBILITY_MANAGED"]="1";
            info.ArgumentList.Add(Environment.GetEnvironmentVariable("SHOWDEVICE_DEMO")=="1"?"demo":"watch");info.ArgumentList.Add(plugins);
            core=Process.Start(info)??throw new IOException("Local core unavailable");
            var errors=DrainErrors(core.StandardError);
            await Send(visibility);
            while(!disposed){
                string? line=await ReadLine(core.StandardOutput);if(line==null)break;
                using var document=JsonDocument.Parse(line,new JsonDocumentOptions{MaxDepth=64});
                var snapshot=document.RootElement.Clone();
                dispatcher.TryEnqueue(()=>{if(disposed)return;Latest=snapshot;Snapshot?.Invoke(snapshot);});
            }
            var message=await errors;
            if(!disposed)dispatcher.TryEnqueue(()=>Failed?.Invoke("Local core stopped. "+message));
        }catch(Exception e){if(!disposed)dispatcher.TryEnqueue(()=>Failed?.Invoke(e.Message));}
    }
    private static async Task<string> DrainErrors(StreamReader reader){
        var tail="";var buffer=new char[1024];int count;
        while((count=await reader.ReadAsync(buffer))>0){tail+=new string(buffer,0,count);if(tail.Length>1000)tail=tail[^1000..];}
        return tail;
    }
    private static async Task<string?> ReadLine(StreamReader reader){
        var text=new System.Text.StringBuilder();var c=new char[1];
        while(await reader.ReadAsync(c)!=0){if(c[0]=='\n')return text.ToString();if(text.Length>=2097152)throw new IOException("Core frame exceeded limit");text.Append(c[0]);}
        return text.Length==0?null:throw new IOException("Truncated core frame");
    }
    public async Task Send(string command){
        await commands.WaitAsync();
        try{if(!disposed&&IsRunning){await core!.StandardInput.WriteLineAsync(command);await core.StandardInput.FlushAsync();}}
        catch(Exception e) when(e is IOException or InvalidOperationException){Failed?.Invoke(e.Message);}
        finally{commands.Release();}
    }
    public Task Interest(bool all,IEnumerable<string> ids){
        visibility=JsonSerializer.Serialize(new{type="visibility",all,device_ids=ids.Order().ToArray()});
        return Send(visibility);
    }
    public async Task UpdateSettings(AppSettings settings){
        Settings=settings;L.Language=settings.Language;PreferencesChanged?.Invoke();
        await Send(JsonSerializer.Serialize(new{type="settings",preferences=settings.PollPreferences()}));
    }
    public void Dispose(){
        disposed=true;
        systemUi.ColorValuesChanged-=SystemColorsChanged;
        try{core?.StandardInput.Close();if(core is {HasExited:false}&&!core.WaitForExit(1200))core.Kill(true);}catch(Exception e){Debug.WriteLine(e.Message);}
        core?.Dispose();
    }
}
