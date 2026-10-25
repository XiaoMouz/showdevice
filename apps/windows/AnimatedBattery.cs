using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
namespace ShowDevice;

internal sealed class AnimatedBattery : Grid {
    private readonly Border fill=new(){Height=5,CornerRadius=new(1),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(2,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    private readonly Border outline=new(){Width=17,Height=9,CornerRadius=new(2),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Left};
    private readonly Border terminal=new(){Width=2,Height=4,CornerRadius=new(1),HorizontalAlignment=HorizontalAlignment.Right};
    private readonly TextBlock number=new(){FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Width=38,HorizontalTextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock question=new(){Text="?",FontFamily=new("Segoe UI"),FontSize=10,FontWeight=Microsoft.UI.Text.FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly FontIcon warning=new(){Glyph="\uE7BA",FontFamily=new("Segoe Fluent Icons"),FontSize=14};
    private readonly Grid battery=new(){Width=20,Height=10,VerticalAlignment=VerticalAlignment.Center};
    private readonly Grid indicator=new(){Width=24,Height=24,VerticalAlignment=VerticalAlignment.Center};
    private readonly Microsoft.UI.Xaml.Shapes.Path ringTrack=new(){StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Opacity=.2};
    private readonly Microsoft.UI.Xaml.Shapes.Path ringFill=new(){StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
    private readonly ChargingGlyph batteryBolt=new(5,7){HorizontalAlignment=HorizontalAlignment.Left,Margin=new(6,0,0,0)};
    private readonly ChargingGlyph ringBolt=new(7,10);
    private readonly Border lineFill=new(){Height=2,HorizontalAlignment=HorizontalAlignment.Left,RenderTransform=new ScaleTransform{ScaleX=0},RenderTransformOrigin=new(0,0)};
    public Grid TopLine {get;}=new(){Height=2,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly Stopwatch elapsed=new();
    private double shown,target,start;
    private bool hasValue,active,muted,missing,failed,isCharging;
    private int duration;
    private string signature="";
    private SurfaceAppearance style=new();
    private bool tile;
    private FontIcon? deviceGlyph;
    public void UseDeviceTile(FontIcon glyph){
        tile=true;deviceGlyph=glyph;Children.Clear();ColumnDefinitions.Clear();ColumnSpacing=0;Width=88;Height=88;
        ringTrack.StrokeThickness=ringFill.StrokeThickness=.85;
        var ring=new Viewbox{Child=indicator,Width=76,Height=76,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};Children.Add(ring);
        glyph.FontSize=26;glyph.Width=30;glyph.Height=30;glyph.HorizontalAlignment=HorizontalAlignment.Center;glyph.VerticalAlignment=VerticalAlignment.Top;glyph.Margin=new(0,23,0,0);Children.Add(glyph);
        number.Width=48;number.FontSize=12;number.HorizontalTextAlignment=TextAlignment.Center;number.HorizontalAlignment=HorizontalAlignment.Center;number.VerticalAlignment=VerticalAlignment.Top;number.Margin=new(0,67,0,0);SetColumn(number,0);Children.Add(number);
        // Keep the mark in unscaled tile coordinates, below the device and inside the arc.
        indicator.Children.Remove(ringBolt);Children.Add(ringBolt);
        ringBolt.VerticalAlignment=VerticalAlignment.Top;ringBolt.Margin=new(0,54,0,0);
    }
    public AnimatedBattery(){
        ColumnSpacing=5;ColumnDefinitions.Add(new(){Width=new GridLength(24)});ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        battery.Children.Add(outline);battery.Children.Add(terminal);battery.Children.Add(fill);battery.Children.Add(batteryBolt);
        indicator.Children.Add(battery);indicator.Children.Add(ringTrack);indicator.Children.Add(ringFill);indicator.Children.Add(ringBolt);indicator.Children.Add(question);indicator.Children.Add(warning);
        Children.Add(indicator);Children.Add(number);SetColumn(number,1);
        var lineTrack=new Border{Height=2,Opacity=.18,Background=DeviceVisuals.Resource("TextFillColorSecondaryBrush")};TopLine.Children.Add(lineTrack);TopLine.Children.Add(lineFill);
        TopLine.SizeChanged+=(_,e)=>lineFill.Width=e.NewSize.Width;
        ringTrack.Data=Arc(100);
        timer.Tick+=(_,_)=>{shown=SurfacePolicy.Interpolate(start,target,elapsed.Elapsed.TotalMilliseconds,duration);Draw();if(elapsed.ElapsedMilliseconds>=duration)Stop();};
        Unloaded+=(_,_)=>timer.Stop();
        Loaded+=(_,_)=>Draw();ActualThemeChanged+=(_,_)=>Draw();
    }
    private static Geometry Arc(double percent){
        var point=AppearanceRules.RingPoint(percent);var first=AppearanceRules.RingPoint(0);
        var figure=new PathFigure{StartPoint=new(first.X,first.Y),IsClosed=false};
        if(percent>0)figure.Segments.Add(new ArcSegment{Point=new(point.X,point.Y),Size=new Size(9,9),SweepDirection=SweepDirection.Clockwise,IsLargeArc=percent*3.3>180});
        var geometry=new PathGeometry();geometry.Figures.Add(figure);return geometry;
    }
    private void Draw(){
        number.Text=hasValue?$"{shown:0}%":tile?"?":"—";fill.Width=13*Math.Clamp(shown,0,100)/100;
        ringFill.Data=Arc(missing||failed?0:shown);((ScaleTransform)lineFill.RenderTransform).ScaleX=missing||failed?0:Math.Clamp(shown,0,100)/100;
        Brush brush=muted?DeviceVisuals.Resource("TextFillColorSecondaryBrush"):new SolidColorBrush(BackgroundLayer.Color(AppearanceRules.BatteryColor(style,shown,ActualTheme==ElementTheme.Dark)));
        if(new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)brush=DeviceVisuals.Resource("TextFillColorPrimaryBrush");
        fill.Background=outline.BorderBrush=terminal.Background=number.Foreground=question.Foreground=lineFill.Background=brush;ringTrack.Stroke=ringFill.Stroke=brush;batteryBolt.Fill=ringBolt.Fill=brush;
    }
    public void SetActive(bool value){active=value;if(!value)Stop();}
    private void Stop(){timer.Stop();shown=target;Draw();}
    public bool Update(JsonElement device,int interval,int transition,SurfaceAppearance appearance){
        style=appearance;var state=device.GetProperty("state");
        if(!state.TryGetProperty("battery.percent",out var r)||DeviceVisuals.Unsupported(state,"battery.percent")){
            TopLine.Visibility=ringBolt.Visibility=batteryBolt.Visibility=Visibility.Collapsed;timer.Stop();
            if(tile){Visibility=Visibility.Visible;indicator.Visibility=number.Visibility=Visibility.Collapsed;if(deviceGlyph!=null)deviceGlyph.Visibility=Visibility.Visible;}
            else Visibility=Visibility.Collapsed;
            return false;
        }
        Visibility=Visibility.Visible;
        indicator.Visibility=number.Visibility=Visibility.Visible;
        string next=r.GetRawText()+ReadingNotice.Error(device)+(state.TryGetProperty("battery.charging",out var c)?c.GetRawText():"");
        bool changed=next!=signature;signature=next;
        var availability=r.GetProperty("availability").GetString();
        bool stale=availability=="stale"||(r.TryGetProperty("updated_at_ms",out var time)&&time.ValueKind==JsonValueKind.Number&&DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-time.GetInt64()>Math.Max(10,interval*2)*1000L);
        failed=ReadingNotice.IsParseFailure(ReadingNotice.Error(device));
        bool numeric=r.TryGetProperty("value",out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out _);
        missing=availability=="unknown"||!numeric;muted=stale||failed||missing;
        var newValue=numeric?Math.Clamp(v.GetDouble(),0,100):0;
        isCharging=ReadingNotice.IsCharging(state);
        warning.Foreground=DeviceVisuals.Resource("SystemFillColorCautionBrush");warning.Visibility=failed?Visibility.Visible:Visibility.Collapsed;
        battery.Visibility=!tile&&style.Indicator=="icon"&&!failed?Visibility.Visible:Visibility.Collapsed;
        ringTrack.Visibility=ringFill.Visibility=(tile||style.Indicator=="ring")&&!failed?Visibility.Visible:Visibility.Collapsed;
        TopLine.Visibility=!tile&&style.Indicator=="line"?Visibility.Visible:Visibility.Collapsed;
        if(deviceGlyph!=null)deviceGlyph.Visibility=failed?Visibility.Collapsed:Visibility.Visible;
        // Charging occupies the same fixed indicator slot; never adds a trailing column.
        bool charging=isCharging&&!failed&&!missing;
        batteryBolt.Visibility=charging&&!tile&&style.Indicator=="icon"?Visibility.Visible:Visibility.Collapsed;
        ringBolt.Visibility=charging&&(tile||style.Indicator!="icon")?Visibility.Visible:Visibility.Collapsed;
        fill.Visibility=!missing&&!isCharging?Visibility.Visible:Visibility.Collapsed;
        question.Visibility=missing&&!failed&&!tile?Visibility.Visible:Visibility.Collapsed;
        if(changed){
            bool animate=active&&hasValue&&numeric&&newValue!=target&&!failed&&availability=="available"&&new Windows.UI.ViewManagement.UISettings().AnimationsEnabled&&transition>0;
            start=shown;target=newValue;hasValue=numeric;duration=transition;
            if(animate){elapsed.Restart();timer.Start();}else Stop();
        }
        Draw();
        string hint=failed?ReadingNotice.Brief(ReadingNotice.Error(device)):stale?ReadingNotice.BatteryHint("stale"):missing?ReadingNotice.BatteryHint("unknown"):L.T("Battery")+$" {newValue:0}%";
        if(isCharging)hint+=" · "+L.T("Charging");ToolTipService.SetToolTip(this,hint);AutomationProperties.SetName(this,hint);
        return changed;
    }
}
