using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;
namespace ShowDevice;

internal sealed class CompactSurface : Window {
    private readonly AppSession session;
    private readonly Grid rows=new(){RowSpacing=2,ColumnSpacing=10};
    private readonly Dictionary<string,SurfaceDeviceCard> items=new();
    private readonly TextBlock empty=new(){Text=L.T("Connect a supported device"),Margin=new(8,14,8,14),TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=1,Value=0,Height=2,Opacity=0,Margin=new(4,0,4,8),IsHitTestVisible=false};
    private readonly DispatcherTimer pulse=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private readonly Stopwatch pulseTime=new();
    private readonly bool desktop;
    private readonly Grid root=new(){Padding=new(12),RowSpacing=0};
    private readonly BackgroundLayer background;
    private readonly ThemeResources themeResources;
    private string appearanceSignature="";
    private bool exposed,closing;
    private nint host;
    private Windows.Foundation.Point? drag;
    private NativeShell.Rect dragBounds;
    private bool dragging;
    private long suppressOpenUntil;
    private NativeShell.Rect popupAnchor;
    private bool capturePending;
    public nint Handle {get;}
    public bool Exposed=>exposed;
    public bool Attached=>host!=0&&NativeShell.IsWindow(host)&&NativeShell.GetParent(Handle)==host;
    public event Action<string?>? OpenDevice;
    public event Action? Hidden;
    public event Action? Moved;
    public IEnumerable<string> DeviceIds=>items.Keys;
    public CompactSurface(AppSession session,bool desktop){
        this.session=session;this.desktop=desktop;
        Title=desktop?"ShowDevice · Desktop":"ShowDevice · Devices";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","showdevice-icon.ico"));
        Handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;AppWindow.SetPresenter(presenter);
        // Opt-in development mode exposes the popup to native UI automation; normal runs stay tool windows.
        bool inspect=!desktop&&Environment.GetEnvironmentVariable("SHOWDEVICE_SURFACE_INSPECTION")=="1";
        AppWindow.IsShownInSwitchers=inspect;if(!inspect)NativeShell.ToolWindow(Handle);
        SystemBackdrop=new TransparentSurfaceBackdrop(Handle);
        int noBorder=unchecked((int)0xFFFFFFFE);NativeShell.DwmSetWindowAttribute(Handle,34,ref noBorder,4);
        // Rounded edges are alpha-composited by BackgroundLayer. An HRGN hard-clips them to whole pixels.
        int squareNativeFrame=1;NativeShell.DwmSetWindowAttribute(Handle,33,ref squareNativeFrame,4);
        root.CornerRadius=new(12);
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new Grid{ColumnSpacing=8,MinHeight=28};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var title=new TextBlock{Text=L.T("Devices"),FontSize=15,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(title);
        var refresh=SmallButton("\uE72C",L.T("Refresh devices"));header.Children.Add(refresh);Grid.SetColumn(refresh,1);refresh.Click+=async(_,_)=>{Pulse();await session.Send("refresh");};
        var open=SmallButton("\uE8A7",L.T("Open ShowDevice"));header.Children.Add(open);Grid.SetColumn(open,2);open.Click+=(_,_)=>OpenDevice?.Invoke(null);
        if(!desktop){root.Children.Add(header);progress.Margin=new(4,4,4,8);}root.Children.Add(progress);Grid.SetRow(progress,1);
        var scroller=new ScrollViewer{Content=rows,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroller);Grid.SetRow(scroller,2);
        background=new BackgroundLayer(root);
        themeResources=new ThemeResources(background);UpdateTheme();session.ThemeChanged+=UpdateTheme;
        Content=new ContentControl{Content=background,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),FontFamily=new FontFamily("Microsoft YaHei UI"),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        root.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Escape&&!desktop)Hide();};
        root.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        root.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e)=>{
            if(session.Settings.Surfaces.Locked||InteractiveSource(e.OriginalSource as DependencyObject))return;
            if(!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed)return;
            NativeShell.GetWindowRect(Handle,out dragBounds);
            // The press may be queued while the cursor has already moved; use its screen position.
            uint position=NativeShell.GetMessagePos();
            dragging=false;drag=new(unchecked((short)(position&0xffff)),unchecked((short)(position>>16)));
            // Let a short press reach the device button; capture only after the drag threshold.
        }),true);
        root.AddHandler(UIElement.PointerMovedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e)=>MoveDrag(e)),true);
        root.AddHandler(UIElement.PointerReleasedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e)=>{
            if(drag==null)return;MoveDrag(e);bool moved=dragging;FinishDrag();root.ReleasePointerCapture(e.Pointer);if(moved)e.Handled=true;
        }),true);
        root.PointerCaptureLost+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,root))FinishDrag();};
        Activated+=(_,e)=>{if(!desktop&&e.WindowActivationState==WindowActivationState.Deactivated)Hide();};
        Closed+=(_,_)=>session.ThemeChanged-=UpdateTheme;
        AppWindow.Closing+=(_,e)=>{if(!closing){e.Cancel=true;Hide();}};
        pulse.Tick+=(_,_)=>{double t=pulseTime.Elapsed.TotalMilliseconds/600;progress.Value=Math.Min(1,t*2);progress.Opacity=Math.Max(0,1-t);if(t>=1){pulse.Stop();progress.Opacity=0;}};
        AppWindow.Resize(new SizeInt32((int)session.Settings.Surfaces.Width,160));
    }
    private static Button SmallButton(string glyph,string name){var b=new Button{Content=new FontIcon{Glyph=glyph,FontFamily=new("Segoe Fluent Icons"),FontSize=14},Width=28,Height=28,MinWidth=0,MinHeight=0,Padding=new(0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),BorderThickness=new(0)};ToolTipService.SetToolTip(b,name);AutomationProperties.SetName(b,name);return b;}
    public void SetExposed(bool visible){if(exposed==visible)return;exposed=visible;foreach(var item in items.Values)item.Battery.SetActive(visible);if(!visible){pulse.Stop();progress.Opacity=0;}}
    private void Pulse(){if(!exposed)return;if(!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)return;pulseTime.Restart();pulse.Start();}
    public void Update(){
        UpdateTheme();
        if(session.Latest is not { } snapshot)return;
        var prefs=session.Settings;
        var appearance=desktop?prefs.Personalization.Desktop:prefs.Personalization.Tray;
        string nextAppearance=L.Language+JsonSerializer.Serialize(appearance);
        if(appearanceSignature!=nextAppearance){foreach(var item in items.Values)item.Battery.SetActive(false);items.Clear();rows.Children.Clear();appearanceSignature=nextAppearance;}
        background.Apply(appearance.Background);rows.RowSpacing=appearance.Layout=="cards"?10:2;
        var incoming=SurfacePolicy.Ordered(snapshot.GetProperty("devices").EnumerateArray().Where(d=>!prefs.ExcludedDevices.Contains(d.GetProperty("device").GetProperty("id").GetString()!)&&SurfacePolicy.Visible(prefs.Surfaces,desktop,d.GetProperty("device").GetProperty("id").GetString()!)),desktop?prefs.Surfaces.DesktopOrder:prefs.Surfaces.TrayOrder,d=>d.GetProperty("device").GetProperty("id").GetString()!,d=>d.GetProperty("device").GetProperty("name").GetString()!).ToArray();
        empty.Text=L.T(!(desktop?prefs.Surfaces.AllDevices:prefs.Surfaces.TrayAllDevices)&&(desktop?prefs.Surfaces.Devices:prefs.Surfaces.TrayDevices).Count==0?"No devices selected":"Connect a supported device");
        var ids=incoming.Select(d=>d.GetProperty("device").GetProperty("id").GetString()!).ToHashSet();
        foreach(var id in items.Keys.Where(id=>!ids.Contains(id)).ToArray()){items[id].Battery.SetActive(false);rows.Children.Remove(items[id].View);items.Remove(id);}
        bool changed=false;
        foreach(var device in incoming){
            string id=device.GetProperty("device").GetProperty("id").GetString()!;
            if(!items.TryGetValue(id,out var item)){
                item=new SurfaceDeviceCard(device,appearance,desktop?"desktop":"tray",id=>{if(Environment.TickCount64>=suppressOpenUntil)OpenDevice?.Invoke(id);});
                item.Battery.SetActive(exposed);items[id]=item;rows.Children.Add(item.View);
            }
            changed|=item.Update(device,prefs);
        }
        if(items.Count==0&&!rows.Children.Contains(empty))rows.Children.Add(empty);else if(items.Count>0)rows.Children.Remove(empty);
        int columns=appearance.Layout=="cards"?Math.Max(2,(int)((prefs.Surfaces.Width-24)/130)):1;
        if(rows.ColumnDefinitions.Count!=columns){rows.ColumnDefinitions.Clear();for(int i=0;i<columns;i++)rows.ColumnDefinitions.Add(new());}
        int rowCount=Math.Max(1,(int)Math.Ceiling((double)items.Count/columns));
        if(rows.RowDefinitions.Count!=rowCount){rows.RowDefinitions.Clear();for(int i=0;i<rowCount;i++)rows.RowDefinitions.Add(new(){Height=GridLength.Auto});}
        double side=(prefs.Surfaces.Width-24-(columns-1)*10)/columns;int index=0;
        foreach(var device in incoming){
            var item=items[device.GetProperty("device").GetProperty("id").GetString()!];
            int oldIndex=rows.Children.IndexOf(item.View);if(oldIndex!=index){rows.Children.RemoveAt(oldIndex);rows.Children.Insert(index,item.View);}
            Grid.SetRow(item.View,index/columns);Grid.SetColumn(item.View,index%columns);item.View.Height=appearance.Layout=="cards"?side:double.NaN;item.View.Width=appearance.Layout=="cards"?side:double.NaN;index++;
        }
        Grid.SetColumnSpan(empty,columns);
        double scale=Math.Max(1,NativeShell.GetDpiForWindow(Handle)/96d);
        // Measure the actual padding, progress row and native header instead of estimated offsets.
        background.Measure(new Windows.Foundation.Size(prefs.Surfaces.Width,double.PositiveInfinity));
        int width=(int)Math.Ceiling(prefs.Surfaces.Width*scale),height=(int)Math.Ceiling(Math.Clamp(background.DesiredSize.Height,desktop?64:100,600)*scale);
        NativeShell.GetWindowRect(Handle,out var bounds);
        if(bounds.Right-bounds.Left!=width||bounds.Bottom-bounds.Top!=height){
            NativeShell.SetWindowPos(Handle,0,0,0,width,height,0x16);
            if(!desktop&&exposed&&drag==null)PositionPopup();
        }
        if(changed){Pulse();if(exposed&&!capturePending){capturePending=true;_=Capture();}}
    }
    private async Task Capture(){try{await SurfaceDiagnostics.Capture(background,desktop?"desktop":"tray");}finally{capturePending=false;}}
    private void UpdateTheme(){
        var style=desktop?session.Settings.Personalization.Desktop:session.Settings.Personalization.Tray;
        background.RequestedTheme=session.ResolveTheme(style.Theme);
        if(Content is FrameworkElement content)content.RequestedTheme=background.RequestedTheme;
        int dark=background.RequestedTheme==ElementTheme.Dark?1:0;NativeShell.DwmSetWindowAttribute(Handle,20,ref dark,4);
    }
    public void Hide(){NativeShell.ShowWindow(Handle,0);SetExposed(false);Hidden?.Invoke();}
    public void ShowPopup(NativeShell.Rect anchor){
        popupAnchor=anchor;Update();PositionPopup();Activate();NativeShell.SetForegroundWindow(Handle);SetExposed(true);Update();
    }
    private void MoveDrag(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e){
        if(drag is not {} origin)return;
        if(session.Settings.Surfaces.Locked){drag=null;dragging=false;root.ReleasePointerCaptures();return;}
        // Physical screen coordinates never depend on the window that we are moving.
        if(!NativeShell.GetCursorPos(out var point))return;
        double scale=NativeShell.GetDpiForWindow(Handle)/96d;
        int dx=point.X-(int)origin.X,dy=point.Y-(int)origin.Y;
        if(!dragging&&Math.Abs(dx)+Math.Abs(dy)<4*scale)return;
        if(!dragging){
            if(!root.CapturePointer(e.Pointer)){drag=null;return;}
            dragging=true;
        }
        suppressOpenUntil=Environment.TickCount64+350;
        var target=SurfacePolicy.DragPosition(dragBounds.Left,dragBounds.Top,(int)origin.X,(int)origin.Y,point.X,point.Y);
        var fitted=FitPopup(target.X,target.Y);
        if(desktop)Place(fitted.X,fitted.Y);else NativeShell.SetWindowPos(Handle,0,fitted.X,fitted.Y,0,0,0x15);
        e.Handled=true;
    }
    private bool InteractiveSource(DependencyObject? source){
        for(var node=source;node!=null;node=VisualTreeHelper.GetParent(node)){
            if(items.Values.Any(item=>ReferenceEquals(item.DragHandle,node)))return false;
            if(node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or Microsoft.UI.Xaml.Controls.Primitives.RangeBase or TextBox or ComboBox or ToggleSwitch or NumberBox)return true;
        }
        return false;
    }
    private void FinishDrag(){if(drag==null)return;drag=null;if(dragging){dragging=false;suppressOpenUntil=Environment.TickCount64+350;SavePosition();}}
    private PointInt32 FitPopup(int x,int y){
        NativeShell.GetWindowRect(Handle,out var bounds);
        var area=DisplayArea.GetFromRect(new RectInt32(x,y,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top),DisplayAreaFallback.Nearest).WorkArea;
        var point=SurfacePolicy.FitPosition(x,y,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,new(area.X,area.Y,area.X+area.Width,area.Y+area.Height));
        return new(point.X,point.Y);
    }
    private void PositionPopup(){
        var prefs=session.Settings.Surfaces;
        if(prefs.TrayX is {} savedX&&prefs.TrayY is {} savedY){AppWindow.Move(FitPopup(savedX,savedY));return;}
        var anchor=popupAnchor;var area=DisplayArea.GetFromRect(new RectInt32(anchor.Left,anchor.Top,1,1),DisplayAreaFallback.Nearest).WorkArea;
        NativeShell.GetWindowRect(Handle,out var r);int width=r.Right-r.Left,height=r.Bottom-r.Top;
        int x=Math.Clamp(anchor.Right-width,area.X,Math.Max(area.X,area.X+area.Width-width));
        int y=anchor.Top-height-10;if(y<area.Y)y=anchor.Bottom+10;y=Math.Clamp(y,area.Y,Math.Max(area.Y,area.Y+area.Height-height));
        AppWindow.Move(new PointInt32(x,y));
    }
    public bool AttachDesktop(){
        if(!Attached){
            Hide();host=NativeShell.DesktopHost();if(host==0)return false;
            var style=NativeShell.GetWindowLongPtrW(Handle,-16).ToInt64();NativeShell.SetWindowLongPtrW(Handle,-16,(nint)((style&~0x80000000L)|0x40000000));
            NativeShell.SetParent(Handle,host);if(!Attached){host=0;return false;}
            // Above the desktop icon-list sibling, never above normal top-level applications.
            NativeShell.SetWindowPos(Handle,0,0,0,0,0,0x13);
        }
        var prefs=session.Settings.Surfaces;
        var area=DisplayArea.GetFromRect(new RectInt32(prefs.X??0,prefs.Y??0,1,1),DisplayAreaFallback.Nearest).WorkArea;
        Update();NativeShell.GetWindowRect(Handle,out var rect);int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        Place(Math.Clamp(prefs.X??area.X+area.Width-width-24,area.X,Math.Max(area.X,area.X+area.Width-width)),Math.Clamp(prefs.Y??area.Y+24,area.Y,Math.Max(area.Y,area.Y+area.Height-height)));
        NativeShell.ShowWindow(Handle,4);return true;
    }
    private void Place(int x,int y){if(!Attached)return;var pt=new NativeShell.Point{X=x,Y=y};NativeShell.ScreenToClient(host,ref pt);NativeShell.SetWindowPos(Handle,0,pt.X,pt.Y,0,0,0x15);}
    private void SavePosition(){
        NativeShell.GetWindowRect(Handle,out var r);
        if(desktop){session.Settings.Surfaces.X=r.Left;session.Settings.Surfaces.Y=r.Top;}
        else{session.Settings.Surfaces.TrayX=r.Left;session.Settings.Surfaces.TrayY=r.Top;}
        session.Settings.Save();Moved?.Invoke();
    }
    public void Shutdown(){closing=true;session.ThemeChanged-=UpdateTheme;pulse.Stop();foreach(var item in items.Values)item.Battery.SetActive(false);Close();}
}
