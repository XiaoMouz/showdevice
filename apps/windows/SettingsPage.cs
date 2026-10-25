using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
namespace ShowDevice;
internal static class SettingsPage {
    private static TextBlock Heading(string title)=>new(){Text=L.T(title),FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Margin=new Thickness(0,8,0,0)};
    private static Grid Field(string label,FrameworkElement control){
        var row=new Grid{ColumnSpacing=24,RowSpacing=8,Padding=new Thickness(0,10,0,10)};
        row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var text=new TextBlock{Text=L.T(label),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        row.Children.Add(text);row.Children.Add(control);AutomationProperties.SetName(control,L.T(label));
        row.SizeChanged+=(_,args)=>{
            bool narrow=args.NewSize.Width<420;
            Grid.SetRow(control,narrow?1:0);Grid.SetColumn(control,narrow?0:1);Grid.SetColumnSpan(control,narrow?2:1);
            Grid.SetColumnSpan(text,narrow?2:1);control.Width=narrow?double.NaN:200;control.HorizontalAlignment=HorizontalAlignment.Stretch;
        };
        return row;
    }
    public static FrameworkElement Create(AppSettings current,Func<AppSettings,Task> apply,Action reset){
        var draft=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(current))!;
        var content=new StackPanel{Spacing=20,MaxWidth=800,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(12,16,24,24)};
        content.Children.Add(new TextBlock{Text=L.T("Settings"),FontSize=28,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        var general=new StackPanel{Spacing=2};general.Children.Add(Heading("General"));
        var language=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};
        language.Items.Add("简体中文");language.Items.Add("English");language.SelectedIndex=current.Language=="en"?1:0;
        general.Children.Add(Field("Language",language));
        var interval=new NumberBox{Minimum=5,Maximum=3600,Value=current.IntervalSeconds,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};
        general.Children.Add(Field("Global refresh interval (seconds)",interval));content.Children.Add(general);
        var entries=new List<(string Id,CheckBox Global,NumberBox Seconds,CheckBox Exclude)>();
        var deviceSection=new StackPanel{Spacing=0};
        if(current.KnownDevices.Count>0)deviceSection.Children.Add(Heading("Per-device settings"));
        foreach(var (id,name) in current.KnownDevices.OrderBy(p=>p.Value)){
            var section=new StackPanel{Spacing=10,Padding=new Thickness(0,16,0,16)};
            section.Children.Add(new TextBlock{Text=name,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
            var global=new CheckBox{Content=L.T("Use global interval"),IsChecked=!current.DeviceIntervals.ContainsKey(id)};
            var seconds=new NumberBox{Minimum=5,Maximum=3600,Value=current.DeviceIntervals.GetValueOrDefault(id,current.IntervalSeconds),SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};
            var excluded=new CheckBox{Content=L.T("Exclude device"),IsChecked=current.ExcludedDevices.Contains(id)};
            var options=new Grid{ColumnSpacing=16,RowSpacing=4};options.ColumnDefinitions.Add(new ColumnDefinition());options.ColumnDefinitions.Add(new ColumnDefinition());
            options.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});options.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            options.Children.Add(global);options.Children.Add(excluded);Grid.SetColumn(excluded,1);
            options.SizeChanged+=(_,args)=>{bool narrow=args.NewSize.Width<420;Grid.SetRow(excluded,narrow?1:0);Grid.SetColumn(excluded,narrow?0:1);Grid.SetColumnSpan(global,narrow?2:1);Grid.SetColumnSpan(excluded,narrow?2:1);};
            var customInterval=Field("Refresh interval (seconds)",seconds);
            void Enable(){global.IsEnabled=excluded.IsChecked!=true;seconds.IsEnabled=excluded.IsChecked!=true;customInterval.Visibility=global.IsChecked==true?Visibility.Collapsed:Visibility.Visible;}
            global.Checked+=(_,_)=>Enable();global.Unchecked+=(_,_)=>Enable();excluded.Checked+=(_,_)=>Enable();excluded.Unchecked+=(_,_)=>Enable();Enable();
            AutomationProperties.SetName(seconds,name+" "+L.T("Refresh interval (seconds)"));
            AutomationProperties.SetName(global,name+" "+L.T("Use global interval"));AutomationProperties.SetName(excluded,name+" "+L.T("Exclude device"));
            section.Children.Add(options);section.Children.Add(customInterval);
            deviceSection.Children.Add(new Border{Child=section,BorderThickness=new Thickness(0,0,0,1),BorderBrush=DeviceVisuals.Resource("DividerStrokeColorDefaultBrush")});
            entries.Add((id,global,seconds,excluded));
        }
        content.Children.Add(deviceSection);
        var error=new InfoBar{IsClosable=true,Severity=InfoBarSeverity.Error};content.Children.Add(error);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,HorizontalAlignment=HorizontalAlignment.Right};
        var save=new Button{Content=L.T("Save"),MinWidth=88,Style=(Style)Application.Current.Resources["AccentButtonStyle"]};
        var cancel=new Button{Content=L.T("Cancel"),MinWidth=88};
        actions.Children.Add(cancel);actions.Children.Add(save);content.Children.Add(actions);
        cancel.Click+=(_,_)=>reset();
        save.Click+=async (_,_)=>{
            if(double.IsNaN(interval.Value)||entries.Any(e=>e.Global.IsChecked!=true&&double.IsNaN(e.Seconds.Value))){error.Message=L.T("Enter a refresh interval between 5 and 3600 seconds.");error.IsOpen=true;return;}
            draft.Language=language.SelectedIndex==1?"en":"zh";draft.IntervalSeconds=(int)Math.Clamp(interval.Value,5,3600);
            foreach(var entry in entries){
                if(entry.Global.IsChecked==true)draft.DeviceIntervals.Remove(entry.Id);else draft.DeviceIntervals[entry.Id]=(int)Math.Clamp(entry.Seconds.Value,5,3600);
                if(entry.Exclude.IsChecked==true)draft.ExcludedDevices.Add(entry.Id);else draft.ExcludedDevices.Remove(entry.Id);
            }
            save.IsEnabled=false;cancel.IsEnabled=false;
            try{await apply(draft);}catch(Exception e){error.Message=ReadingNotice.Brief(e.Message);ToolTipService.SetToolTip(error,e.Message);error.IsOpen=true;}
            finally{save.IsEnabled=true;cancel.IsEnabled=true;}
        };
        return content;
    }
}
