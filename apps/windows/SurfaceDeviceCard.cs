using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
namespace ShowDevice;

internal sealed class SurfaceDeviceCard {
    public BackgroundLayer View {get;}
    public Button DragHandle {get;}
    public AnimatedBattery Battery {get;}=new();
    private readonly StackPanel content=new(){Spacing=8};
    private readonly StackPanel details=new(){Spacing=6};
    private readonly StackPanel plugin=new(){Spacing=4};
    private readonly Dictionary<string,TextBlock> values=new();
    private JsonElement? previous;
    private string fieldsSignature="";
    private readonly string id,surface;
    private readonly SurfaceAppearance appearance;
    public SurfaceDeviceCard(JsonElement device,SurfaceAppearance appearance,string surface,Action<string> open){
        this.appearance=appearance;this.surface=surface;id=device.GetProperty("device").GetProperty("id").GetString()!;
        bool card=appearance.Layout=="cards";
        var grid=new Grid{ColumnSpacing=10,RowSpacing=6};grid.ColumnDefinitions.Add(new(){Width=new GridLength(card?38:24)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var icon=DeviceVisuals.Icon(DeviceVisuals.Kind(device),card?32:20);icon.Foreground=DeviceVisuals.Accent(DeviceVisuals.Kind(device));grid.Children.Add(icon);
        var name=new TextBlock{Text=device.GetProperty("device").GetProperty("name").GetString(),FontSize=card?15:13,FontWeight=card?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=card?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalAlignment=VerticalAlignment.Center};
        grid.Children.Add(name);Grid.SetColumn(name,1);grid.Children.Add(Battery);Grid.SetColumn(Battery,2);
        if(card){grid.Children.Clear();grid.ColumnDefinitions.Clear();grid.RowDefinitions.Clear();Grid.SetColumn(Battery,0);Battery.UseDeviceTile(icon);grid.Children.Add(Battery);grid.HorizontalAlignment=HorizontalAlignment.Center;grid.VerticalAlignment=VerticalAlignment.Center;}
        var button=new Button{Content=grid,Style=(Style)Application.Current.Resources["SurfaceDeviceButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center,Padding=card?new Thickness(6):new Thickness(10,6,10,6),MinHeight=card?100:44,CornerRadius=new(card?12:8)};
        DragHandle=button;
        button.Click+=(_,_)=>open(id);AutomationProperties.SetName(button,name.Text);ToolTipService.SetToolTip(button,name.Text);
        content.Children.Add(button);content.Children.Add(details);content.Children.Add(plugin);
        details.Margin=new Thickness(10,0,10,6);plugin.Margin=new Thickness(10,0,10,6);
        // The button owns the full tile/row, so its hover and focus match the background bounds.
        if(card)content.Children.Remove(button);
        View=new BackgroundLayer(card?button:content,card?12:8);View.Children.Add(Battery.TopLine);
        if(card){View.BorderThickness=new(1);View.BorderBrush=DeviceVisuals.Resource("CardStrokeColorDefaultBrush");}
    }
    public bool Update(JsonElement device,AppSettings settings){
        View.Apply(appearance.ForDevice(id));
        bool changed=Battery.Update(device,settings.DeviceIntervals.GetValueOrDefault(id,settings.IntervalSeconds),settings.Surfaces.TransitionMs,appearance);
        var state=device.GetProperty("state");
        var fields=appearance.Layout=="details"?(appearance.Fields.TryGetValue(id,out var selected)?selected:state.EnumerateObject().Where(p=>!p.Name.StartsWith("battery.")).Select(p=>p.Name).Take(3).ToList()):new List<string>();
        fields=fields.Where(key=>state.TryGetProperty(key,out _)&&!DeviceVisuals.Unsupported(state,key)).ToList();
        string next=string.Join('|',fields);
        if(next!=fieldsSignature){
            fieldsSignature=next;details.Children.Clear();values.Clear();var labels=Labels(device);
            foreach(var key in fields){
                var row=new Grid{ColumnSpacing=12};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                row.Children.Add(new TextBlock{Text=L.T(labels.GetValueOrDefault(key,key)),FontSize=12,Foreground=DeviceVisuals.Resource("TextFillColorSecondaryBrush"),TextTrimming=TextTrimming.CharacterEllipsis});
                var value=new TextBlock{FontSize=12,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,MaxWidth=150,TextTrimming=TextTrimming.CharacterEllipsis};Grid.SetColumn(value,1);row.Children.Add(value);details.Children.Add(row);values[key]=value;
            }
        }
        foreach(var (key,text) in values){string value=PluginPage.Value(state,key);if(text.Text!=value)text.Text=value;}
        details.Visibility=details.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
        if(previous is not { } old||!ViewChanges.Body(old,device)){
            plugin.Children.Clear();
            if(appearance.Layout!="cards"&&appearance.PluginLayout&&device.TryGetProperty("surface_templates",out var templates)){
                var template=templates.EnumerateArray().FirstOrDefault(t=>t.GetProperty("surface").GetString()==surface&&t.GetProperty("style").GetString() is "native.details.v1" or "mouse.dpi.v1" or "audio.controls.v1");
                if(template.ValueKind==JsonValueKind.Object){
                    string pageId=template.GetProperty("page_id").GetString()!;
                    var page=device.GetProperty("pages").EnumerateArray().FirstOrDefault(p=>p.GetProperty("id").GetString()==pageId);
                    if(page.ValueKind==JsonValueKind.Object){
                        var projected=System.Text.Json.Nodes.JsonNode.Parse(device.GetRawText())!;projected["pages"]=new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonNode.Parse(page.GetRawText()));
                        PluginPage.Render(plugin,JsonSerializer.SerializeToElement(projected));
                    }
                }
            }
        }
        plugin.Visibility=plugin.Children.Count>0?Visibility.Visible:Visibility.Collapsed;previous=device;
        return changed;
    }
    internal static Dictionary<string,string> Labels(JsonElement device){
        var labels=new Dictionary<string,string>();
        void Walk(JsonElement nodes){foreach(var node in nodes.EnumerateArray()){if(node.TryGetProperty("binding",out var binding)&&binding.ValueKind==JsonValueKind.String)labels[binding.GetString()!]=node.GetProperty("label").GetString()!;if(node.TryGetProperty("children",out var children))Walk(children);}}
        foreach(var page in device.GetProperty("pages").EnumerateArray())Walk(page.GetProperty("nodes"));return labels;
    }
}
