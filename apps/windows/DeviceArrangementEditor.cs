using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
namespace ShowDevice;

internal static class DeviceArrangementEditor {
    public static FrameworkElement Create(AppSettings draft,Action commit){
        var panel=new StackPanel{Spacing=8};
        panel.Children.Add(SettingsPage.Heading("Device visibility and order"));
        var scope=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var label in new[]{"Main window","Tray popup","Desktop card"})scope.Items.Add(L.T(label));
        scope.SelectedIndex=0;
        panel.Children.Add(SettingsPage.Panel(SettingsPage.Field("Display area",scope,SettingsPage.SettingIcon("\uE7F4"))));
        var all=new ToggleSwitch{OnContent="",OffContent="",MinWidth=0};
        var allCard=SettingsPage.Panel(SettingsPage.Field("Show all devices",all,SettingsPage.SettingIcon("\uE8FD")));panel.Children.Add(allCard);
        var list=new ListView{CanDragItems=true,CanReorderItems=true,AllowDrop=true,SelectionMode=ListViewSelectionMode.Single,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(0)};
        AutomationProperties.SetName(list,L.T("Device order"));
        var rows=new StackPanel();rows.Children.Add(list);panel.Children.Add(SettingsPage.Panel(rows));
        var empty=new TextBlock{Text=L.T("Connect a supported device"),TextWrapping=TextWrapping.Wrap,Margin=new(36,12,0,12)};rows.Children.Add(empty);
        var checks=new Dictionary<string,CheckBox>();
        var arrows=new Dictionary<string,(Button Up,Button Down)>();bool building=false;
        var prefs=draft.Surfaces;
        List<string> Order()=>scope.SelectedIndex switch{1=>prefs.TrayOrder,2=>prefs.DesktopOrder,_=>prefs.MainOrder};
        List<string> Picks()=>scope.SelectedIndex==1?prefs.TrayDevices:prefs.Devices;
        bool All()=>scope.SelectedIndex==1?prefs.TrayAllDevices:prefs.AllDevices;
        void SetAll(bool value){if(scope.SelectedIndex==1)prefs.TrayAllDevices=value;else prefs.AllDevices=value;}
        void UpdateChecks(){
            building=true;all.IsOn=All();
            foreach(var (id,check) in checks)check.IsChecked=All()||Picks().Contains(id);
            building=false;
        }
        void UpdateArrows(){
            for(int i=0;i<list.Items.Count;i++){
                var id=(string)((ListViewItem)list.Items[i]).Tag;var buttons=arrows[id];buttons.Up.IsEnabled=i>0;buttons.Down.IsEnabled=i<list.Items.Count-1;
            }
        }
        void SaveOrder(){
            var ids=list.Items.Cast<ListViewItem>().Select(item=>(string)item.Tag).ToList();
            var order=Order();var missing=order.Except(ids).ToArray();order.Clear();order.AddRange(ids);order.AddRange(missing);
            UpdateArrows();commit();
        }
        void Move(string id,int direction){
            var item=list.Items.Cast<ListViewItem>().First(row=>(string)row.Tag==id);int index=list.Items.IndexOf(item),next=index+direction;
            if(next<0||next>=list.Items.Count)return;
            list.Items.RemoveAt(index);list.Items.Insert(next,item);list.SelectedItem=item;SaveOrder();
        }
        Button Arrow(string id,string glyph,string label,int direction){
            var button=new Button{Content=new FontIcon{Glyph=glyph,FontFamily=new("Segoe Fluent Icons"),FontSize=12},Width=28,Height=28,MinWidth=0,MinHeight=0,Padding=new(0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),BorderThickness=new(0)};
            AutomationProperties.SetName(button,draft.KnownDevices[id]+" · "+L.T(label));ToolTipService.SetToolTip(button,L.T(label));button.Click+=(_,_)=>Move(id,direction);return button;
        }
        void Build(){
            building=true;list.Items.Clear();checks.Clear();arrows.Clear();allCard.Visibility=scope.SelectedIndex==0?Visibility.Collapsed:Visibility.Visible;all.IsOn=All();
            foreach(var (id,name) in SurfacePolicy.Ordered(draft.KnownDevices,Order(),pair=>pair.Key,pair=>pair.Value)){
                var row=new Grid{ColumnSpacing=12,MinHeight=48};
                row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(24)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                if(scope.SelectedIndex!=0){
                    var check=new CheckBox{IsChecked=All()||Picks().Contains(id),MinWidth=0,MinHeight=32,Padding=new(0),IsEnabled=!draft.ExcludedDevices.Contains(id)};
                    AutomationProperties.SetName(check,name+" · "+L.T("Show device"));checks[id]=check;row.Children.Add(check);
                    if(!check.IsEnabled)ToolTipService.SetToolTip(check,L.T("Excluded"));
                    void Changed(){
                        if(building)return;
                        if(All()){Picks().Clear();Picks().AddRange(draft.KnownDevices.Keys);SetAll(false);}
                        if(check.IsChecked==true){if(!Picks().Contains(id))Picks().Add(id);}else Picks().Remove(id);
                        UpdateChecks();commit();
                    }
                    check.Checked+=(_,_)=>Changed();check.Unchecked+=(_,_)=>Changed();
                }
                var kind=draft.DeviceKinds.GetValueOrDefault(id,"other");var icon=DeviceVisuals.Icon(kind,20);icon.Foreground=DeviceVisuals.Accent(kind);row.Children.Add(icon);Grid.SetColumn(icon,1);
                var title=new TextBlock{Text=name,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};row.Children.Add(title);Grid.SetColumn(title,2);
                var up=Arrow(id,"\uE70E","Move up",-1);var down=Arrow(id,"\uE70D","Move down",1);arrows[id]=(up,down);row.Children.Add(up);Grid.SetColumn(up,3);row.Children.Add(down);Grid.SetColumn(down,4);
                if(scope.SelectedIndex==0){row.ColumnDefinitions.RemoveAt(0);Grid.SetColumn(icon,0);Grid.SetColumn(title,1);Grid.SetColumn(up,2);Grid.SetColumn(down,3);}
                var item=new ListViewItem{Content=row,Tag=id,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(0)};AutomationProperties.SetName(item,name);list.Items.Add(item);
            }
            empty.Visibility=list.Items.Count==0?Visibility.Visible:Visibility.Collapsed;UpdateArrows();building=false;
        }
        all.Toggled+=(_,_)=>{if(building)return;SetAll(all.IsOn);UpdateChecks();commit();};
        list.DragItemsCompleted+=(_,_)=>{if(!building)SaveOrder();};
        scope.SelectionChanged+=(_,_)=>Build();Build();return panel;
    }
}
