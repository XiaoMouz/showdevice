using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ShowDevice.Updates;

internal static class Program {
    static readonly string Root=AppContext.BaseDirectory;
    static readonly string Cache=Path.Combine(Root,"updates");
    static readonly string StatePath=Path.Combine(Root,"install-state.json");
    static readonly string StatusPath=Path.Combine(Cache,"status.json");
    static ReleaseConfig Config(){using var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("ShowDevice.Launcher.release-config.json")!;return JsonSerializer.Deserialize<ReleaseConfig>(s)!;}
    static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path))??throw new InvalidDataException("Invalid local update state");
    static string Slot(string version){if(!UpdateContract.ValidVersion(version))throw new InvalidDataException("Invalid installed version");return Path.Combine(Root,"versions",version);}
    static bool Enabled(){
        try{var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShowDevice","settings.json");if(!File.Exists(path))return true;using var doc=JsonDocument.Parse(File.ReadAllText(path));return !doc.RootElement.TryGetProperty("AutoUpdate",out var flag)||flag.GetBoolean();}catch{return false;}
    }
    static bool Busy(){
        try{using var existing=Mutex.OpenExisting("Local\\ShowDevice.Native");return true;}catch(WaitHandleCannotBeOpenedException){}
        // Do not switch versions while an old owned UI/core/plugin still runs.
        foreach(var process in Process.GetProcesses())using(process){try{var path=process.MainModule?.FileName;if(path!=null&&Path.GetFullPath(path).StartsWith(Path.Combine(Root,"versions")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return true;}catch{}}
        return false;
    }
    static void Status(string status,string? version=null){Directory.CreateDirectory(Cache);UpdateContract.WriteJson(StatusPath,new{Status=status,Version=version});}
    static FileStream? Lock(string path){try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){return null;}}
    static async Task<int> Main(string[] args){
        try{
            Directory.CreateDirectory(Cache);
            if(args.Contains("--check")){
                using var checkLock=Lock(Path.Combine(Cache,"download.lock"));if(checkLock==null)return 0;
                try{if(Enabled())await Download();}catch{Status("check_failed");return 1;}return 0;
            }
            using var launchLock=new FileStream(Path.Combine(Root,"launch.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var state=Read<InstallState>(StatePath);
            if(Busy()){Start(state.Current,args,null);return 0;}
            if(state.Trial&&state.Previous!=null){state=new(state.Previous,null,false,state.Current);UpdateContract.WriteJson(StatePath,state);Status("rolled_back");}
            var pending=Path.Combine(Cache,"pending.json");
            using var installLock=Lock(Path.Combine(Cache,"download.lock"));
            if(installLock!=null&&Enabled()&&UpdateContract.Configured(Config())&&File.Exists(pending)){
                try{
                    var m=UpdateContract.Verify(Read<UpdateEnvelope>(pending),Config().PublicKeyPem,Config().Flavor);
                    if(UpdateContract.Newer(m.Version,state.Current)&&m.Version!=state.Rejected){
                        var archive=Path.Combine(Cache,"package.zip");UpdateContract.VerifyArchive(archive,m);
                        var stage=Path.Combine(Root,"versions","stage-"+Guid.NewGuid().ToString("N"));
                        UpdateContract.Extract(archive,stage);
                        if(File.ReadAllText(Path.Combine(stage,"release-version.txt")).Trim()!=m.Version)throw new InvalidDataException("Version mismatch");
                        Directory.Move(stage,Slot(m.Version));
                        state=new(m.Version,state.Current,true,state.Rejected);UpdateContract.WriteJson(StatePath,state);
                    }
                }catch{Status("install_failed");} // Keep the last usable version on any validation/install error.
            }
            if(!state.Trial){Start(state.Current,args,null);return 0;}
            var health=Path.Combine(Cache,"health-"+Guid.NewGuid().ToString("N"));
            Process? candidate=null;bool healthy=false;
            try{
                candidate=Start(state.Current,args,health);
                for(int i=0;i<150;i++){await Task.Delay(200);if(candidate.HasExited)break;if(File.Exists(health)){healthy=true;break;}}
            }catch{}
            if(healthy){UpdateContract.WriteJson(StatePath,state with{Trial=false});Status("updated",state.Current);File.Delete(pending);}
            else{
                if(candidate is {HasExited:false}){candidate.Kill(true);await candidate.WaitForExitAsync();}
                var fallback=new InstallState(state.Previous!,null,false,state.Current);UpdateContract.WriteJson(StatePath,fallback);Status("rolled_back");Start(fallback.Current,args,null);
            }
            candidate?.Dispose();if(File.Exists(health))File.Delete(health);return 0;
        }catch(IOException){return 1;}catch(Exception){try{Status("check_failed");}catch{}return 1;}
    }
    static Process Start(string version,string[] args,string? health){
        var folder=Slot(version);var start=new ProcessStartInfo(Path.Combine(folder,"ShowDevice.exe")){UseShellExecute=false,WorkingDirectory=folder};
        if(args.Contains("--tray"))start.ArgumentList.Add("--tray");
        if(args.Contains("--background"))start.ArgumentList.Add("--background");
        start.Environment["SHOWDEVICE_HOME"]=folder;
        start.Environment["SHOWDEVICE_INSTALL_ROOT"]=Root;
        start.Environment["SHOWDEVICE_UPDATE_HEALTH"]=health??"";
        return Process.Start(start)??throw new IOException("Unable to start application");
    }
    static async Task Download(){
        var config=Config();if(!UpdateContract.Configured(config)){Status("not_configured");return;}
        var state=Read<InstallState>(StatePath);
        if(state.Trial)return;
        if(File.Exists(Path.Combine(Cache,"pending.json"))){
            try{var ready=UpdateContract.Verify(Read<UpdateEnvelope>(Path.Combine(Cache,"pending.json")),config.PublicKeyPem,config.Flavor);UpdateContract.VerifyArchive(Path.Combine(Cache,"package.zip"),ready);if(UpdateContract.Newer(ready.Version,state.Current)&&ready.Version!=state.Rejected){Status("ready",ready.Version);return;}}catch{}
        }
        using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(10)};client.DefaultRequestHeaders.UserAgent.ParseAdd("ShowDevice-Updater/1");
        var baseUrl=$"https://github.com/{config.Repository}/releases/latest/download/";
        var metadata=config.Flavor=="minimal"?"update-minimal":"update";
        var manifest=await Fetch(client,baseUrl+metadata+".json",16384);
        var signature=await Fetch(client,baseUrl+metadata+".sig",2048);
        var envelope=new UpdateEnvelope(Convert.ToBase64String(manifest),System.Text.Encoding.UTF8.GetString(signature).Trim());
        var m=UpdateContract.Verify(envelope,config.PublicKeyPem,config.Flavor);
        if(!UpdateContract.Newer(m.Version,state.Current)||m.Version==state.Rejected){Status(m.Version==state.Rejected?"rolled_back":"current");return;}
        Status("downloading",m.Version);
        var temp=Path.Combine(Cache,"package.download");
        try{
            using var response=await client.GetAsync($"https://github.com/{config.Repository}/releases/download/v{m.Version}/{m.Archive}",HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("Insecure redirect");
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await using(var input=await response.Content.ReadAsStreamAsync(deadline.Token))await using(var output=File.Create(temp)){
                var buffer=new byte[65536];long total=0;int count;
                while((count=await input.ReadAsync(buffer,deadline.Token))>0){total+=count;if(total>m.Size)throw new InvalidDataException("Oversized download");await output.WriteAsync(buffer.AsMemory(0,count),deadline.Token);}
            }
            UpdateContract.VerifyArchive(temp,m);File.Move(temp,Path.Combine(Cache,"package.zip"),true);
            UpdateContract.WriteJson(Path.Combine(Cache,"pending.json"),envelope);Status("ready",m.Version);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static async Task<byte[]> Fetch(HttpClient client,string url,int limit){
        using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
        if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("Insecure redirect");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);using var output=new MemoryStream();var buffer=new byte[4096];int n;
        while((n=await input.ReadAsync(buffer,timeout.Token))>0){if(output.Length+n>limit)throw new InvalidDataException("Oversized metadata");output.Write(buffer,0,n);}return output.ToArray();
    }
}
