using Microsoft.UI.Xaml;
namespace ShowDevice;
public partial class App : Application {
    private Window? window;
    private AppSession? session;
    private ResidentShell? shell;
    private Mutex? instance;
    private EventWaitHandle? activation;
    private EventWaitHandle? trayActivation;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        if(UpdateIntegration.RedirectToLauncher()){Exit();return;}
        instance=new Mutex(true,"Local\\ShowDevice.Native",out bool first);
        activation=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ShowDevice.Activate");
        trayActivation=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ShowDevice.Tray");
        if(!first){if(!Environment.GetCommandLineArgs().Contains("--background"))(Environment.GetCommandLineArgs().Contains("--tray")?trayActivation:activation).Set();Exit();return;}
        session=new AppSession(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        var main=new MainWindow(session);window=main;
        if(!Environment.GetCommandLineArgs().Contains("--background"))window.Activate();
        shell=new ResidentShell(session,main);
        if(Environment.GetCommandLineArgs().Contains("--background"))main.AppWindow.Hide();
        if(Environment.GetCommandLineArgs().Contains("--tray")){main.AppWindow.Hide();shell.ShowTray();}
        _=session.Start();
        UpdateIntegration.Begin();
        var dispatcher=main.DispatcherQueue;
        _=Task.Run(()=>{while(activation.WaitOne())dispatcher.TryEnqueue(()=>shell.Open());});
        _=Task.Run(()=>{while(trayActivation.WaitOne())dispatcher.TryEnqueue(()=>shell.ShowTray());});
    }
}

