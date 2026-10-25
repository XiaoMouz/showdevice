namespace ShowDevice;

// Versioned, local app feature preferences. Unknown styles fall back to the native default.
internal sealed class SurfacePreferences {
    public int Version {get;set;}=1;
    public string Style {get;set;}="native.compact.v1";
    public bool DesktopEnabled {get;set;}=true;
    public bool Locked {get;set;}=false;
    public int? X {get;set;}
    public int? Y {get;set;}
    public int? TrayX {get;set;}
    public int? TrayY {get;set;}
    public List<string> Devices {get;set;}=new();
    public bool AllDevices {get;set;}=true;
    public bool TrayAllDevices {get;set;}=true;
    public List<string> TrayDevices {get;set;}=new();
    public List<string> MainOrder {get;set;}=new();
    public List<string> TrayOrder {get;set;}=new();
    public List<string> DesktopOrder {get;set;}=new();
    public double Width {get;set;}=320;
    public int TransitionMs {get;set;}=420;
    public void Normalize(){
        Width=double.IsFinite(Width)?Math.Clamp(Width,260,480):320;
        TransitionMs=Math.Clamp(TransitionMs,0,1000);
        Devices=NormalizeIds(Devices);TrayDevices=NormalizeIds(TrayDevices);
        MainOrder=NormalizeIds(MainOrder);TrayOrder=NormalizeIds(TrayOrder);DesktopOrder=NormalizeIds(DesktopOrder);
    }
    private static List<string> NormalizeIds(List<string>? ids)=>(ids??new()).Where(id=>!string.IsNullOrEmpty(id)&&id.Length<=1024).Distinct().Take(256).ToList();
}
internal readonly record struct SurfaceRect(int Left,int Top,int Right,int Bottom){
    public bool Empty=>Right<=Left||Bottom<=Top;
    public IEnumerable<SurfaceRect> Subtract(SurfaceRect cover){
        int l=Math.Max(Left,cover.Left),t=Math.Max(Top,cover.Top),r=Math.Min(Right,cover.Right),b=Math.Min(Bottom,cover.Bottom);
        if(r<=l||b<=t){yield return this;yield break;}
        if(t>Top)yield return new(Left,Top,Right,t);
        if(b<Bottom)yield return new(Left,b,Right,Bottom);
        if(l>Left)yield return new(Left,t,l,b);
        if(r<Right)yield return new(r,t,Right,b);
    }
}
internal static class SurfacePolicy {
    public static (int X,int Y) DragPosition(int windowX,int windowY,int startX,int startY,int pointerX,int pointerY)=>(windowX+pointerX-startX,windowY+pointerY-startY);
    public static bool Visible(SurfacePreferences prefs,bool desktop,string id)=>desktop?prefs.AllDevices||prefs.Devices.Contains(id):prefs.TrayAllDevices||prefs.TrayDevices.Contains(id);
    public static IEnumerable<T> Ordered<T>(IEnumerable<T> items,IEnumerable<string> order,Func<T,string> id,Func<T,string> name){
        var rank=order.Distinct().Select((key,index)=>(key,index)).ToDictionary(p=>p.key,p=>p.index);
        return items.OrderBy(item=>rank.GetValueOrDefault(id(item),int.MaxValue)).ThenBy(name,StringComparer.OrdinalIgnoreCase).ThenBy(id,StringComparer.Ordinal);
    }
    public static (bool All,string[] Ids) Interest(SurfacePreferences prefs,bool main,bool tray,bool desktop){
        bool all=main||(tray&&prefs.TrayAllDevices)||(desktop&&prefs.AllDevices);
        var ids=(tray?prefs.TrayDevices:Enumerable.Empty<string>()).Concat(desktop?prefs.Devices:Enumerable.Empty<string>()).Distinct().Order(StringComparer.Ordinal).ToArray();
        return(all,all?Array.Empty<string>():ids);
    }
    public static (int X,int Y) FitPosition(int x,int y,int width,int height,SurfaceRect area)=>(
        Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-width)),
        Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-height)));
    public static bool Exposed(SurfaceRect bounds,IEnumerable<SurfaceRect> covers){
        var remaining=new List<SurfaceRect>{bounds};
        foreach(var cover in covers){remaining=remaining.SelectMany(r=>r.Subtract(cover)).ToList();if(remaining.Count==0)return false;if(remaining.Count>1024)return false;}
        return remaining.Any(r=>!r.Empty);
    }
    public static double Interpolate(double start,double end,double elapsed,double duration){
        var t=duration<=0?1:Math.Clamp(elapsed/duration,0,1);
        return start+(end-start)*(1-Math.Pow(1-t,3));
    }
}
