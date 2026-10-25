using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ShowDevice;

// XAML clipping alone exposes the HWND's default erase color at the rounded edge.
// Give compact windows an alpha backdrop and clear their native backing pixels to alpha zero.
internal sealed class TransparentSurfaceBackdrop : SystemBackdrop {
    private readonly nint window;
    private readonly NativeShell.SubclassProc callback;
    private Windows.UI.Composition.Compositor? compositor;
    private Windows.UI.Composition.CompositionColorBrush? brush;
    public TransparentSurfaceBackdrop(nint window){this.window=window;callback=Message;}
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target,XamlRoot root){
        base.OnTargetConnected(target,root);
        compositor=new Windows.UI.Composition.Compositor();
        brush=compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
        target.SystemBackdrop=brush;
        NativeShell.ConfigureTransparentSurface(window);
        NativeShell.SetWindowSubclass(window,callback,2,0);
        NativeShell.RemoveSurfaceFrame(window);
        NativeShell.ClearSurface(window);
    }
    private nint Message(nint hwnd,uint message,nuint wp,nint lp,nuint id,nuint data){
        // The presenter can restore a dialog frame after construction. Keep all pixels client-owned.
        if(message==0x0083&&wp!=0)return 0; // WM_NCCALCSIZE
        if(message==0x0014&&NativeShell.ClearSurface(hwnd,(nint)wp))return 1; // WM_ERASEBKGND
        if(message==0x031E)NativeShell.ConfigureTransparentSurface(hwnd); // compositor restarted
        return NativeShell.DefSubclassProc(hwnd,message,wp,lp);
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target){
        NativeShell.RemoveWindowSubclass(window,callback,2);
        target.SystemBackdrop=null;
        brush?.Dispose();brush=null;compositor?.Dispose();compositor=null;
        base.OnTargetDisconnected(target);
    }
}
