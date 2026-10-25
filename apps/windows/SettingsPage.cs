using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
namespace ShowDevice;
internal static class SettingsPage {
    private static TextBlock Heading(string title)=>new(){Text=L.T(title),FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)};
    private static Border Panel(UIElement child)=>new(){Child=child,Padding=new Thickness(16,8,16,8),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Background=DeviceVisuals.Resource("CardBackgroundFillColorDefaultBrush"),BorderBrush=DeviceVisuals.Resource("CardStrokeColorDefaultBrush")};
    private static FontIcon SettingIcon(string glyph)=>new(){Glyph=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=20,Foreground=DeviceVisuals.Resource("TextFillColorSecondaryBrush"),IsHitTestVisible=false};
    private static Grid Field(string label,FrameworkElement control,FontIcon? icon=null){
        var row=new Grid{ColumnSpacing=24,MinHeight=48,Padding=new Thickness(0,8,0,8)};
        row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var text=new TextBlock{Text=L.T(label),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        var caption=new Grid{ColumnSpacing=12,VerticalAlignment=VerticalAlignment.Center};
        caption.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});caption.ColumnDefinitions.Add(new ColumnDefinition());
        if(icon!=null){icon.HorizontalAlignment=HorizontalAlignment.Center;icon.VerticalAlignment=VerticalAlignment.Center;caption.Children.Add(icon);AutomationProperties.SetAccessibilityView(icon,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);}
        caption.Children.Add(text);Grid.SetColumn(text,1);
        row.Children.Add(caption);row.Children.Add(control);AutomationProperties.SetName(control,L.T(label));
        control.VerticalAlignment=VerticalAlignment.Center;
        row.SizeChanged+=(_,args)=>{
            bool narrow=args.NewSize.Width<420;
            row.RowSpacing=narrow?8:0;
            Grid.SetRow(control,narrow?1:0);Grid.SetColumn(control,narrow?0:1);Grid.SetColumnSpan(control,narrow?2:1);
            Grid.SetColumnSpan(caption,narrow?2:1);
            control.Width=control is ToggleSwitch||narrow?double.NaN:200;
            control.HorizontalAlignment=control is ToggleSwitch?HorizontalAlignment.Right:HorizontalAlignment.Stretch;
        };
        return row;
    }
    public static FrameworkElement Create(AppSettings current,Func<AppSettings,Task> apply){
        var draft=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(current))!;
        var content=new StackPanel{Spacing=24,MaxWidth=800,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(12,16,12,24)};
        var error=new InfoBar{IsClosable=true,Severity=InfoBarSeverity.Error};
        Task pending=Task.CompletedTask;
        async void Commit(){
            var next=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(draft))!;
            var previous=pending;
            var done=new TaskCompletionSource();pending=done.Task;
            try{await previous;await apply(next);error.IsOpen=false;}
            catch(Exception e){error.Message=ReadingNotice.Brief(e.Message);ToolTipService.SetToolTip(error,e.Message);error.IsOpen=true;}
            finally{done.SetResult();}
        }
        content.Children.Add(new TextBlock{Text=L.T("Settings"),FontSize=28,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        var general=new StackPanel{Spacing=8};general.Children.Add(Heading("General"));
        var language=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};
        language.Items.Add("简体中文");language.Items.Add("English");language.SelectedIndex=current.Language=="en"?1:0;
        general.Children.Add(Panel(Field("Language",language,SettingIcon("\uE774"))));
        var theme=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var label in new[]{"Follow system","Light","Dark"})theme.Items.Add(L.T(label));theme.SelectedIndex=Array.IndexOf(new[]{"system","light","dark"},current.Theme);
        general.Children.Add(Panel(Field("Theme",theme,SettingIcon("\uE708"))));
        theme.SelectionChanged+=(_,_)=>{draft.Theme=new[]{"system","light","dark"}[theme.SelectedIndex];Commit();};
        var interval=new NumberBox{Minimum=5,Maximum=3600,Value=current.IntervalSeconds,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};
        general.Children.Add(Panel(Field("Global refresh interval (seconds)",interval,SettingIcon("\uE72C"))));content.Children.Add(general);
        language.SelectionChanged+=(_,_)=>{draft.Language=language.SelectedIndex==1?"en":"zh";Commit();};
        interval.ValueChanged+=(_,_)=>{if(!double.IsFinite(interval.Value))return;draft.IntervalSeconds=(int)Math.Clamp(interval.Value,5,3600);Commit();};
        var surfaces=new StackPanel{Spacing=8};surfaces.Children.Add(Heading("Tray and desktop"));
        var desktop=new ToggleSwitch{IsOn=current.Surfaces.DesktopEnabled,MinWidth=0,OnContent="",OffContent=""};
        var locked=new ToggleSwitch{IsOn=current.Surfaces.Locked,MinWidth=0,OnContent="",OffContent=""};
        var width=new NumberBox{Minimum=260,Maximum=480,Value=current.Surfaces.Width,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};
        var motion=new NumberBox{Minimum=0,Maximum=1000,Value=current.Surfaces.TransitionMs,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};
        surfaces.Children.Add(Panel(Field("Desktop card",desktop,SettingIcon("\uE7F4"))));
        surfaces.Children.Add(Panel(Field("Lock card position",locked,SettingIcon("\uE72E"))));
        surfaces.Children.Add(Panel(Field("Card width",width,SettingIcon("\uE740"))));
        surfaces.Children.Add(Panel(Field("Battery transition (ms)",motion,SettingIcon("\uE916"))));
        desktop.Toggled+=(_,_)=>{draft.Surfaces.DesktopEnabled=desktop.IsOn;Commit();};
        locked.Toggled+=(_,_)=>{draft.Surfaces.Locked=locked.IsOn;Commit();};
        width.ValueChanged+=(_,_)=>{if(double.IsFinite(width.Value)){draft.Surfaces.Width=width.Value;Commit();}};
        motion.ValueChanged+=(_,_)=>{if(double.IsFinite(motion.Value)){draft.Surfaces.TransitionMs=(int)motion.Value;Commit();}};
        content.Children.Add(surfaces);
        content.Children.Add(Panel(DeviceArrangementEditor.Create(draft,Commit)));
        var devices=new StackPanel{Spacing=8};
        if(current.KnownDevices.Count>0)devices.Children.Add(Heading("Devices"));
        foreach(var (id,name) in current.KnownDevices.OrderBy(p=>p.Value)){
            var section=new StackPanel{Spacing=0};
            var visible=new ToggleSwitch{MinWidth=0,IsOn=!current.ExcludedDevices.Contains(id),OnContent=L.T("Visible"),OffContent=L.T("Excluded")};
            var kind=current.DeviceKinds.GetValueOrDefault(id,"other");
            var deviceIcon=DeviceVisuals.Icon(kind,24);deviceIcon.Foreground=DeviceVisuals.Accent(kind);
            section.Children.Add(Field(name,visible,deviceIcon));AutomationProperties.SetName(visible,name+" "+L.T("Show device"));
            var mode=new ComboBox();mode.Items.Add(L.T("Follow global"));mode.Items.Add(L.T("Custom"));mode.SelectedIndex=current.DeviceIntervals.ContainsKey(id)?1:0;
            section.Children.Add(Field("Refresh interval",mode));AutomationProperties.SetName(mode,name+" "+L.T("Refresh interval"));
            var seconds=new NumberBox{Minimum=5,Maximum=3600,Value=current.DeviceIntervals.GetValueOrDefault(id,current.IntervalSeconds),SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};
            var custom=Field("Interval (seconds)",seconds);section.Children.Add(custom);AutomationProperties.SetName(seconds,name+" "+L.T("Interval (seconds)"));
            void Update(){mode.IsEnabled=visible.IsOn;seconds.IsEnabled=visible.IsOn;custom.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;}
            Update();
            visible.Toggled+=(_,_)=>{if(visible.IsOn)draft.ExcludedDevices.Remove(id);else draft.ExcludedDevices.Add(id);Update();Commit();};
            mode.SelectionChanged+=(_,_)=>{
                if(mode.SelectedIndex==0)draft.DeviceIntervals.Remove(id);
                else{if(!double.IsFinite(seconds.Value))seconds.Value=draft.IntervalSeconds;draft.DeviceIntervals[id]=(int)seconds.Value;}
                Update();Commit();
            };
            seconds.ValueChanged+=(_,_)=>{if(mode.SelectedIndex!=1||!double.IsFinite(seconds.Value))return;draft.DeviceIntervals[id]=(int)Math.Clamp(seconds.Value,5,3600);Commit();};
            // Reserved insertion point for core-validated DeviceSettingsExtension pages.
            // Plugins never contribute native controls or execute UI code here.
            section.Children.Add(new StackPanel{Tag=new DeviceSettingsSlot(id),Spacing=8});
            devices.Children.Add(Panel(section));
        }
        content.Children.Add(devices);content.Children.Add(error);
        return content;
    }
}
internal sealed record DeviceSettingsSlot(string DeviceId);
