using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
namespace ShowDevice;
internal static class DeviceVisuals {
    internal const double DetailInset=24;
    private const double HeaderHeight=180;
    private const double ArtworkScale=.7;
    public static Brush Resource(string key) => ThemeResources.Resource(key);
    public static string Kind(JsonElement device) => device.TryGetProperty("device_kind",out var k)?k.GetString()??"other":"other";
    public static string TypeName(JsonElement device) => L.T(Kind(device) switch {"keyboard"=>"Keyboard","mouse"=>"Mouse","headset"=>"Headset","gamepad"=>"Gamepad",_=>"Device"});
    public static string Glyph(string kind) => kind switch {"keyboard"=>"\uE765","mouse"=>"\uE962","headset"=>"\uE7F6","gamepad"=>"\uE7FC","power"=>"\uE83E","connection"=>"\uE839","lighting"=>"\uE781","settings"=>"\uE713",_=>"\uE772"};
    public static FontIcon Icon(string kind,double size=22) => new(){Glyph=Glyph(kind),FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=size,IsHitTestVisible=false};
    public static Brush Accent(string kind) {
        if(new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)return Resource("TextFillColorPrimaryBrush");
        return ThemeResources.Adaptive(
            kind switch {"keyboard"=>Color.FromArgb(255,117,83,189),"mouse"=>Color.FromArgb(255,20,135,107),"headset"=>Color.FromArgb(255,56,115,190),_=>Color.FromArgb(255,100,116,139)},
            kind switch {"keyboard"=>Color.FromArgb(255,179,148,255),"mouse"=>Color.FromArgb(255,73,217,172),"headset"=>Color.FromArgb(255,116,181,255),_=>Color.FromArgb(255,182,200,221)});
    }
    public static bool Unsupported(JsonElement state,string? key) => key!=null && state.TryGetProperty(key,out var r) && r.GetProperty("availability").GetString()=="unsupported";
    private static FrameworkElement ChargingMark(bool compact,Brush brush){
        return new ChargingGlyph(compact?7:11,compact?9:14){Fill=brush};
    }
    public static FrameworkElement? Battery(JsonElement state,bool compact=false,string? error=null) {
        if(!state.TryGetProperty("battery.percent",out var r)||Unsupported(state,"battery.percent"))return null;
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=compact?5:10,VerticalAlignment=VerticalAlignment.Center};
        var availability=r.GetProperty("availability").GetString();
        bool numeric=r.TryGetProperty("value",out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double _);
        double percent=numeric?Math.Clamp(v.GetDouble(),0,100):0;
        bool fresh=availability=="available";
        bool failed=availability!="offline"&&ReadingNotice.IsParseFailure(error);
        bool uncertain=!failed&&(ReadingNotice.UncertainBattery(availability)||(!numeric&&availability!="offline"));
        bool isCharging=ReadingNotice.IsCharging(state);
        var brush=Resource("TextFillColorSecondaryBrush");
        if(fresh&&!failed&&!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            brush=ThemeResources.Adaptive(BackgroundLayer.Color(AppearanceRules.BatteryColor(new(),percent)),BackgroundLayer.Color(AppearanceRules.BatteryColor(new(),percent,true)));
        double width=compact?22:36, height=compact?11:18, inset=compact?2:3, terminal=compact?2:3;
        var battery=new Grid{Width=width,Height=failed?(compact?14:22):height,VerticalAlignment=VerticalAlignment.Center};
        battery.Children.Add(new Border{Width=width-terminal-1,Height=height,BorderThickness=new Thickness(compact?1:1.5),BorderBrush=brush,CornerRadius=new CornerRadius(compact?2:3),HorizontalAlignment=HorizontalAlignment.Left});
        battery.Children.Add(new Border{Width=terminal,Height=compact?5:7,Background=brush,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,CornerRadius=new CornerRadius(1)});
        if(numeric&&availability!="offline"&&!uncertain&&!failed&&!isCharging)battery.Children.Add(new Border{Width=(width-terminal-1-inset*2)*percent/100,Height=height-inset*2,Background=brush,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(inset,0,0,0),CornerRadius=new CornerRadius(1)});
        if(uncertain)battery.Children.Add(new TextBlock{Text="?",FontFamily=new FontFamily("Segoe UI"),FontSize=compact?10:14,FontWeight=Microsoft.UI.Text.FontWeights.Bold,Foreground=brush,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,compact?-2:-1,terminal+1,0)});
        if(isCharging&&!uncertain&&!failed){var bolt=ChargingMark(compact,brush);bolt.Margin=new Thickness(0,0,terminal+1,0);battery.Children.Add(bolt);}
        if(failed){
            battery.Children.Clear();
            battery.Children.Add(new FontIcon{Glyph="\uE7BA",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=compact?14:22,Foreground=Resource("SystemFillColorCautionBrush"),IsHitTestVisible=false});
        }
        row.Children.Add(battery);
        if(isCharging&&(uncertain||failed))row.Children.Add(ChargingMark(compact,brush));
        string text=availability=="offline"?L.T("Offline"):!numeric?"—":$"{percent:0}%";
        row.Children.Add(new TextBlock{Text=text,Foreground=fresh&&!failed?Resource("TextFillColorPrimaryBrush"):brush,FontSize=compact?14:24,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        if(!compact&&isCharging)
            row.Children.Add(new TextBlock{Text=L.T("Charging"),Foreground=brush,VerticalAlignment=VerticalAlignment.Center});
        string hint=failed?ReadingNotice.Brief(error):uncertain||availability=="stale"?ReadingNotice.BatteryHint(availability):L.T("Battery")+": "+text;
        if(isCharging)hint+=" · "+L.T("Charging");
        ToolTipService.SetToolTip(row,hint);
        AutomationProperties.SetName(row,uncertain?L.T("Battery")+": "+text+". "+hint:hint);
        return row;
    }
    public static FrameworkElement ListRow(JsonElement device) {
        var grid=new Grid{Padding=new Thickness(6,12,6,12),ColumnSpacing=14};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon=Icon(Kind(device),26);icon.Foreground=Accent(Kind(device));grid.Children.Add(icon);
        var labels=new StackPanel{Spacing=6};Grid.SetColumn(labels,1);grid.Children.Add(labels);
        labels.Children.Add(new TextBlock{Text=device.GetProperty("device").GetProperty("name").GetString(),TextWrapping=TextWrapping.Wrap,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        var line=new Grid{ColumnSpacing=12};
        line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        line.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        line.Children.Add(new TextBlock{Text=TypeName(device),Foreground=Resource("TextFillColorSecondaryBrush"),VerticalAlignment=VerticalAlignment.Center});
        var battery=Battery(device.GetProperty("state"),true,ReadingNotice.Error(device));
        if(battery!=null){Grid.SetColumn(battery,1);battery.HorizontalAlignment=HorizontalAlignment.Right;line.Children.Add(battery);}
        labels.Children.Add(line);return grid;
    }
    public static void SetListRowCompact(FrameworkElement row,bool compact){
        if(row is not Grid grid)return;
        ((FontIcon)grid.Children[0]).FontSize=compact?18:26;
        grid.Padding=compact?new Thickness(0,12,0,12):new Thickness(6,12,6,12);
        grid.ColumnSpacing=compact?0:14;
        grid.ColumnDefinitions[0].Width=compact?new GridLength(1,GridUnitType.Star):GridLength.Auto;
        grid.ColumnDefinitions[1].Width=compact?new GridLength(0):new GridLength(1,GridUnitType.Star);
        ((FrameworkElement)grid.Children[1]).Visibility=compact?Visibility.Collapsed:Visibility.Visible;
    }
    public static FrameworkElement Header(JsonElement device,TextBlock updated,bool customBackground=false) {
        var grid=new Grid{Height=HeaderHeight};
        var header=new Border{Child=grid,CornerRadius=new CornerRadius(12)};
        Color SurfaceColor()=>grid.ActualTheme==ElementTheme.Dark?Color.FromArgb(255,39,39,39):Color.FromArgb(255,250,250,250);
        void UpdateSurface(){
            header.Background=customBackground?new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                :new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast?Resource("LayerFillColorDefaultBrush")
                :new SolidColorBrush(SurfaceColor());
        }
        grid.ActualThemeChanged+=(_,_)=>UpdateSurface();grid.Loaded+=(_,_)=>UpdateSurface();UpdateSurface();
        // A clipped canvas never contributes the oversized image's desired size to layout.
        var artwork=new Grid{Margin=new Thickness(0,0,124,0),IsHitTestVisible=false};grid.Children.Add(artwork);
        artwork.SizeChanged+=(_,_)=>artwork.Clip=new RectangleGeometry{Rect=new Windows.Foundation.Rect(0,0,artwork.ActualWidth,artwork.ActualHeight)};
        var art=Icon(Kind(device),72);art.Foreground=Accent(Kind(device));
        art.HorizontalAlignment=HorizontalAlignment.Right;art.VerticalAlignment=VerticalAlignment.Center;art.Margin=new Thickness(0,0,32,0);grid.Children.Add(art);
        AutomationProperties.SetAccessibilityView(art,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        if(!customBackground&&device.TryGetProperty("product_image",out var resource)&&resource.ValueKind==JsonValueKind.String){
            try{
                var path=resource.GetString()!;
                if(path.StartsWith(@"\\?\"))path=path[4..];
                var uri=new Uri(path,UriKind.Absolute);
                if(uri.IsFile&&!uri.IsUnc&&File.Exists(path)){
                    var bitmap=new BitmapImage{DecodePixelWidth=960};
                    // These crops target the identifying parts of the bundled model photos,
                    // not their transparent canvas centers. Dimensions stay independent of header height.
                    var crop=device.GetProperty("plugin").GetString() switch {
                        "org.showdevice.hyperx" => (Width:300d,Left:0d,Top:-75d), // earcup, logo and microphone
                        "org.showdevice.razer" => (Width:220d,Left:24d,Top:-65d), // shell and palm logo
                        "org.showdevice.nuphy" => (Width:360d,Left:0d,Top:-70d), // key field and colored spacebar
                        "org.showdevice.sony.dualsense" => (Width:320d,Left:12d,Top:-35d), // white shells, sticks and face buttons
                        _ => (Width:320d,Left:0d,Top:-70d)
                    };
                    var picture=new Image{Width=crop.Width*ArtworkScale,Stretch=Stretch.Uniform,Opacity=.9,Visibility=Visibility.Collapsed};
                    AutomationProperties.SetAccessibilityView(picture,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                    picture.ImageOpened+=(_,_)=>{
                        if(bitmap.PixelWidth<=0||bitmap.PixelHeight<=0)return;
                        picture.Height=picture.Width*bitmap.PixelHeight/bitmap.PixelWidth;
                        picture.Visibility=Visibility.Visible;
                    };
                    picture.ImageFailed+=(_,_)=>picture.Visibility=Visibility.Collapsed;
                    // Scale around the lower-left of the fixed header, keeping the image below the title.
                    var imageLayer=new Canvas();Canvas.SetLeft(picture,crop.Left*ArtworkScale);Canvas.SetTop(picture,HeaderHeight+(crop.Top-HeaderHeight)*ArtworkScale);
                    imageLayer.Children.Add(picture);artwork.Children.Add(imageLayer);
                    var veil=new Border{IsHitTestVisible=false};artwork.Children.Add(veil);
                    var edgeVeil=new Border{IsHitTestVisible=false};artwork.Children.Add(edgeVeil);
                    void UpdateVeil(){
                        bool contrast=new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
                        artwork.Opacity=contrast?0:1;
                        // The artwork veil and the entire header must resolve to the same solid surface.
                        // A translucent base exposes the wallpaper at the veil's right edge.
                        var color=SurfaceColor();
                        var fade=new LinearGradientBrush{StartPoint=new Windows.Foundation.Point(0,0),EndPoint=new Windows.Foundation.Point(0,1)};
                        fade.GradientStops.Add(new GradientStop{Offset=0,Color=color});
                        fade.GradientStops.Add(new GradientStop{Offset=.3,Color=Color.FromArgb(235,color.R,color.G,color.B)});
                        fade.GradientStops.Add(new GradientStop{Offset=.55,Color=Color.FromArgb(200,color.R,color.G,color.B)});
                        fade.GradientStops.Add(new GradientStop{Offset=.78,Color=Color.FromArgb(125,color.R,color.G,color.B)});
                        fade.GradientStops.Add(new GradientStop{Offset=.88,Color=Color.FromArgb(140,color.R,color.G,color.B)});
                        fade.GradientStops.Add(new GradientStop{Offset=1,Color=color});
                        veil.Background=fade;
                        var edge=new LinearGradientBrush{StartPoint=new Windows.Foundation.Point(0,0),EndPoint=new Windows.Foundation.Point(1,0)};
                        edge.GradientStops.Add(new GradientStop{Offset=0,Color=Color.FromArgb(0,color.R,color.G,color.B)});
                        edge.GradientStops.Add(new GradientStop{Offset=.65,Color=Color.FromArgb(0,color.R,color.G,color.B)});
                        edge.GradientStops.Add(new GradientStop{Offset=1,Color=color});edgeVeil.Background=edge;
                    }
                    grid.ActualThemeChanged+=(_,_)=>UpdateVeil();grid.Loaded+=(_,_)=>UpdateVeil();UpdateVeil();
                    picture.Source=bitmap;bitmap.UriSource=uri;
                }
            }catch(Exception){ /* Optional artwork must never prevent device rendering. */ }
        }
        var content=new StackPanel{Spacing=8,Margin=new Thickness(DetailInset,DetailInset,124,0),VerticalAlignment=VerticalAlignment.Top};grid.Children.Add(content);
        content.Children.Add(new TextBlock{Text=device.GetProperty("device").GetProperty("name").GetString(),FontSize=28,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        content.Children.Add(new TextBlock{Text=TypeName(device),Foreground=Accent(Kind(device)),FontSize=14});
        content.Children.Add(updated);UpdateTime(updated,device);
        return header;
    }
    public static void UpdateTime(TextBlock label,JsonElement device) {
        var readings=device.GetProperty("state").EnumerateObject().Select(p=>p.Value).Where(r=>r.GetProperty("availability").GetString()!="unsupported").ToArray();
        var times=readings.Where(r=>r.TryGetProperty("updated_at_ms",out var t)&&t.ValueKind==JsonValueKind.Number).Select(r=>r.GetProperty("updated_at_ms").GetInt64()).ToArray();
        bool offline=readings.Any(r=>r.GetProperty("availability").GetString()=="offline");
        bool stale=readings.Any(r=>r.GetProperty("availability").GetString()=="stale");
        label.Foreground=Resource("TextFillColorSecondaryBrush");label.FontSize=12;
        if(times.Length==0){label.Text=offline?L.T("Offline"):L.T("Waiting for a reading");return;}
        var time=DateTimeOffset.FromUnixTimeMilliseconds(times.Min()).ToLocalTime();
        var age=DateTimeOffset.Now-time;
        string ago=age.TotalSeconds<5?L.T("just now"):age.TotalSeconds<60?(L.Language=="zh"?$"{(int)age.TotalSeconds} 秒前":$"{(int)age.TotalSeconds}s ago"):age.TotalMinutes<60?(L.Language=="zh"?$"{(int)age.TotalMinutes} 分钟前":$"{(int)age.TotalMinutes}m ago"):time.ToString("MMM d, HH:mm");
        label.Text=(offline?L.T("Offline · Last updated "):stale?L.T("Stale · Last updated "):L.T("Updated "))+ago;
        ToolTipService.SetToolTip(label,time.ToString("yyyy-MM-dd HH:mm:ss zzz"));
    }
}
