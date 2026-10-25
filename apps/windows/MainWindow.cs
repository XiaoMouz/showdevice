using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using Windows.Graphics;
namespace ShowDevice;
public sealed class MainWindow : Window {
    private readonly ListView devices = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly StackPanel detail = new() { Spacing = 4, Padding = new Thickness(12,0,12,24) };
    private readonly TextBlock status = new() { Text = "Loading…", TextWrapping = TextWrapping.Wrap };
    private readonly InfoBar error = new() { IsClosable = false, Severity = InfoBarSeverity.Warning };
    private readonly Button refresh = new() { Content = new FontIcon{Glyph="\uE72C",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=16}, IsEnabled = false, Width=28,Height=28,MinWidth=0,MinHeight=0,Padding=new Thickness(0) };
    private readonly Dictionary<string, JsonElement> models = new();
    private readonly Dictionary<string,ListViewItem> deviceItems = new();
    private readonly StackPanel readings = new(){Spacing=4};
    private readonly InfoBar deviceError = new(){IsClosable=false,Severity=InfoBarSeverity.Warning,Title="Reading unavailable"};
    private bool applying;
    private bool compactList;
    private bool settingsVisible;
    private AppSettings settings=AppSettings.Load();
    private readonly SemaphoreSlim commandLock=new(1,1);
    private WindowPreferences preferences=WindowPreferences.Load(WindowPreferences.FilePath);
    private readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(400)};
    private Storyboard? switchAnimation;
    private Storyboard? paneAnimation;
    private JsonElement? renderedDevice;
    private bool emptyRendered;
    private Process? core;
    private string? selected;
    private string previous = "";
    private TextBlock? updated;
    private readonly DispatcherTimer clock = new(){Interval=TimeSpan.FromSeconds(1)};
    public MainWindow() {
        Title = "ShowDevice";
        L.Language=settings.Language;
        RestoreWindow();
        compactList=preferences.CompactList;
        SystemBackdrop = new MicaBackdrop();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titleBar=new TitleBar{
            Title="ShowDevice",IsPaneToggleButtonVisible=true,
            IconSource=new BitmapIconSource{UriSource=new Uri("ms-appx:///Assets/showdevice-icon.png"),ShowAsMonochrome=false}
        };
        titleBar.Loaded+=(_,_)=>RoundTitleIcon(titleBar);
        ExtendsContentIntoTitleBar=true;
        AppWindow.TitleBar.ButtonBackgroundColor=Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor=Microsoft.UI.Colors.Transparent;
        root.Children.Add(titleBar);root.Children.Add(error);Grid.SetRow(error,1);
        error.Margin=new Thickness(12,0,12,0);
        var body=new Grid{Margin=new Thickness(12,12,12,0)};
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var paneSlot=new Grid{Width=compactList?64:304};
        paneSlot.SizeChanged+=(_,args)=>paneSlot.Clip=new RectangleGeometry{Rect=new Windows.Foundation.Rect(0,0,args.NewSize.Width,args.NewSize.Height)};
        body.Children.Add(paneSlot);
        var pane=new Grid{Width=288,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,16,0),RowSpacing=12};
        pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        pane.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var settingsIcon=new FontIcon{Glyph="\uE713",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=18};
        var settingsLabel=new TextBlock{Text=L.T("Settings"),VerticalAlignment=VerticalAlignment.Center};
        var settingsRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=14,Height=44};
        settingsRow.Children.Add(settingsIcon);settingsRow.Children.Add(settingsLabel);
        var settingsItem=new ListViewItem{Content=settingsRow,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var settingsNavigation=new ListView{SelectionMode=ListViewSelectionMode.Single,Margin=new Thickness(0,0,0,12)};
        settingsNavigation.Items.Add(settingsItem);
        pane.Children.Add(settingsNavigation);Grid.SetRow(settingsNavigation,3);
        var paneHeader=new Grid{Padding=new Thickness(12,0,0,0)};
        paneHeader.ColumnDefinitions.Add(new ColumnDefinition());
        paneHeader.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var paneTitle=new TextBlock{Text="Devices",FontSize=20,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
        paneHeader.Children.Add(paneTitle);
        paneHeader.Children.Add(refresh);Grid.SetColumn(refresh,1);
        AutomationProperties.SetName(refresh,"Refresh devices");ToolTipService.SetToolTip(refresh,"Refresh devices");
        pane.Children.Add(paneHeader);pane.Children.Add(status);Grid.SetRow(status,1);status.Margin=new Thickness(12,0,0,0);
        AutomationProperties.SetName(devices,"Connected devices");
        pane.Children.Add(devices);Grid.SetRow(devices,2);paneSlot.Children.Add(pane);
        var scroll=new ScrollViewer { Content=detail, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        body.Children.Add(scroll);Grid.SetColumn(scroll,1);root.Children.Add(body);Grid.SetRow(body,2);
        void UpdatePane(bool animate=false){
            bool useMotion=animate&&new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            var movers=new List<FrameworkElement>{refresh,settingsIcon};
            movers.AddRange(deviceItems.Values.Select(row=>((Grid)row.Content).Children[0]).OfType<FrameworkElement>());
            var positions=useMotion?movers.ToDictionary(element=>element,element=>element.TransformToVisual(body).TransformPoint(new Windows.Foundation.Point())):null;
            var sizes=useMotion?movers.OfType<FontIcon>().ToDictionary(icon=>icon,icon=>icon.FontSize*(icon.RenderTransform is CompositeTransform current?current.ScaleX:1)):null;
            // Read the animated value before stopping to support continuous reversal.
            double currentWidth=paneSlot.Width;
            paneAnimation?.Stop();paneAnimation=null;
            foreach(var element in movers)element.RenderTransform=null;
            double targetWidth=compactList?64:304;
            paneSlot.Width=targetWidth;
            pane.Width=compactList?56:288;
            pane.Margin=new Thickness(0,0,compactList?8:16,0);
            paneTitle.Visibility=compactList?Visibility.Collapsed:Visibility.Visible;
            paneHeader.Padding=compactList?new Thickness(0):new Thickness(12,0,0,0);
            Grid.SetColumn(refresh,compactList?0:1);Grid.SetColumnSpan(refresh,compactList?2:1);
            refresh.HorizontalAlignment=compactList?HorizontalAlignment.Center:HorizontalAlignment.Right;
            settingsLabel.Visibility=compactList?Visibility.Collapsed:Visibility.Visible;
            settingsItem.Width=compactList?48:double.NaN;
            settingsItem.HorizontalAlignment=compactList?HorizontalAlignment.Center:HorizontalAlignment.Stretch;
            settingsItem.Padding=compactList?new Thickness(0):new Thickness(18,0,12,0);
            settingsRow.HorizontalAlignment=compactList?HorizontalAlignment.Center:HorizontalAlignment.Left;
            foreach(var row in deviceItems.Values)UpdateRowLayout(row);
            if(!useMotion)return;
            // Resolve destination layout once, then invert each glyph/button's displacement.
            body.UpdateLayout();
            var transition=new Storyboard();
            void Move(DependencyObject target,string property,double from,double to){
                var motion=new DoubleAnimation{From=from,To=to,Duration=new Duration(TimeSpan.FromMilliseconds(compactList?190:240)),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
                Storyboard.SetTarget(motion,target);Storyboard.SetTargetProperty(motion,property);transition.Children.Add(motion);
            }
            foreach(var element in movers){
                var destination=element.TransformToVisual(body).TransformPoint(new Windows.Foundation.Point());
                var origin=positions![element];
                var transform=new CompositeTransform();element.RenderTransform=transform;
                Move(transform,"TranslateX",origin.X-destination.X,0);
                Move(transform,"TranslateY",origin.Y-destination.Y,0);
                if(element is FontIcon icon){
                    Move(transform,"ScaleX",sizes![icon]/icon.FontSize,1);
                    Move(transform,"ScaleY",sizes![icon]/icon.FontSize,1);
                }
            }
            // A short, user-triggered layout animation keeps the detail's right edge fixed.
            var width=new DoubleAnimation{From=currentWidth,To=targetWidth,
                Duration=new Duration(TimeSpan.FromMilliseconds(compactList?190:240)),
                EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},EnableDependentAnimation=true};
            Storyboard.SetTarget(width,paneSlot);Storyboard.SetTargetProperty(width,"Width");transition.Children.Add(width);
            if(!compactList){
                void Reveal(DependencyObject target){
                    var fade=new DoubleAnimationUsingKeyFrames();
                    fade.KeyFrames.Add(new DiscreteDoubleKeyFrame{Value=0,KeyTime=KeyTime.FromTimeSpan(TimeSpan.Zero)});
                    fade.KeyFrames.Add(new DiscreteDoubleKeyFrame{Value=0,KeyTime=KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))});
                    fade.KeyFrames.Add(new EasingDoubleKeyFrame{Value=1,KeyTime=KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
                    Storyboard.SetTarget(fade,target);Storyboard.SetTargetProperty(fade,"Opacity");transition.Children.Add(fade);
                }
                Reveal(paneTitle);
                Reveal(settingsLabel);
                foreach(var row in deviceItems.Values)if(row.Content is Grid content)Reveal(content.Children[1]);
            }
            paneAnimation=transition;transition.Begin();
        }
        UpdatePane();
        void UpdateLanguage(){
            root.Language=L.Language=="zh"?"zh-CN":"en-US";
            paneTitle.Text=L.T("Devices");status.Text=L.T("Loading…");deviceError.Title=L.T("Reading unavailable");
            AutomationProperties.SetName(devices,L.T("Connected devices"));
            AutomationProperties.SetName(refresh,L.T("Refresh devices"));ToolTipService.SetToolTip(refresh,L.T("Refresh devices"));
            settingsLabel.Text=L.T("Settings");AutomationProperties.SetName(settingsItem,L.T("Settings"));ToolTipService.SetToolTip(settingsItem,L.T("Settings"));
            paneAnimation?.Stop();switchAnimation?.Stop();
            foreach(var (id,row) in deviceItems){row.Content=DeviceVisuals.ListRow(models[id]);UpdateRowLayout(row);}
            renderedDevice=null;emptyRendered=false;Render();
        }
        UpdateLanguage();
        FrameworkElement? settingsPage=null;
        void BuildSettings(){
            settingsPage=SettingsPage.Create(settings,async next=>{
                next.KnownDevices=new(settings.KnownDevices);
                next.Save();settings=next;L.Language=settings.Language;
                applying=true;
                try{foreach(var id in deviceItems.Keys.Where(settings.ExcludedDevices.Contains).ToArray()){devices.Items.Remove(deviceItems[id]);deviceItems.Remove(id);models.Remove(id);}}
                finally{applying=false;}
                if(selected!=null&&!models.ContainsKey(selected))selected=models.Keys.FirstOrDefault();
                previous="";UpdateLanguage();
                await SendCommand(JsonSerializer.Serialize(new{type="settings",preferences=settings.PollPreferences()}));
                BuildSettings();
            },BuildSettings);
            if(settingsVisible)scroll.Content=settingsPage;
        }
        settingsNavigation.SelectionChanged+=(_,_)=>{
            if(settingsNavigation.SelectedItem==null)return;
            settingsVisible=true;
            applying=true;try{devices.SelectedItem=null;}finally{applying=false;}
            if(settingsPage==null)BuildSettings();
            scroll.Content=settingsPage;
            scroll.ChangeView(null,0,null,true);
        };
        titleBar.PaneToggleRequested+=(_,_)=>{
            compactList=!compactList;UpdatePane(animate:true);
            preferences=preferences with{CompactList=compactList};QueueSave();
        };
        Content=new ContentControl{Content=root,FontFamily=new FontFamily("Microsoft YaHei UI"),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        SetTitleBar(titleBar);
        clock.Tick += (_,_) => {if(updated!=null&&selected!=null&&models.TryGetValue(selected,out var current))DeviceVisuals.UpdateTime(updated,current);};clock.Start();
        devices.SelectionChanged += (_,_) => { if(!applying&&devices.SelectedItem is ListViewItem item && item.Tag is string id){bool changed=selected!=id||settingsVisible;settingsVisible=false;settingsNavigation.SelectedItem=null;scroll.Content=detail;selected=id;Render();if(changed)AnimateSelection();} };
        refresh.Click += async (_,_) => {
            try { if(core is { HasExited:false }) { refresh.IsEnabled=false;await SendCommand("refresh");await Task.Delay(2000);refresh.IsEnabled=true; } }
            catch(Exception e){ ShowError(e.Message); }
        };
        saveTimer.Tick+=(_,_)=>{saveTimer.Stop();preferences.Save(WindowPreferences.FilePath);};
        AppWindow.Changed+=(_,args)=>{if(args.DidPositionChange||args.DidSizeChange||args.DidPresenterChange){CaptureWindow();QueueSave();}};
        if(preferences.Maximized&&AppWindow.Presenter is OverlappedPresenter presenter)presenter.Maximize();
        Closed += (_,_) => {clock.Stop();saveTimer.Stop();switchAnimation?.Stop();paneAnimation?.Stop();preferences.Save(WindowPreferences.FilePath);try{if(core is { HasExited:false })core.Kill(entireProcessTree:true);}catch{}core?.Dispose();};
        _=StartCore();
    }
    private async Task SendCommand(string command){
        await commandLock.WaitAsync();
        try{if(core is {HasExited:false}){await core.StandardInput.WriteLineAsync(command);await core.StandardInput.FlushAsync();}}
        finally{commandLock.Release();}
    }
    private static void RoundTitleIcon(DependencyObject root){
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is FrameworkElement element&&element.Name=="PART_Icon"){
                var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
                var shape=visual.Compositor.CreateRoundedRectangleGeometry();
                void Resize(){shape.Size=new System.Numerics.Vector2((float)element.ActualWidth,(float)element.ActualHeight);shape.CornerRadius=new System.Numerics.Vector2((float)Math.Min(element.ActualWidth,element.ActualHeight)*.25f);}
                Resize();element.SizeChanged+=(_,_)=>Resize();
                visual.Clip=visual.Compositor.CreateGeometricClip(shape);
                return;
            }
            RoundTitleIcon(child);
        }
    }
    private void RestoreWindow(){
        var requested=new RectInt32(preferences.X??0,preferences.Y??0,Math.Clamp(preferences.Width,640,20000),Math.Clamp(preferences.Height,420,20000));
        var area=(preferences.X.HasValue&&preferences.Y.HasValue?DisplayArea.GetFromRect(requested,DisplayAreaFallback.Nearest):DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary)).WorkArea;
        preferences=preferences.Fit(area.X,area.Y,area.Width,area.Height);
        AppWindow.MoveAndResize(new RectInt32(preferences.X!.Value,preferences.Y!.Value,preferences.Width,preferences.Height));
    }
    private void CaptureWindow(){
        if(AppWindow.Presenter is not OverlappedPresenter presenter||presenter.State==OverlappedPresenterState.Minimized)return;
        bool maximized=presenter.State==OverlappedPresenterState.Maximized;
        preferences=preferences with{Maximized=maximized};
        if(!maximized&&AppWindow.Size.Width>0&&AppWindow.Size.Height>0)
            preferences=preferences with{X=AppWindow.Position.X,Y=AppWindow.Position.Y,Width=AppWindow.Size.Width,Height=AppWindow.Size.Height};
    }
    private void QueueSave(){saveTimer.Stop();saveTimer.Start();}
    private void AnimateSelection(){
        switchAnimation?.Stop();
        if(!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)return;
        var transform=new TranslateTransform();detail.RenderTransform=transform;
        var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        var fade=new DoubleAnimation{From=.85,To=1,Duration=new Duration(TimeSpan.FromMilliseconds(160)),EasingFunction=ease};
        var slide=new DoubleAnimation{From=6,To=0,Duration=new Duration(TimeSpan.FromMilliseconds(180)),EasingFunction=ease};
        Storyboard.SetTarget(fade,detail);Storyboard.SetTargetProperty(fade,"Opacity");
        Storyboard.SetTarget(slide,transform);Storyboard.SetTargetProperty(slide,"Y");
        switchAnimation=new Storyboard();switchAnimation.Children.Add(fade);switchAnimation.Children.Add(slide);switchAnimation.Begin();
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
            info.Environment["SHOWDEVICE_POLL_PREFERENCES"]=JsonSerializer.Serialize(settings.PollPreferences());
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
        error.IsOpen=diagnostics.Length>0;error.Title="";error.Message=string.Join("\n",diagnostics.Select(ReadingNotice.Brief).Distinct());ToolTipService.SetToolTip(error,string.Join("\n",diagnostics));
        applying=true;
        try{
            var incoming=data.EnumerateArray().Where(item=>!settings.ExcludedDevices.Contains(item.GetProperty("device").GetProperty("id").GetString()!)).ToArray();
            bool knownChanged=false;
            foreach(var item in incoming){
                string id=item.GetProperty("device").GetProperty("id").GetString()!,name=item.GetProperty("device").GetProperty("name").GetString()!;
                if(settings.KnownDevices.Count<256&&settings.KnownDevices.GetValueOrDefault(id)!=name){settings.KnownDevices[id]=name;knownChanged=true;}
            }
            if(knownChanged){try{settings.Save();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){ShowError(e.Message);}}
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
                UpdateRowLayout(row);
                string name=item.GetProperty("device").GetProperty("name").GetString()!;
                AutomationProperties.SetName(row,name);ToolTipService.SetToolTip(row,name);
                models[id]=item.Clone();
            }
            if(selected==null||!models.ContainsKey(selected))selected=models.Keys.FirstOrDefault();
            var target=!settingsVisible&&selected!=null?deviceItems[selected]:null;
            if(!ReferenceEquals(devices.SelectedItem,target))devices.SelectedItem=target;
        }finally{applying=false;}
        status.Visibility=Visibility.Collapsed;
        Render();
    }
    private void UpdateRowLayout(ListViewItem row){
        row.MinWidth=0;
        row.Width=compactList?48:double.NaN;
        row.Padding=compactList?new Thickness(4,0,4,0):new Thickness(12,0,12,0);
        row.HorizontalAlignment=compactList?HorizontalAlignment.Center:HorizontalAlignment.Stretch;
        DeviceVisuals.SetListRowCompact((FrameworkElement)row.Content,compactList);
    }
    private void Render(){
        if(selected==null||!models.TryGetValue(selected,out var device)){
            if(emptyRendered)return;
            updated=null;renderedDevice=null;detail.Children.Clear();readings.Children.Clear();emptyRendered=true;
            detail.Children.Add(new TextBlock{Text=L.T("Connect a supported device"),FontSize=22});
            detail.Children.Add(new TextBlock{Text=L.T("Connect a receiver or USB cable, then refresh."),TextWrapping=TextWrapping.Wrap});
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
        var rawIssue=deviceError.IsOpen?issue.GetString():null;
        deviceError.Title="";deviceError.Message=deviceError.IsOpen?ReadingNotice.Brief(rawIssue):null;
        deviceError.Severity=ReadingNotice.MayBeSleeping(rawIssue)?InfoBarSeverity.Informational:InfoBarSeverity.Warning;
        ToolTipService.SetToolTip(deviceError,rawIssue);
        renderedDevice=device;emptyRendered=false;
    }
    private void ShowError(string message){error.IsOpen=true;error.Title="";error.Message=ReadingNotice.Brief(message);ToolTipService.SetToolTip(error,message);}
}



