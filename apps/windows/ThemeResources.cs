using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
namespace ShowDevice;

// Clone programmatic theme brushes: two native windows may intentionally use opposite themes.
// XAML palettes resolve the OS's actual theme/high-contrast colors; custom user colors stay intact.
internal sealed class ThemeResources {
    private sealed record Key(string? Name, Windows.UI.Color Light=default, Windows.UI.Color Dark=default,bool Opaque=false);
    private static readonly DependencyProperty BrushKeyProperty=DependencyProperty.RegisterAttached("BrushKey",typeof(object),typeof(ThemeResources),new PropertyMetadata(null));
    private static readonly DependencyProperty ObserverProperty=DependencyProperty.RegisterAttached("Observer",typeof(object),typeof(ThemeResources),new PropertyMetadata(null));
    private static long generation;
    private long seen=-1;
    private bool queued;
    private readonly Grid root;
    private readonly ThemePalette light=new(){RequestedTheme=ElementTheme.Light,Width=0,Height=0,IsHitTestVisible=false,Opacity=0};
    private readonly ThemePalette dark=new(){RequestedTheme=ElementTheme.Dark,Width=0,Height=0,IsHitTestVisible=false,Opacity=0};
    public ThemeResources(Grid root){
        this.root=root;root.Children.Add(light);root.Children.Add(dark);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(light,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(dark,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        root.LayoutUpdated+=(_,_)=>{if(seen!=generation)Refresh();};
        root.ActualThemeChanged+=(_,_)=>QueueRefresh();
        light.Loaded+=(_,_)=>QueueRefresh();dark.Loaded+=(_,_)=>QueueRefresh();
        light.ActualThemeChanged+=(_,_)=>QueueRefresh();dark.ActualThemeChanged+=(_,_)=>QueueRefresh();
        root.Loaded+=(_,_)=>Refresh();
    }
    public static Brush Resource(string key,bool opaque=false){
        var original=(Brush)Application.Current.Resources[key];
        if(original is not SolidColorBrush color)return original;
        var fill=color.Color;if(opaque)fill.A=255;
        var copy=new SolidColorBrush(fill){Opacity=opaque?1:color.Opacity};copy.SetValue(BrushKeyProperty,new Key(key,Opaque:opaque));generation++;return copy;
    }
    public static Brush Adaptive(Windows.UI.Color light,Windows.UI.Color dark){
        var brush=new SolidColorBrush(light);brush.SetValue(BrushKeyProperty,new Key(null,light,dark));generation++;return brush;
    }
    private void QueueRefresh(){if(queued)return;queued=true;root.DispatcherQueue.TryEnqueue(()=>{queued=false;Refresh();});}
    public void Refresh(){
        seen=generation;
        bool contrast=new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        void Paint(Brush? brush,ElementTheme theme){
            if(brush is not SolidColorBrush color||color.GetValue(BrushKeyProperty) is not Key key)return;
            if(key.Name==null&&!contrast){color.Color=theme==ElementTheme.Dark?key.Dark:key.Light;return;}
            var resolved=(theme==ElementTheme.Dark?dark:light).Brush(key.Name??"TextFillColorPrimaryBrush");
            var fill=resolved.Color;if(key.Opaque)fill.A=255;color.Color=fill;color.Opacity=key.Opaque?1:resolved.Opacity;
        }
        var visited=new HashSet<DependencyObject>();
        void Walk(DependencyObject node,ElementTheme inherited){
            if(!visited.Add(node)||ReferenceEquals(node,light)||ReferenceEquals(node,dark))return;
            var theme=inherited;
            if(node is FrameworkElement element){
                if(element.RequestedTheme!=ElementTheme.Default)theme=element.RequestedTheme;
                if(!ReferenceEquals(element.GetValue(ObserverProperty),this)){
                    element.SetValue(ObserverProperty,this);element.Loaded+=(_,_)=>QueueRefresh();element.ActualThemeChanged+=(_,_)=>QueueRefresh();
                }
            }
            if(node is Panel panel)Paint(panel.Background,theme);
            if(node is Grid grid)Paint(grid.BorderBrush,theme);
            if(node is Border border){Paint(border.Background,theme);Paint(border.BorderBrush,theme);}
            if(node is Control control){Paint(control.Foreground,theme);Paint(control.Background,theme);Paint(control.BorderBrush,theme);}
            if(node is TextBlock text)Paint(text.Foreground,theme);
            if(node is IconElement icon)Paint(icon.Foreground,theme);
            if(node is Shape shape){Paint(shape.Fill,theme);Paint(shape.Stroke,theme);}
            // ScrollViewer and virtualized items can own content before it enters the visual tree.
            // Resolve these brushes from the declared ancestor theme, then recheck on realization.
            if(node is Panel children)foreach(var child in children.Children)Walk(child,theme);
            if(node is Border {Child:{} childBorder})Walk(childBorder,theme);
            if(node is ContentControl {Content:DependencyObject childContent})Walk(childContent,theme);
            if(node is ContentPresenter {Content:DependencyObject presented})Walk(presented,theme);
            if(node is ItemsControl items)foreach(var item in items.Items)if(item is DependencyObject childItem)Walk(childItem,theme);
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Walk(VisualTreeHelper.GetChild(node,i),theme);
        }
        Walk(root,root.ActualTheme);
    }
}
