using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
namespace ShowDevice;
public sealed class MainWindow : Window {
    private readonly ListView devices = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly StackPanel detail = new() { Spacing = 4, Padding = new Thickness(12,0,12,24) };
    private readonly TextBlock status = new() { Text = "Loading…", TextWrapping = TextWrapping.Wrap };
    private readonly InfoBar error = new() { IsClosable = false, Severity = InfoBarSeverity.Warning };
    private readonly Button refresh = new() { Content = "Refresh", IsEnabled = false };
    private readonly Dictionary<string, JsonElement> models = new();
    private readonly Dictionary<string,ListViewItem> deviceItems = new();
    private readonly StackPanel readings = new(){Spacing=4};
    private readonly InfoBar deviceError = new(){IsClosable=false,Severity=InfoBarSeverity.Warning,Title="Reading unavailable"};
    private bool applying;
    private JsonElement? renderedDevice;
    private bool emptyRendered;
    private Process? core;
    private string? selected;
    private string previous = "";
    private TextBlock? updated;
    private readonly DispatcherTimer clock = new(){Interval=TimeSpan.FromSeconds(1)};
    public MainWindow() {
        Title = "ShowDevice";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1080,740));
        SystemBackdrop = new MicaBackdrop();
        var root = new Grid { Padding = new Thickness(24), RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Spacing=4 };
        heading.Children.Add(new TextBlock { Text="Devices", FontSize=28, FontWeight=Microsoft.UI.Text.FontWeights.SemiBold });
        heading.Children.Add(status);
        header.Children.Add(heading); header.Children.Add(refresh); Grid.SetColumn(refresh,1);
        root.Children.Add(header); root.Children.Add(error); Grid.SetRow(error,1);
        var body=new Grid { ColumnSpacing=20 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        AutomationProperties.SetName(devices,"Connected devices");
        body.Children.Add(devices);
        var scroll=new ScrollViewer { Content=detail, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        body.Children.Add(scroll);Grid.SetColumn(scroll,1);root.Children.Add(body);Grid.SetRow(body,2);
        Content=root;
        clock.Tick += (_,_) => {if(updated!=null&&selected!=null&&models.TryGetValue(selected,out var current))DeviceVisuals.UpdateTime(updated,current);};clock.Start();
        devices.SelectionChanged += (_,_) => { if(!applying&&devices.SelectedItem is ListViewItem item && item.Tag is string id){selected=id;Render();} };
        refresh.Click += async (_,_) => {
            try { if(core is { HasExited:false }) { refresh.IsEnabled=false;await core.StandardInput.WriteLineAsync("refresh");await core.StandardInput.FlushAsync();await Task.Delay(2000);refresh.IsEnabled=true; } }
            catch(Exception e){ ShowError(e.Message); }
        };
        Closed += (_,_) => {clock.Stop();try{if(core is { HasExited:false })core.Kill(entireProcessTree:true);}catch{}core?.Dispose();};
        _=StartCore();
    }
    private async Task StartCore(){
        try {
            string? root=Environment.GetEnvironmentVariable("SHOWDEVICE_HOME");
            if(string.IsNullOrEmpty(root)){
                var d=new DirectoryInfo(AppContext.BaseDirectory);
                while(d!=null && !File.Exists(Path.Combine(d.FullName,"dist","showdevice.exe")))d=d.Parent;
                root=d?.FullName;
            }
            string executable=root!=null?Path.Combine(root,"dist","showdevice.exe"):Path.Combine(AppContext.BaseDirectory,"showdevice.exe");
            string plugins=root!=null?Path.Combine(root,"dist","plugins"):Path.Combine(AppContext.BaseDirectory,"plugins");
            if(!File.Exists(executable))throw new IOException("Local core not found. Build the workspace and run showdevice package, then reopen the app.");
            var info=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardInput=true,RedirectStandardError=true,CreateNoWindow=true};
            info.ArgumentList.Add(Environment.GetEnvironmentVariable("SHOWDEVICE_DEMO")=="1"?"demo":"watch");info.ArgumentList.Add(plugins);
            core=Process.Start(info)??throw new IOException("Unable to start local core.");
            var stderr=core.StandardError.ReadToEndAsync();
            refresh.IsEnabled=true;
            while(true){
                string? line=await ReadBoundedLine(core.StandardOutput);
                if(line==null)break;
                using var document=JsonDocument.Parse(line,new JsonDocumentOptions{MaxDepth=64});
                var snapshot=document.RootElement.Clone();
                DispatcherQueue.TryEnqueue(()=>Apply(snapshot));
            }
            var message=await stderr;
            DispatcherQueue.TryEnqueue(()=>{refresh.IsEnabled=false;ShowError("Local core stopped. "+message[..Math.Min(message.Length,500)]);});
        } catch(Exception e){DispatcherQueue.TryEnqueue(()=>ShowError(e.Message));}
    }
    private static async Task<string?> ReadBoundedLine(StreamReader reader){
        var buffer=new System.Text.StringBuilder();var c=new char[1];
        while(await reader.ReadAsync(c.AsMemory())!=0){if(c[0]=='\n')return buffer.ToString();if(buffer.Length>=2097152)throw new IOException("Core frame exceeded limit.");buffer.Append(c[0]);}
        return buffer.Length==0?null:throw new IOException("Truncated core frame.");
    }
    private void Apply(JsonElement snapshot){
        var data=snapshot.GetProperty("devices");
        string signature=data.GetRawText()+snapshot.GetProperty("diagnostics").GetRawText();
        if(signature==previous)return;previous=signature;
        var diagnostics=snapshot.GetProperty("diagnostics").EnumerateArray().Select(x=>x.GetString()).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();
        error.IsOpen=diagnostics.Length>0;error.Message=string.Join("\n",diagnostics);
        applying=true;
        try{
            var incoming=data.EnumerateArray().ToArray();
            var ids=incoming.Select(item=>item.GetProperty("device").GetProperty("id").GetString()!).ToHashSet();
            foreach(var id in deviceItems.Keys.Where(id=>!ids.Contains(id)).ToArray()){
                devices.Items.Remove(deviceItems[id]);deviceItems.Remove(id);models.Remove(id);
            }
            foreach(var item in incoming){
                string id=item.GetProperty("device").GetProperty("id").GetString()!;
                if(!deviceItems.TryGetValue(id,out var row)){
                    row=new ListViewItem{Content=DeviceVisuals.ListRow(item),Tag=id,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                    deviceItems.Add(id,row);devices.Items.Add(row);
                }else if(!ViewChanges.ListRow(models[id],item)){
                    row.Content=DeviceVisuals.ListRow(item);
                }
                models[id]=item.Clone();
            }
            if(selected==null||!models.ContainsKey(selected))selected=models.Keys.FirstOrDefault();
            var target=selected!=null?deviceItems[selected]:null;
            if(!ReferenceEquals(devices.SelectedItem,target))devices.SelectedItem=target;
        }finally{applying=false;}
        status.Visibility=Visibility.Collapsed;
        Render();
    }
    private void Render(){
        if(selected==null||!models.TryGetValue(selected,out var device)){
            if(emptyRendered)return;
            updated=null;renderedDevice=null;detail.Children.Clear();readings.Children.Clear();emptyRendered=true;
            detail.Children.Add(new TextBlock{Text="Connect a supported device",FontSize=22});
            detail.Children.Add(new TextBlock{Text="Connect a receiver or USB cable, then refresh.",TextWrapping=TextWrapping.Wrap});
            return;
        }
        bool headerChanged=renderedDevice is not JsonElement oldHeader||!ViewChanges.Header(oldHeader,device);
        bool bodyChanged=headerChanged||renderedDevice is not JsonElement oldBody||!ViewChanges.Body(oldBody,device);
        if(headerChanged){
            detail.Children.Clear();updated=new TextBlock();
            detail.Children.Add(DeviceVisuals.Header(device,updated));
            detail.Children.Add(deviceError);detail.Children.Add(readings);
        }
        if(bodyChanged){readings.Children.Clear();PluginPage.Render(readings,device);}
        if(updated!=null)DeviceVisuals.UpdateTime(updated,device);
        deviceError.IsOpen=device.TryGetProperty("error",out var issue)&&issue.ValueKind==JsonValueKind.String;
        deviceError.Message=deviceError.IsOpen?issue.GetString():null;
        renderedDevice=device;emptyRendered=false;
    }
    private void ShowError(string message){error.IsOpen=true;error.Title="Unable to read devices";error.Message=message;}
}


