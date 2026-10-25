using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace ShowDevice;
public sealed partial class ThemePalette : UserControl {
    public ThemePalette(){InitializeComponent();}
    internal SolidColorBrush Brush(string key)=>(SolidColorBrush)(key switch {
        "TextFillColorPrimaryBrush"=>Primary.Background,"TextFillColorSecondaryBrush"=>Secondary.Background,
        "LayerFillColorDefaultBrush"=>Layer.Background,"CardBackgroundFillColorDefaultBrush"=>Card.Background,
        "CardStrokeColorDefaultBrush"=>Stroke.Background,"SystemFillColorCautionBrush"=>Caution.Background,
        _=>Primary.Background
    });
}
