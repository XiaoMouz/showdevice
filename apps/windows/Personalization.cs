namespace ShowDevice;

internal sealed class BackgroundSpec {
    public string Kind {get;set;}="system";
    public string Color {get;set;}="#EDF3F9";
    public double Opacity {get;set;}=1;
    public string? Image {get;set;}
    public double Tint {get;set;}=.65;
    public void Normalize(){
        Kind=Kind is "color" or "image"?Kind:"system";
        Color=AppearanceRules.Color(Color,"#EDF3F9");
        Opacity=double.IsFinite(Opacity)?Math.Clamp(Opacity,0,1):1;
        Tint=double.IsFinite(Tint)?Math.Clamp(Tint,0,1):.65;
        if(Image?.Length>4096)Image=null;
    }
}
internal sealed class SurfaceAppearance {
    public string Theme {get;set;}="app";
    public string Layout {get;set;}="list";
    public string Indicator {get;set;}="icon";
    public bool PluginLayout {get;set;}=true;
    public string LowColor {get;set;}="#C43838";
    public string MediumColor {get;set;}="#A96F00";
    public string HighColor {get;set;}="#1A8F62";
    public int LowAt {get;set;}=20;
    public int MediumAt {get;set;}=40;
    public BackgroundSpec Background {get;set;}=new();
    public BackgroundSpec DeviceBackground {get;set;}=new();
    public Dictionary<string,BackgroundSpec> DeviceBackgrounds {get;set;}=new();
    public Dictionary<string,List<string>> Fields {get;set;}=new();
    public void Normalize(){
        Theme=ThemePolicy.Surface(Theme);
        Layout=Layout is "cards" or "details"?Layout:"list";
        Indicator=Indicator is "line" or "ring"?Indicator:"icon";
        LowColor=AppearanceRules.Color(LowColor,"#C43838");MediumColor=AppearanceRules.Color(MediumColor,"#A96F00");HighColor=AppearanceRules.Color(HighColor,"#1A8F62");
        LowAt=Math.Clamp(LowAt,0,98);MediumAt=Math.Clamp(MediumAt,LowAt+1,99);
        Background??=new();Background.Normalize();DeviceBackground??=new();DeviceBackground.Normalize();
        DeviceBackgrounds=AppearanceRules.Backgrounds(DeviceBackgrounds);
        Fields=(Fields??new()).Where(p=>p.Key.Length<=1024).Take(64).ToDictionary(p=>p.Key,p=>(p.Value??new()).Where(f=>f.Length<=96).Distinct().Take(12).ToList());
    }
    public BackgroundSpec ForDevice(string id)=>DeviceBackgrounds.GetValueOrDefault(id,DeviceBackground);
}
internal sealed class Personalization {
    public int Version {get;set;}=1;
    public BackgroundSpec MainBackground {get;set;}=new();
    public BackgroundSpec DeviceBackground {get;set;}=new();
    public Dictionary<string,BackgroundSpec> DeviceBackgrounds {get;set;}=new();
    public SurfaceAppearance Desktop {get;set;}=new();
    public SurfaceAppearance Tray {get;set;}=new();
    public void Normalize(){
        MainBackground??=new();MainBackground.Normalize();DeviceBackground??=new();DeviceBackground.Normalize();
        DeviceBackgrounds=AppearanceRules.Backgrounds(DeviceBackgrounds);
        Desktop??=new();Desktop.Normalize();Tray??=new();Tray.Normalize();
    }
}
internal static class AppearanceRules {
    public static string Color(string? value,string fallback)=>value is {Length:7}&&value[0]=='#'&&value[1..].All(Uri.IsHexDigit)?value.ToUpperInvariant():fallback;
    public static Dictionary<string,BackgroundSpec> Backgrounds(Dictionary<string,BackgroundSpec>? values){
        var result=(values??new()).Where(p=>p.Key.Length<=1024&&p.Value!=null).Take(64).ToDictionary();foreach(var item in result.Values)item.Normalize();return result;
    }
    public static string BatteryColor(SurfaceAppearance style,double percent,bool dark=false){
        var color=percent<=style.LowAt?style.LowColor:percent<=style.MediumAt?style.MediumColor:style.HighColor;
        if(!dark)return color;
        // Keep configured colors unchanged on disk; lift lightness only for dark surfaces.
        if(color=="#C43838")return "#FF727A";
        if(color=="#A96F00")return "#F5BE4F";
        if(color=="#1A8F62")return "#45D99C";
        double r=Convert.ToByte(color.Substring(1,2),16)/255d,g=Convert.ToByte(color.Substring(3,2),16)/255d,b=Convert.ToByte(color.Substring(5,2),16)/255d;
        double min=Math.Min(r,Math.Min(g,b)),max=Math.Max(r,Math.Max(g,b)),l=(min+max)/2;
        if(l>=.65)return color;
        double saturation=max==min?0:(max-min)/(1-Math.Abs(2*l-1));
        double chroma=.7*saturation,low=.65-chroma/2;
        int Lift(double channel)=>(int)Math.Round(255*(max==min?.65:low+(channel-min)/(max-min)*chroma));
        return $"#{Lift(r):X2}{Lift(g):X2}{Lift(b):X2}";
    }
    public static (double X,double Y) RingPoint(double percent){double angle=(105+330*Math.Clamp(percent,0,100)/100)*Math.PI/180;return(12+9*Math.Cos(angle),12+9*Math.Sin(angle));}
}
