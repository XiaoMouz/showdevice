using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
namespace ShowDevice;

internal sealed class ChargingGlyph : Grid {
    private readonly Polygon shape=new(){Points=new PointCollection{new(4,0),new(0,7),new(3,7),new(2,12),new(8,4),new(5,4)}};
    public Brush Fill {set=>shape.Fill=value;}
    public ChargingGlyph(double width,double height){
        Width=width;Height=height;
        HorizontalAlignment=HorizontalAlignment.Center;VerticalAlignment=VerticalAlignment.Center;IsHitTestVisible=false;
        // Scale a fixed drawing canvas, never the Polygon's implicit layout bounds.
        var canvas=new Canvas{Width=8,Height=12};canvas.Children.Add(shape);Children.Add(new Viewbox{Child=canvas,Stretch=Stretch.Uniform});
    }
}
