using System.Text.Json;
namespace ShowDevice;

internal sealed record WindowPreferences {
    public bool CompactList { get; init; }
    public bool Maximized { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public int Width { get; init; }=1080;
    public int Height { get; init; }=740;
    public static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShowDevice","window.json");
    public static WindowPreferences Load(string path){
        try{
            if(!File.Exists(path)||new FileInfo(path).Length>16384)return new();
            return JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(path))??new();
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public void Save(string path){
        try{
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));
            File.Move(path+".tmp",path,true);
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException){System.Diagnostics.Debug.WriteLine(e.Message);}
    }
    public WindowPreferences Fit(int left,int top,int width,int height){
        int w=Math.Clamp(Width,Math.Min(640,width),width),h=Math.Clamp(Height,Math.Min(420,height),height);
        return this with{Width=w,Height=h,X=(int)Math.Clamp((long)(X??left+(width-w)/2),left,(long)left+width-w),Y=(int)Math.Clamp((long)(Y??top+(height-h)/2),top,(long)top+height-h)};
    }
}
