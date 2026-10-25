$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('ShowDeviceIconMask' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing.Common,System.Drawing.Primitives @'
using System;
using System.Drawing;
public static class ShowDeviceIconMask {
    public static void Apply(Bitmap image) {
        double radius = image.Width * .25; // Same ratio as MainWindow.RoundTitleIcon.
        for (int y=0;y<image.Height;y++) for (int x=0;x<image.Width;x++) {
            if ((x>=radius && x+1<=image.Width-radius) || (y>=radius && y+1<=image.Height-radius)) continue;
            int covered=0;
            for(int sy=0;sy<8;sy++) for(int sx=0;sx<8;sx++) {
                double px=x+(sx+.5)/8,py=y+(sy+.5)/8;
                double cx=Math.Max(radius,Math.Min(image.Width-radius,px));
                double cy=Math.Max(radius,Math.Min(image.Height-radius,py));
                if ((px-cx)*(px-cx)+(py-cy)*(py-cy)<=radius*radius) covered++;
            }
            var color=image.GetPixel(x,y);
            image.SetPixel(x,y,Color.FromArgb((color.A*covered+32)/64,color.R,color.G,color.B));
        }
    }
}
'@
}
$assetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'ui/assets'
$source = [Drawing.Bitmap]::new((Join-Path $assetDirectory 'showdevice-icon.png'))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, $size, $size)
            [ShowDeviceIconMask]::Apply($bitmap)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Create((Join-Path $assetDirectory 'showdevice-icon.ico'))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = [byte]($sizes[$i] % 256)
            $writer.Write($dimension); $writer.Write($dimension)
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write($frame) }
    } finally { $writer.Dispose(); $file.Dispose() }
} finally { $source.Dispose() }
