using System.Runtime.InteropServices;
using System.Text;
namespace ShowDevice;

internal static class NativeShell {
    internal delegate bool EnumProc(nint window,nint param);
    internal delegate nint SubclassProc(nint window,uint message,nuint wp,nint lp,nuint id,nuint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect {public int Left,Top,Right,Bottom; public SurfaceRect Bounds=>new(Left,Top,Right,Bottom);}
    [StructLayout(LayoutKind.Sequential)] internal struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct NotifyData {
        public uint Size;public nint Window;public uint Id,Flags,Callback;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string Title;
        public uint InfoFlags;public Guid Guid;public nint Balloon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct IconIdentifier {public uint Size;public nint Window;public uint Id;public Guid Guid;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern nint FindWindow(string cls,string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent,nint after,string cls,string? name);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback,nint param);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern uint GetMessagePos();
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd,ref Point point);
    [DllImport("user32.dll")] internal static extern nint GetWindowLongPtrW(nint hwnd,int index);
    [DllImport("user32.dll")] internal static extern nint SetWindowLongPtrW(nint hwnd,int index,nint value);
    [DllImport("user32.dll",SetLastError=true)] internal static extern nint SetParent(nint hwnd,nint parent);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd,int command);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] internal static extern nint SendMessageTimeout(nint hwnd,uint message,nuint wp,nint lp,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll")] internal static extern nint CreateIconFromResourceEx(byte[] bytes,uint size,bool icon,uint version,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] internal static extern nint LoadIcon(nint instance,nint name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern nint LoadImage(nint instance,string name,uint type,int width,int height,uint flags);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] internal static extern bool Shell_NotifyIcon(uint message,ref NotifyData data);
    [DllImport("shell32.dll")] internal static extern int Shell_NotifyIconGetRect(ref IconIdentifier id,out Rect rect);
    [DllImport("comctl32.dll")] internal static extern bool SetWindowSubclass(nint hwnd,SubclassProc callback,nuint id,nuint data);
    [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(nint hwnd,SubclassProc callback,nuint id);
    [DllImport("comctl32.dll")] internal static extern nint DefSubclassProc(nint hwnd,uint message,nuint wp,nint lp);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd,int attribute,out int value,int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [StructLayout(LayoutKind.Sequential)] private struct Margins {public int Left,Right,Top,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind {public uint Flags;public int Enable;public nint Region;public int Transition;}
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd,ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd,ref BlurBehind blur);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc,ref Rect rect,nint brush);
    public static void ConfigureTransparentSurface(nint hwnd){
        var margins=new Margins();DwmExtendFrameIntoClientArea(hwnd,ref margins);
        var region=CreateRectRgn(-2,-2,-1,-1);
        try{var blur=new BlurBehind{Flags=3,Enable=1,Region=region};DwmEnableBlurBehindWindow(hwnd,ref blur);}
        finally{if(region!=0)DeleteObject(region);}
    }
    public static bool ClearSurface(nint hwnd,nint dc=default){
        if(!GetClientRect(hwnd,out var rect))return false;
        bool borrowed=dc!=0;if(!borrowed)dc=GetDC(hwnd);if(dc==0)return false;
        try{return FillRect(dc,ref rect,GetStockObject(4))!=0;} // BLACK_BRUSH clears backing alpha.
        finally{if(!borrowed)ReleaseDC(hwnd,dc);}
    }
    public static void RemoveSurfaceFrame(nint hwnd){
        var style=GetWindowLongPtrW(hwnd,-16).ToInt64();
        SetWindowLongPtrW(hwnd,-16,(nint)(style&~0x00C40000L)); // caption/dialog frame/thick resize frame
        var extended=GetWindowLongPtrW(hwnd,-20).ToInt64();
        SetWindowLongPtrW(hwnd,-20,(nint)(extended&~0x00020301L)); // modal, window, client and static edges
        SetWindowPos(hwnd,0,0,0,0,0,0x37); // recompute non-client area, no move/resize/activation
    }
    [DllImport("user32.dll")] internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool AppendMenu(nint menu,uint flags,nuint id,string text);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint owner,nint rect);
    [DllImport("user32.dll")] internal static extern bool DestroyMenu(nint menu);
    [DllImport("wtsapi32.dll")] internal static extern bool WTSRegisterSessionNotification(nint hwnd,uint flags);
    [DllImport("wtsapi32.dll")] internal static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    public static string Class(nint hwnd){var text=new StringBuilder(256);GetClassName(hwnd,text,256);return text.ToString();}
    public static bool Usable(nint hwnd)=>IsWindowVisible(hwnd)&&!IsIconic(hwnd)&&(DwmGetWindowAttribute(hwnd,14,out int cloaked,4)!=0||cloaked==0);
    public static void ToolWindow(nint hwnd){var ex=GetWindowLongPtrW(hwnd,-20).ToInt64();SetWindowLongPtrW(hwnd,-20,(nint)((ex|0x80)&~0x40000));}
    public static nint DesktopHost(){
        // Wallpaper WorkerW sits behind the full-screen DefView input surface.
        // A child of DefView stays on the desktop but can receive mouse input above its icon list.
        var progman=FindWindow("Progman",null);
        var host=progman==0?0:FindWindowEx(progman,0,"SHELLDLL_DefView",null);
        if(host!=0)return host;
        EnumWindows((w,_)=>{
            host=FindWindowEx(w,0,"SHELLDLL_DefView",null);return host==0;
        },0);
        return host;
    }
    public static bool DesktopExposed(nint hwnd){
        if(!Usable(hwnd)||!GetWindowRect(hwnd,out var rect))return false;
        var covers=new List<SurfaceRect>();
        EnumWindows((w,_)=>{
            if(w==hwnd||!Usable(w))return true;
            var cls=Class(w);if(cls is "Progman" or "WorkerW")return true;
            if(GetWindowRect(w,out var r)&&!r.Bounds.Empty)covers.Add(r.Bounds);
            return true;
        },0);
        return SurfacePolicy.Exposed(rect.Bounds,covers);
    }
}
