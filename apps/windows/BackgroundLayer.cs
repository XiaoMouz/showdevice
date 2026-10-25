using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
namespace ShowDevice;

internal sealed class BackgroundLayer : Grid {
    private readonly ImageBrush artwork=new(){Stretch=Stretch.UniformToFill};
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle image=new(){IsHitTestVisible=false};
    private readonly Border tint=new(){IsHitTestVisible=false};
    private string signature="";
    private BackgroundSpec spec=new();
    private readonly bool transparentSystem;
    private readonly FrameworkElement? themedContent;
    public ElementTheme ContentTheme=>themedContent?.ActualTheme??ActualTheme;
    public static Windows.UI.Color Color(string hex)=>Windows.UI.Color.FromArgb(255,Convert.ToByte(hex.Substring(1,2),16),Convert.ToByte(hex.Substring(3,2),16),Convert.ToByte(hex.Substring(5,2),16));
    public BackgroundLayer(UIElement content,double radius=12,bool transparentSystem=false){
        this.transparentSystem=transparentSystem;
        themedContent=content as FrameworkElement;
        var canvas=new Canvas{IsHitTestVisible=false};canvas.Children.Add(image);
        CornerRadius=tint.CornerRadius=new(radius);image.RadiusX=image.RadiusY=radius;image.Fill=artwork;
        Children.Add(canvas);Children.Add(tint);Children.Add(content);
        // Rounded fills retain fractional edge coverage; a geometric clip cuts those pixels off.
        SizeChanged+=(_,e)=>{image.Width=e.NewSize.Width;image.Height=e.NewSize.Height;Clip=new RectangleGeometry{Rect=new(0,0,e.NewSize.Width,e.NewSize.Height)};};
        ActualThemeChanged+=(_,_)=>Apply(spec,true);
    }
    public void Apply(BackgroundSpec value,bool force=false){
        spec=value;string next=System.Text.Json.JsonSerializer.Serialize(value);
        if(!force&&signature==next)return;signature=next;
        bool contrast=new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        if(themedContent!=null){
            var color=Color(value.Color);
            double baseBrightness=ActualTheme==ElementTheme.Dark?39:250;
            double brightness=(.2126*color.R+.7152*color.G+.0722*color.B)*value.Opacity+baseBrightness*(1-value.Opacity);
            themedContent.RequestedTheme=value.Kind=="color"&&value.Opacity>0&&!contrast
                ?(brightness>140?ElementTheme.Light:ElementTheme.Dark)
                :ElementTheme.Default;
        }
        Background=DeviceVisuals.Resource("LayerFillColorDefaultBrush");
        tint.Background=value.Kind=="color"&&!contrast?new SolidColorBrush(Color(value.Color)):ThemeResources.Resource("LayerFillColorDefaultBrush",opaque:value.Kind=="image"&&!contrast);
        tint.Opacity=1;artwork.ImageSource=null;
        if(value.Kind=="color"&&!contrast){Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);tint.Opacity=value.Opacity;return;}
        if(value.Kind=="system"&&transparentSystem&&!contrast){Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);tint.Opacity=0;return;}
        if(value.Kind=="image"&&!contrast&&value.Image is { } path){
            try{
                var uri=new Uri(path,UriKind.Absolute);
                if(uri.IsFile&&!uri.IsUnc&&File.Exists(path)&&new FileInfo(path).Length<=20*1024*1024){
                    var bitmap=new BitmapImage{DecodePixelWidth=1536};
                    bitmap.ImageFailed+=(_,_)=>{if(ReferenceEquals(artwork.ImageSource,bitmap)){artwork.ImageSource=null;tint.Opacity=1;}};
                    artwork.ImageSource=bitmap;bitmap.UriSource=uri;tint.Opacity=value.Tint;
                }
            }catch(Exception e) when(e is IOException or UriFormatException or UnauthorizedAccessException){System.Diagnostics.Debug.WriteLine(e.Message);}
        }
    }
}
