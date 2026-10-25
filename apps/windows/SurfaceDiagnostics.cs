using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
namespace ShowDevice;

// Opt-in native-content capture for development; no screen or third-party windows are captured.
internal static class SurfaceDiagnostics {
    public static async Task Capture(FrameworkElement content,string name){
        if(Environment.GetEnvironmentVariable("SHOWDEVICE_DIAGNOSTICS")!="1")return;
        try{
            await Task.Delay(700);
            var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(content);
            if(bitmap.PixelWidth==0||bitmap.PixelHeight==0)return;
            var pixels=await bitmap.GetPixelsAsync();
            using var stream=File.Open(Path.Combine(Path.GetTempPath(),$"showdevice-{name}.png"),FileMode.Create);
            var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());
            await encoder.FlushAsync();
        }catch(Exception e){System.Diagnostics.Debug.WriteLine(e.Message);}
    }
}
