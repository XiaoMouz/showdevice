using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
namespace ShowDevice;

internal static class PersonalizationPage {
    private static TextBlock Title(string text,int size=16)=>new(){Text=L.T(text),FontSize=size,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};
    private static Border Panel(UIElement content)=>new(){Child=content,Padding=new(16),CornerRadius=new(10),Background=DeviceVisuals.Resource("CardBackgroundFillColorDefaultBrush"),BorderBrush=DeviceVisuals.Resource("CardStrokeColorDefaultBrush"),BorderThickness=new(1)};
    private static Grid Row(string label,FrameworkElement control){
        var row=new Grid{ColumnSpacing=20,MinHeight=42};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(new TextBlock{Text=L.T(label),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap});row.Children.Add(control);Grid.SetColumn(control,1);control.VerticalAlignment=VerticalAlignment.Center;AutomationProperties.SetName(control,L.T(label));return row;
    }
    private static ComboBox Choice(string[] values,int selected,Action<int> change){var combo=new ComboBox{MinWidth=160};foreach(var value in values)combo.Items.Add(L.T(value));combo.SelectedIndex=Math.Max(0,selected);combo.SelectionChanged+=(_,_)=>change(combo.SelectedIndex);return combo;}
    private static Button ColorButton(string value,Action<string> change){
        var swatch=new Border{Width=36,Height=18,CornerRadius=new(4),Background=new SolidColorBrush(BackgroundLayer.Color(value))};
        var button=new Button{Content=swatch};var picker=new ColorPicker{Color=BackgroundLayer.Color(value),IsAlphaEnabled=false,IsMoreButtonVisible=false,IsColorSliderVisible=true,MinWidth=260};button.Flyout=new Flyout{Content=picker};
        AutomationProperties.SetName(button,L.T("Background color"));
        picker.ColorChanged+=(_,e)=>{string hex=$"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";swatch.Background=new SolidColorBrush(e.NewColor);change(hex);};return button;
    }
    private static FrameworkElement BackgroundEditor(string label,BackgroundSpec spec,Window owner,Action commit,Action<Exception> fail){
        var panel=new StackPanel{Spacing=10};panel.Children.Add(Title(label));
        FrameworkElement? overlay=null,transparencyRow=null;Button? colorControl=null;Button? chooseControl=null;
        void Refresh(){if(overlay!=null)overlay.Visibility=spec.Kind=="image"?Visibility.Visible:Visibility.Collapsed;if(transparencyRow!=null)transparencyRow.Visibility=spec.Kind=="color"?Visibility.Visible:Visibility.Collapsed;if(colorControl!=null)colorControl.Visibility=spec.Kind=="color"?Visibility.Visible:Visibility.Collapsed;if(chooseControl!=null)chooseControl.Visibility=spec.Kind=="image"?Visibility.Visible:Visibility.Collapsed;}
        var options=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var mode=Choice(new[]{"System background","Solid color","Local image"},Array.IndexOf(new[]{"system","color","image"},spec.Kind),i=>{spec.Kind=new[]{"system","color","image"}[i];Refresh();commit();});options.Children.Add(mode);
        colorControl=ColorButton(spec.Color,color=>{spec.Color=color;spec.Kind="color";mode.SelectedIndex=1;commit();});options.Children.Add(colorControl);
        var choose=new Button{Content=L.T("Choose image")};options.Children.Add(choose);chooseControl=choose;
        var tint=new Slider{Minimum=0,Maximum=100,Value=spec.Tint*100,Width=160};tint.ValueChanged+=(_,e)=>{spec.Tint=e.NewValue/100;commit();};
        var transparency=new Slider{Minimum=0,Maximum=100,StepFrequency=1,Value=Math.Round((1-spec.Opacity)*100),Width=120};
        AutomationProperties.SetName(transparency,L.T(label)+" · "+L.T("Background transparency"));
        var percent=new TextBlock{Text=$"{transparency.Value:0}%",Width=42,HorizontalTextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        var opacityControl=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};opacityControl.Children.Add(transparency);opacityControl.Children.Add(percent);
        transparency.ValueChanged+=(_,e)=>{spec.Opacity=1-e.NewValue/100;percent.Text=$"{e.NewValue:0}%";commit();};
        transparencyRow=Row("Background transparency",opacityControl);
        transparencyRow.SizeChanged+=(_,e)=>{
            var row=(Grid)transparencyRow;bool narrow=e.NewSize.Width<340;
            if(row.RowDefinitions.Count==0){row.RowDefinitions.Add(new(){Height=GridLength.Auto});row.RowDefinitions.Add(new(){Height=GridLength.Auto});}
            Grid.SetRow(opacityControl,narrow?1:0);Grid.SetColumn(opacityControl,narrow?0:1);Grid.SetColumnSpan(opacityControl,narrow?2:1);
            opacityControl.HorizontalAlignment=HorizontalAlignment.Right;
        };
        choose.Click+=async(_,_)=>{
            try{
                var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(owner));
                foreach(var extension in new[]{".png",".jpg",".jpeg"})picker.FileTypeFilter.Add(extension);
                var file=await picker.PickSingleFileAsync();if(file==null)return;
                var info=new FileInfo(file.Path);if(info.Length>20*1024*1024)throw new InvalidDataException(L.T("Image must be smaller than 20 MB."));
                using(var input=await file.OpenReadAsync()){
                    var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(input);
                    if(decoder.PixelWidth>16000||decoder.PixelHeight>16000||(ulong)decoder.PixelWidth*decoder.PixelHeight>64000000)throw new InvalidDataException(L.T("Image dimensions are too large."));
                }
                byte[] bytes=await File.ReadAllBytesAsync(file.Path);var hash=Convert.ToHexString(SHA256.HashData(bytes));
                string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShowDevice","backgrounds");Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,hash+Path.GetExtension(file.Path).ToLowerInvariant());if(!File.Exists(path))await File.WriteAllBytesAsync(path,bytes);
                spec.Image=path;spec.Kind="image";mode.SelectedIndex=2;commit();
            }catch(Exception e){fail(e);}
        };
        panel.SizeChanged+=(_,e)=>options.Orientation=e.NewSize.Width<360?Orientation.Vertical:Orientation.Horizontal;
        panel.Children.Add(options);panel.Children.Add(transparencyRow);overlay=Row("Image overlay",tint);panel.Children.Add(overlay);Refresh();return panel;
    }
    public static FrameworkElement Create(AppSession session,Window owner,Func<Personalization,Task> apply){
        var draft=JsonSerializer.Deserialize<Personalization>(JsonSerializer.Serialize(session.Settings.Personalization))!;
        var root=new StackPanel{Spacing=20,Margin=new(12,16,12,24),MaxWidth=850};root.Children.Add(Title("Personalization",28));
        var tabs=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var text in new[]{"Main window","Desktop card","Tray popup"})tabs.Items.Add(L.T(text));tabs.SelectedIndex=0;root.Children.Add(tabs);
        var content=new StackPanel{Spacing=16};root.Children.Add(content);var error=new InfoBar{IsClosable=true,Severity=InfoBarSeverity.Error};root.Children.Add(error);
        var debounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};Task pending=Task.CompletedTask;
        async void Flush(){debounce.Stop();var snapshot=JsonSerializer.Deserialize<Personalization>(JsonSerializer.Serialize(draft))!;snapshot.Normalize();var previous=pending;var done=new TaskCompletionSource();pending=done.Task;try{await previous;await apply(snapshot);error.IsOpen=false;}catch(Exception e){Fail(e);}finally{done.SetResult();}}
        void Commit(){debounce.Stop();debounce.Start();}
        void Fail(Exception e){error.Message=e is InvalidDataException?e.Message:L.T("Unable to apply appearance.");ToolTipService.SetToolTip(error,e.Message);error.IsOpen=true;}
        debounce.Tick+=(_,_)=>Flush();root.Unloaded+=(_,_)=>{if(debounce.IsEnabled)Flush();};
        void Backgrounds(BackgroundSpec outer,BackgroundSpec common,Dictionary<string,BackgroundSpec> overrides){
            content.Children.Add(Panel(BackgroundEditor(tabs.SelectedIndex==0?"Window background":"Surface background",outer,owner,Commit,Fail)));
            content.Children.Add(Panel(BackgroundEditor("Device panel background",common,owner,Commit,Fail)));
            var device=new ComboBox{PlaceholderText=L.T("Choose device"),HorizontalAlignment=HorizontalAlignment.Stretch};
            var ids=session.Settings.KnownDevices.Keys.ToArray();foreach(var id in ids)device.Items.Add(session.Settings.KnownDevices[id]);
            var editor=new StackPanel{Spacing=12};var group=new StackPanel{Spacing=12};group.Children.Add(Title("Per-device background"));group.Children.Add(device);group.Children.Add(editor);content.Children.Add(Panel(group));
            device.SelectionChanged+=(_,_)=>{
                editor.Children.Clear();if(device.SelectedIndex<0)return;string id=ids[device.SelectedIndex];
                var enabled=new ToggleSwitch{OnContent="",OffContent="",Header=L.T("Use a separate background"),IsOn=overrides.ContainsKey(id),MinWidth=0};editor.Children.Add(enabled);
                var spec=overrides.GetValueOrDefault(id)??JsonSerializer.Deserialize<BackgroundSpec>(JsonSerializer.Serialize(common))!;
                var fields=new ContentControl{Content=BackgroundEditor(session.Settings.KnownDevices[id],spec,owner,Commit,Fail),HorizontalContentAlignment=HorizontalAlignment.Stretch};fields.IsEnabled=enabled.IsOn;editor.Children.Add(fields);
                enabled.Toggled+=(_,_)=>{if(enabled.IsOn)overrides[id]=spec;else overrides.Remove(id);fields.IsEnabled=enabled.IsOn;Commit();};
            };
        }
        void Build(){
            content.Children.Clear();
            if(tabs.SelectedIndex==0){Backgrounds(draft.MainBackground,draft.DeviceBackground,draft.DeviceBackgrounds);return;}
            var style=tabs.SelectedIndex==1?draft.Desktop:draft.Tray;
            FrameworkElement? parameters=null,batteryOption=null,pluginOption=null;
            var layout=new StackPanel{Spacing=8};
            layout.Children.Add(Row("Theme",Choice(new[]{"Follow app","Follow system","Light","Dark"},Array.IndexOf(new[]{"app","system","light","dark"},style.Theme),i=>{style.Theme=new[]{"app","system","light","dark"}[i];Commit();})));
            layout.Children.Add(Row("Layout",Choice(new[]{"List","Cards","Detailed list"},Array.IndexOf(new[]{"list","cards","details"},style.Layout),i=>{style.Layout=new[]{"list","cards","details"}[i];if(parameters!=null)parameters.Visibility=style.Layout=="details"?Visibility.Visible:Visibility.Collapsed;if(batteryOption!=null)batteryOption.Visibility=style.Layout=="cards"?Visibility.Collapsed:Visibility.Visible;if(pluginOption!=null)pluginOption.Visibility=style.Layout=="cards"?Visibility.Collapsed:Visibility.Visible;Commit();})));
            batteryOption=Row("Battery indicator",Choice(new[]{"Battery icon","Top line","Open ring"},Array.IndexOf(new[]{"icon","line","ring"},style.Indicator),i=>{style.Indicator=new[]{"icon","line","ring"}[i];Commit();}));batteryOption.Visibility=style.Layout=="cards"?Visibility.Collapsed:Visibility.Visible;layout.Children.Add(batteryOption);
            var plugin=new ToggleSwitch{OnContent="",OffContent="",IsOn=style.PluginLayout,MinWidth=0};pluginOption=Row("Plugin layout",plugin);pluginOption.Visibility=style.Layout=="cards"?Visibility.Collapsed:Visibility.Visible;layout.Children.Add(pluginOption);plugin.Toggled+=(_,_)=>{style.PluginLayout=plugin.IsOn;Commit();};content.Children.Add(Panel(layout));
            var colors=new StackPanel{Spacing=8};colors.Children.Add(Title("Battery colors"));
            colors.Children.Add(Row("Low battery",ColorButton(style.LowColor,c=>{style.LowColor=c;Commit();})));
            var low=new NumberBox{Value=style.LowAt,Minimum=0,Maximum=98,Width=160,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};colors.Children.Add(Row("Low threshold (%)",low));
            colors.Children.Add(Row("Medium battery",ColorButton(style.MediumColor,c=>{style.MediumColor=c;Commit();})));
            var medium=new NumberBox{Value=style.MediumAt,Minimum=1,Maximum=99,Width=160,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};colors.Children.Add(Row("Medium threshold (%)",medium));medium.ValueChanged+=(_,_)=>{if(double.IsFinite(medium.Value)){style.MediumAt=(int)medium.Value;Commit();}};
            low.ValueChanged+=(_,_)=>{if(double.IsFinite(low.Value)){style.LowAt=(int)low.Value;medium.Minimum=style.LowAt+1;if(medium.Value<medium.Minimum)medium.Value=medium.Minimum;Commit();}};medium.Minimum=style.LowAt+1;
            colors.Children.Add(Row("High battery",ColorButton(style.HighColor,c=>{style.HighColor=c;Commit();})));content.Children.Add(Panel(colors));
            var parameterBox=new StackPanel{Spacing=10};parameterBox.Children.Add(Title("Displayed parameters"));
            if(session.Latest is { } snapshot)foreach(var device in snapshot.GetProperty("devices").EnumerateArray()){
                string id=device.GetProperty("device").GetProperty("id").GetString()!;var choices=new StackPanel{Spacing=2};var labels=SurfaceDeviceCard.Labels(device);
                var state=device.GetProperty("state");var available=state.EnumerateObject().Where(p=>!p.Name.StartsWith("battery.")&&!DeviceVisuals.Unsupported(state,p.Name)).Select(p=>p.Name).Take(64).ToArray();
                if(available.Length==0)continue;
                choices.Children.Add(Title(device.GetProperty("device").GetProperty("name").GetString()!,14));
                foreach(var key in available){
                    var pick=new CheckBox{Content=L.T(labels.GetValueOrDefault(key,key)),IsChecked=style.Fields.TryGetValue(id,out var selected)?selected.Contains(key):available.Take(3).Contains(key)};
                    void Changed(){if(!style.Fields.TryGetValue(id,out var list))style.Fields[id]=list=available.Take(3).ToList();if(pick.IsChecked==true){if(!list.Contains(key)&&list.Count<12)list.Add(key);}else list.Remove(key);Commit();}
                    pick.Checked+=(_,_)=>Changed();pick.Unchecked+=(_,_)=>Changed();choices.Children.Add(pick);
                }
                parameterBox.Children.Add(choices);
            }
            parameters=Panel(parameterBox);parameters.Visibility=style.Layout=="details"?Visibility.Visible:Visibility.Collapsed;content.Children.Add(parameters);Backgrounds(style.Background,style.DeviceBackground,style.DeviceBackgrounds);
        }
        tabs.SelectionChanged+=(_,_)=>Build();Build();return root;
    }
}
