using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
namespace ShowDevice;

internal sealed class ResidentShell : IDisposable {
    private readonly AppSession session;
    private readonly MainWindow main;
    private readonly CompactSurface popup;
    private CompactSurface desktop;
    private readonly nint handle;
    private readonly NativeShell.SubclassProc callback;
    private NativeShell.NotifyData icon;
    private readonly uint taskbarCreated=NativeShell.RegisterWindowMessage("TaskbarCreated");
    private readonly DispatcherTimer visibility=new(){Interval=TimeSpan.FromMilliseconds(650)};
    private bool locked,suspended,disposed,iconOwned;
    private string interest="";
    private DateTime retryDesktop;
    private DateTime popupHidden;
    public void ShowTray(){popup.ShowPopup(Anchor());UpdateInterest(force:true);}
    public ResidentShell(AppSession session,MainWindow main){
        this.session=session;this.main=main;handle=WinRT.Interop.WindowNative.GetWindowHandle(main);
        popup=new(session,false);desktop=new(session,true);
        popup.OpenDevice+=Open;desktop.OpenDevice+=Open;
        popup.Hidden+=()=>{popupHidden=DateTime.UtcNow;UpdateInterest();};main.Hidden+=()=>UpdateInterest();
        callback=Message;NativeShell.SetWindowSubclass(handle,callback,1,0);NativeShell.WTSRegisterSessionNotification(handle,0);
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets","showdevice-icon.ico");
        var nativeIcon=NativeShell.LoadImage(0,iconPath,1,32,32,0x10);iconOwned=nativeIcon!=0;
        if(nativeIcon==0)nativeIcon=NativeShell.LoadIcon(0,(nint)32512);
        icon=new(){Size=(uint)Marshal.SizeOf<NativeShell.NotifyData>(),Window=handle,Id=1,Flags=1|2|4|128,Callback=0x8001,Icon=nativeIcon,Tip="ShowDevice",Info="",Title="",Version=4};
        AddIcon();
        session.Snapshot+=Snapshot;session.PreferencesChanged+=Preferences;
        visibility.Tick+=(_,_)=>Tick();visibility.Start();Preferences();
    }
    private void AddIcon(){NativeShell.Shell_NotifyIcon(0,ref icon);NativeShell.Shell_NotifyIcon(4,ref icon);}
    private nint Message(nint hwnd,uint message,nuint wp,nint lp,nuint id,nuint data){
        try{
            if(message==taskbarCreated){AddIcon();retryDesktop=DateTime.MinValue;}
            if(message==0x02B1){if(wp is 7 or 4)locked=true;else if(wp is 8 or 3)locked=false;Tick();}
            if(message==0x218){if(wp==4)suspended=true;else if(wp is 7 or 18)suspended=false;Tick();}
            if(message==0x8001){
                int action=(int)((long)lp&0xffff);
                if(action is 0x400 or 0x401){
                    if(popup.Exposed)popup.Hide();
                    else if((DateTime.UtcNow-popupHidden).TotalMilliseconds>250){popup.ShowPopup(Anchor());UpdateInterest(force:true);}
                }
                if(action==0x7b)Menu();
                return 0;
            }
        }catch(Exception e){System.Diagnostics.Debug.WriteLine(e);}
        return NativeShell.DefSubclassProc(hwnd,message,wp,lp);
    }
    private NativeShell.Rect Anchor(){
        var id=new NativeShell.IconIdentifier{Size=(uint)Marshal.SizeOf<NativeShell.IconIdentifier>(),Window=handle,Id=1};
        if(NativeShell.Shell_NotifyIconGetRect(ref id,out var rect)==0)return rect;
        NativeShell.GetCursorPos(out var point);return new(){Left=point.X,Right=point.X+1,Top=point.Y,Bottom=point.Y+1};
    }
    private void Menu(){
        var menu=NativeShell.CreatePopupMenu();
        try{
            NativeShell.AppendMenu(menu,0,1,L.T("Open ShowDevice"));
            NativeShell.AppendMenu(menu,session.Settings.Surfaces.DesktopEnabled?8u:0,2,L.T("Desktop card"));
            NativeShell.AppendMenu(menu,session.Settings.Surfaces.Locked?8u:0,3,L.T("Lock card position"));
            NativeShell.AppendMenu(menu,0,4,L.T("Refresh devices"));
            NativeShell.AppendMenu(menu,0x800,0,"");NativeShell.AppendMenu(menu,0,5,L.T("Exit"));
            NativeShell.GetCursorPos(out var point);NativeShell.SetForegroundWindow(handle);
            var choice=NativeShell.TrackPopupMenu(menu,0x100|0x2,point.X,point.Y,0,handle,0);
            if(choice==1)Open(null);
            if(choice==2){session.Settings.Surfaces.DesktopEnabled=!session.Settings.Surfaces.DesktopEnabled;session.Settings.Save();Preferences();}
            if(choice==3){session.Settings.Surfaces.Locked=!session.Settings.Surfaces.Locked;session.Settings.Save();}
            if(choice==4){popup.ShowPopup(Anchor());UpdateInterest(force:true);_=session.Send("refresh");}
            if(choice==5)Exit();
        }finally{NativeShell.DestroyMenu(menu);}
    }
    public void Open(string? id=null){popup.Hide();main.ShowDevice(id);UpdateInterest(force:true);}
    private void Preferences(){
        session.Settings.Surfaces.Normalize();
        if(session.Settings.Surfaces.DesktopEnabled){if(!desktop.AttachDesktop())retryDesktop=DateTime.UtcNow.AddSeconds(5);}else desktop.Hide();
        popup.Update();desktop.Update();Tick();
    }
    private void Snapshot(System.Text.Json.JsonElement snapshot){
        popup.Update();desktop.Update();
        var summary=snapshot.GetProperty("devices").EnumerateArray().Take(4).Select(d=>{
            var name=d.GetProperty("device").GetProperty("name").GetString();
            var state=d.GetProperty("state");
            return state.TryGetProperty("battery.percent",out var p)&&p.TryGetProperty("value",out var v)&&v.ValueKind==System.Text.Json.JsonValueKind.Number?$"{name}: {v.GetDouble():0}%":name;
        });
        string text="ShowDevice\n"+string.Join("\n",summary);icon.Tip=text.Length>127?text[..127]:text;NativeShell.Shell_NotifyIcon(1,ref icon);
        UpdateInterest();
    }
    private void Tick(){
        if(disposed)return;
        if(session.Settings.Surfaces.DesktopEnabled&&!desktop.Attached&&DateTime.UtcNow>=retryDesktop){
            if(!NativeShell.IsWindow(desktop.Handle)){desktop=new CompactSurface(session,true);desktop.OpenDevice+=Open;}
            desktop.AttachDesktop();retryDesktop=DateTime.UtcNow.AddSeconds(5);
        }
        desktop.SetExposed(!locked&&!suspended&&session.Settings.Surfaces.DesktopEnabled&&desktop.Attached&&NativeShell.DesktopExposed(desktop.Handle));
        if(locked||suspended)popup.Hide();
        if(desktop.Exposed)desktop.Update();if(popup.Exposed)popup.Update();
        UpdateInterest();
    }
    private void UpdateInterest(bool force=false){
        if(disposed)return;
        bool mainVisible=!locked&&!suspended&&NativeShell.Usable(handle);
        main.SetSurfaceVisible(mainVisible);
        // Only visible surfaces contribute demand; each compact surface has its own selection.
        var (all,ids)=SurfacePolicy.Interest(session.Settings.Surfaces,mainVisible,popup.Exposed,desktop.Exposed);
        string next=$"{all}:{string.Join('|',ids)}:{desktop.Exposed}:{popup.Exposed}:{mainVisible}";
        if(!force&&next==interest)return;interest=next;
        if(Environment.GetEnvironmentVariable("SHOWDEVICE_DIAGNOSTICS")=="1"){
            var path=Path.Combine(Path.GetTempPath(),"showdevice-surfaces.json");
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new{mainVisible,popup=popup.Exposed,desktop=desktop.Exposed,attached=desktop.Attached,desktopHandle=(long)desktop.Handle,parent=(long)NativeShell.GetParent(desktop.Handle),all,ids}));
        }
        _=session.Interest(all,ids);
    }
    public void Exit(){Dispose();session.Dispose();main.Shutdown();Application.Current.Exit();}
    public void Dispose(){
        if(disposed)return;disposed=true;visibility.Stop();session.Snapshot-=Snapshot;session.PreferencesChanged-=Preferences;
        NativeShell.Shell_NotifyIcon(2,ref icon);NativeShell.WTSUnRegisterSessionNotification(handle);NativeShell.RemoveWindowSubclass(handle,callback,1);
        if(iconOwned)NativeShell.DestroyIcon(icon.Icon);popup.Shutdown();desktop.Shutdown();
    }
}
