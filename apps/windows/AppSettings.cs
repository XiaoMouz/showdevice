using System.Text.Json;
namespace ShowDevice;
internal sealed class AppSettings {
    public string Language {get;set;}="zh";
    public string Theme {get;set;}="system";
    public SurfacePreferences Surfaces {get;set;}=new();
    public Personalization Personalization {get;set;}=new();
    public int IntervalSeconds {get;set;}=30;
    public Dictionary<string,int> DeviceIntervals {get;set;}=new();
    public HashSet<string> ExcludedDevices {get;set;}=new();
    public Dictionary<string,string> KnownDevices {get;set;}=new();
    public Dictionary<string,string> DeviceKinds {get;set;}=new();
    private static string PathName=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShowDevice","settings.json");
    public static AppSettings Load(string? path=null){
        path??=PathName;
        try{
            if(!File.Exists(path)||new FileInfo(path).Length>65536)return new();
            var value=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))??new();
            value.Language=value.Language=="en"?"en":"zh";
            value.Theme=ThemePolicy.Global(value.Theme);
            value.Surfaces??=new();value.Surfaces.Normalize();
            value.Personalization??=new();value.Personalization.Normalize();
            value.IntervalSeconds=Math.Clamp(value.IntervalSeconds,5,3600);
            value.DeviceIntervals=(value.DeviceIntervals??new()).Where(p=>p.Value>=5&&p.Value<=3600).Take(256).ToDictionary();
            value.ExcludedDevices=(value.ExcludedDevices??new()).Take(256).ToHashSet();
            value.KnownDevices=(value.KnownDevices??new()).Take(256).ToDictionary();
            value.DeviceKinds=(value.DeviceKinds??new()).Where(p=>value.KnownDevices.ContainsKey(p.Key)).Take(256).ToDictionary();
            return value;
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public void Save(string? path=null){
        path??=PathName;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));File.Move(path+".tmp",path,true);
    }
    // Wire names are defined by core/api PollPreferences.
    public object PollPreferences()=>new {interval_seconds=IntervalSeconds,device_intervals=DeviceIntervals,excluded_devices=ExcludedDevices};
}
internal static class L {
    public static string Language {get;set;}="zh";
    public static string T(string english)=>Language=="en"?english:Translations.GetValueOrDefault(english,english);
    private static readonly Dictionary<string,string> Translations=new(){
        ["Theme"]="深浅色模式",["Follow system"]="跟随系统",["Follow app"]="跟随应用",["Light"]="浅色",["Dark"]="深色",
        ["Background color"]="背景颜色",["Image dimensions are too large."]="图片尺寸过大，请缩小后重试。",
        ["Personalization"]="个性化",
        ["Main window"]="主界面",
        ["Tray popup"]="托盘浮窗",
        ["Layout"]="布局",
        ["List"]="列表",
        ["Cards"]="卡片",
        ["Detailed list"]="详细列表",
        ["Battery indicator"]="电量指示",
        ["Battery icon"]="电池图标",
        ["Top line"]="顶部细线",
        ["Open ring"]="开口圆环",
        ["Plugin layout"]="使用插件专属布局",
        ["Battery colors"]="电量颜色",
        ["Low battery"]="低电量",
        ["Medium battery"]="中等电量",
        ["High battery"]="充足电量",
        ["Low threshold (%)"]="低电量阈值（%）",
        ["Medium threshold (%)"]="中等电量阈值（%）",
        ["Displayed parameters"]="显示参数",
        ["Window background"]="主界面背景",
        ["Surface background"]="组件背景",
        ["Device panel background"]="设备面板背景",
        ["Per-device background"]="单独设备背景",
        ["Choose device"]="选择设备",
        ["Use a separate background"]="使用独立背景",
        ["System background"]="系统背景",
        ["Solid color"]="纯色",
        ["Local image"]="本地图片",
        ["Choose image"]="选择图片",
        ["Image overlay"]="图片遮罩",
        ["Background transparency"]="背景透明度",
        ["Device visibility and order"]="设备显示与排序",["Device order"]="设备顺序",
        ["Show all devices"]="显示所有设备",["Move up"]="上移",["Move down"]="下移",["No devices selected"]="未选择设备",
        ["Image must be smaller than 20 MB."]="图片大小需小于 20 MB。",
        ["Unable to apply appearance."]="暂时无法应用外观。",
        ["Open ShowDevice"]="打开 ShowDevice",["Desktop card"]="桌面卡片",["Lock card position"]="锁定卡片位置",["Exit"]="退出",
        ["Tray and desktop"]="托盘与桌面",["Card width"]="卡片宽度",["Battery transition (ms)"]="电量过渡时长（毫秒）",
        ["All devices on desktop"]="桌面显示所有设备",
        ["Devices"]="设备",["Settings"]="设置",["Save"]="保存",["Cancel"]="取消",["Language"]="语言",
        ["Refresh interval (seconds)"]="刷新间隔（秒）",["Per-device settings"]="设备单独设置",["Use global interval"]="使用全局间隔",["Exclude device"]="排除此设备",
        ["Refresh devices"]="刷新设备",["Connected devices"]="已连接设备",["Loading…"]="正在加载…",["Reading unavailable"]="暂时无法读取",
        ["Connect a supported device"]="连接支持的设备",["Connect a receiver or USB cable, then refresh."]="连接接收器或 USB 线后刷新。",["Unable to read devices"]="无法读取设备",
        ["Keyboard"]="键盘",["Mouse"]="鼠标",["Headset"]="耳机",["Device"]="设备",["Power"]="电量",["Connection"]="连接",["Lighting"]="灯光",["Device settings"]="设备设置",
        ["Device status"]="设备状态",["Battery (%)"]="电量（%）",
        ["General"]="通用",["Global refresh interval (seconds)"]="全局刷新间隔（秒）",
        ["Visible"]="显示",["Excluded"]="已排除",["Show device"]="显示设备",
        ["Follow global"]="跟随全局",["Custom"]="自定义",["Refresh interval"]="刷新间隔",["Interval (seconds)"]="间隔（秒）",
        ["Access denied. Check permissions."]="访问被拒绝，请检查权限。",
        ["Device service unavailable. Restart the app."]="设备服务不可用，请重启应用。",
        ["Connection interrupted. Reconnect the device."]="连接中断，请重新连接设备。",
        ["No response. Device may be asleep."]="设备未响应，可能处于休眠。",
        ["Unexpected device response. Try refreshing."]="设备回复异常，请刷新重试。",
        ["Device not found. Check the connection."]="未找到设备，请检查连接。",
        ["Unable to read. Try refreshing."]="暂时无法读取，请刷新重试。",
        ["Last known battery; current level unconfirmed."]="上次电量，当前电量待确认。",
        ["Battery level unconfirmed."]="当前电量待确认。",
        ["Enter a refresh interval between 5 and 3600 seconds."]="请输入 5 到 3600 秒之间的刷新间隔。",
        ["Offline"]="离线",["Waiting…"]="等待中…",[" · Last known"]=" · 上次读数",["Charging"]="充电中",["Gamepad"]="手柄",["Estimated from 10% battery bands"]="电量按约 10% 档位估算",["Battery status"]="电池状态",["Discharging"]="使用电池",["Fully charged"]="已充满",["Voltage or temperature error"]="电压或温度异常",["Temperature error"]="温度异常",["Charging error"]="充电异常",["Waiting for a reading"]="等待读数",
        ["Waiting for device"]="等待设备",["Unknown"]="未知",["Not supported"]="不支持",["Device offline"]="设备离线",["Yes"]="是",["No"]="否",["Last known: "]="上次读数：",["Read-only"]="只读",
        ["Horizontal DPI"]="水平 DPI",["Vertical DPI"]="垂直 DPI",["Backlight"]="背光",["Debounce (ms)"]="消抖（毫秒）",["Key mode"]="键位模式",["Connection mode"]="连接模式",["Bluetooth channel"]="蓝牙通道",["Battery"]="电量",["Battery level"]="电量",["Reaction"]="响应",["Bluetooth"]="蓝牙",
        ["just now"]="刚刚",["Offline · Last updated "]="离线 · 上次更新 ",["Stale · Last updated "]="读数已过期 · 上次更新 ",["Updated "]="更新于 ",
    };
}
