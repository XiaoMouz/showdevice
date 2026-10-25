namespace ShowDevice;
internal static class ThemePolicy {
    public static string Global(string? mode)=>mode is "light" or "dark"?mode:"system";
    public static string Surface(string? mode)=>mode is "system" or "light" or "dark"?mode:"app";
    public static string Resolve(string global,string surface,bool systemDark){
        string mode=surface=="app"?Global(global):Surface(surface);
        return mode=="system"?(systemDark?"dark":"light"):mode;
    }
}
