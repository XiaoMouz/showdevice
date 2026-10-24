using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
namespace ShowDevice;
// Consumes core/api AppSnapshot. No plugin code is loaded into this process.
internal static class PluginPage {
    public static string Value(JsonElement state,string? key){
        if(key==null||!state.TryGetProperty(key,out var r))return "Waiting for device";
        string availability=r.GetProperty("availability").GetString()!;
        if(availability=="unknown")return "Unknown";
        if(availability=="unsupported")return "Not supported";
        if(availability=="offline")return "Device offline";
        var v=r.GetProperty("value");string text=v.ValueKind==JsonValueKind.String?v.GetString()!:v.ValueKind==JsonValueKind.True?"Yes":v.ValueKind==JsonValueKind.False?"No":v.ToString();
        if(availability=="stale")text="Last known: "+text;
        
        return text;
    }
    private static bool Condition(JsonElement node,string property,JsonElement state){
        if(!node.TryGetProperty(property,out var c)||c.ValueKind==JsonValueKind.Null)return true;
        string op=c.GetProperty("op").GetString()!;
        if(op=="host_writes_enabled")return false;
        string binding=c.GetProperty("binding").GetString()!;
        if(!state.TryGetProperty(binding,out var r)||r.GetProperty("availability").GetString()!="available")return false;
        return op=="available"||(op=="equals"&&JsonElement.DeepEquals(r.GetProperty("value"),c.GetProperty("value")));
    }
    public static void Render(StackPanel panel,JsonElement device){
        var state=device.GetProperty("state");
        var caps=device.GetProperty("capabilities").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!,x=>x);
        foreach(var page in device.GetProperty("pages").EnumerateArray()){
            if(device.GetProperty("pages").GetArrayLength()>1)panel.Children.Add(new TextBlock{Text=page.GetProperty("title").GetString(),FontSize=18});
            foreach(var section in page.GetProperty("nodes").EnumerateArray().GroupBy(n=>Section(n))){
                var rows=new StackPanel{Spacing=4};
                foreach(var node in section)Add(rows,node,state,caps);
                if(rows.Children.Count==0)continue;
                var heading=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,Margin=new Thickness(0,16,0,8)};
                var icon=DeviceVisuals.Icon(section.Key,18);icon.Foreground=DeviceVisuals.Accent(DeviceVisuals.Kind(device));heading.Children.Add(icon);
                heading.Children.Add(new TextBlock{Text=section.Key switch{"power"=>"Power","connection"=>"Connection","lighting"=>"Lighting",_=>"Device settings"},FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
                panel.Children.Add(heading);panel.Children.Add(rows);
            }
        }
    }
    private static string Section(JsonElement node){
        string key=node.TryGetProperty("binding",out var b)&&b.ValueKind==JsonValueKind.String?b.GetString()!:"";
        return key.StartsWith("battery.")?"power":key is "keyboard.connection_mode" or "keyboard.bluetooth_channel"?"connection":key.StartsWith("lighting.")?"lighting":"settings";
    }
    private static void Add(StackPanel parent,JsonElement node,JsonElement state,Dictionary<string,JsonElement> caps){
        if(!Condition(node,"visible_when",state))return;
        string label=node.GetProperty("label").GetString()!;
        string component=node.GetProperty("component").GetString()!;
        string? binding=node.TryGetProperty("binding",out var b)&&b.ValueKind==JsonValueKind.String?b.GetString():null;
        if(DeviceVisuals.Unsupported(state,binding))return;
        if(component is "status" or "result"){
            if(binding=="battery.percent"){
                var battery=DeviceVisuals.Battery(state);if(battery!=null)parent.Children.Add(battery);return;
            }
            if(binding=="battery.charging"&&state.TryGetProperty("battery.percent",out var percent)&&percent.GetProperty("availability").GetString()=="available"&&state.TryGetProperty(binding,out var charge)&&charge.GetProperty("availability").GetString()=="available")return;
            var metric=new Grid{Padding=new Thickness(0,12,0,12),ColumnSpacing=20};
            metric.ColumnDefinitions.Add(new ColumnDefinition());metric.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            metric.Children.Add(new TextBlock{Text=label,Foreground=DeviceVisuals.Resource("TextFillColorSecondaryBrush"),TextWrapping=TextWrapping.Wrap});
            var readingText=new TextBlock{Text=Value(state,binding),FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Right,TextWrapping=TextWrapping.Wrap};
            Grid.SetColumn(readingText,1);metric.Children.Add(readingText);AutomationProperties.SetName(readingText,label+": "+Value(state,binding));parent.Children.Add(metric);return;
        }
        var row=new StackPanel{Spacing=6,Margin=new Thickness(0,4,0,8)};
        if(component=="group"){row.Children.Add(new TextBlock{Text=label,FontSize=16});foreach(var child in node.GetProperty("children").EnumerateArray())Add(row,child,state,caps);parent.Children.Add(row);return;}
        if(component is not ("toggle" or "button"))row.Children.Add(new TextBlock{Text=label,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        FrameworkElement control;
        JsonElement cap=default;if(binding!=null)caps.TryGetValue(binding,out cap);
        double min=0,max=100,value=0;
        if(cap.ValueKind==JsonValueKind.Object){var t=cap.GetProperty("value_type");if(t.TryGetProperty("min",out var m))min=m.GetDouble();if(t.TryGetProperty("max",out var x))max=x.GetDouble();}
        if(binding!=null&&state.TryGetProperty(binding,out var reading)&&reading.TryGetProperty("value",out var val)&&val.ValueKind==JsonValueKind.Number)value=val.GetDouble();
        switch(component){
            case "toggle":control=new ToggleSwitch{Header=label,IsEnabled=false};break;
            case "select":
                var combo=new ComboBox{IsEnabled=false,PlaceholderText="Read-only"};
                if(cap.ValueKind==JsonValueKind.Object&&cap.GetProperty("value_type").TryGetProperty("values",out var choices))foreach(var choice in choices.EnumerateArray())combo.Items.Add(choice.GetString());
                control=combo;break;
            case "number":control=new NumberBox{Minimum=min,Maximum=max,Value=value,IsEnabled=false};break;
            case "slider":control=new Slider{Minimum=min,Maximum=max,Value=value,IsEnabled=false};break;
            case "button":control=new Button{Content=label,IsEnabled=false};break;
            default:control=new TextBlock{Text=Value(state,binding),TextWrapping=TextWrapping.Wrap};break;
        }
        // All action/edit controls stay disabled until the host write API exists.
        AutomationProperties.SetName(control,label);
        row.Children.Add(control);parent.Children.Add(row);
    }
}



