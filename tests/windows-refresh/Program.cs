using System.Text.Json;
using System.Text.Json.Nodes;
using ShowDevice;

const string snapshot="""
{"device":{"id":"mouse:1","name":"Mouse"},"plugin":"test","device_kind":"mouse",
 "product_image":"C:/local/product.png","pages":[],"capabilities":[],"error":null,
 "state":{"battery.percent":{"availability":"available","value":70,"updated_at_ms":1000},
          "dpi":{"availability":"available","value":800,"updated_at_ms":1000}}}
""";
var original=JsonSerializer.Deserialize<JsonElement>(snapshot);
JsonElement Changed(Action<JsonNode> change){var node=JsonNode.Parse(snapshot)!;change(node);return JsonSerializer.SerializeToElement(node);}
void Expect(bool result,string scenario){if(!result)throw new Exception(scenario);Console.WriteLine("PASS "+scenario);}
var timestamp=Changed(n=>n["state"]!["battery.percent"]!["updated_at_ms"]=2000);
Expect(ViewChanges.Header(original,timestamp)&&ViewChanges.ListRow(original,timestamp)&&ViewChanges.Body(original,timestamp),"timestamp-only refresh preserves every visual region");
var dpi=Changed(n=>n["state"]!["dpi"]!["value"]=1600);
Expect(ViewChanges.Header(original,dpi)&&ViewChanges.ListRow(original,dpi)&&!ViewChanges.Body(original,dpi),"DPI change updates readings without touching list or artwork");
var battery=Changed(n=>n["state"]!["battery.percent"]!["value"]=69);
var charging=Changed(n=>n["state"]!["battery.charging"]=new JsonObject{["availability"]="available",["value"]=true,["updated_at_ms"]=1000});
Expect(!ViewChanges.ListRow(original,charging)&&!ViewChanges.Body(original,charging)&&ViewChanges.Header(original,charging),"charging-only update refreshes icons without reloading artwork");
Expect(ReadingNotice.IsCharging(charging.GetProperty("state")),"fresh charging true shows charging icon");
foreach(var availability in new[]{"available","stale","unknown","unsupported","offline"}){
    var node=JsonNode.Parse(charging.GetRawText())!;
    node["state"]!["battery.charging"]!["availability"]=availability;
    if(availability=="available")node["state"]!["battery.charging"]!["value"]=false;
    var stopped=JsonSerializer.SerializeToElement(node);
    Expect(!ReadingNotice.IsCharging(stopped.GetProperty("state"))&&!ViewChanges.ListRow(charging,stopped),"charging "+availability+" transition removes bolt and refreshes list");
}
var chargeTime=JsonNode.Parse(charging.GetRawText())!;chargeTime["state"]!["battery.charging"]!["updated_at_ms"]=2000;
Expect(ViewChanges.ListRow(charging,JsonSerializer.SerializeToElement(chargeTime)),"charging timestamp-only update preserves list");
Expect(ViewChanges.Header(original,battery)&&!ViewChanges.ListRow(original,battery)&&!ViewChanges.Body(original,battery),"battery change remains visible without reloading artwork");
foreach(var availability in new[]{"stale","offline","unsupported","unknown"}){
    var state=Changed(n=>n["state"]!["battery.percent"]!["availability"]=availability);
    Expect(!ViewChanges.ListRow(original,state)&&!ViewChanges.Body(original,state)&&ViewChanges.Header(original,state),availability+" transition updates readings and preserves image");
}
Expect(!ViewChanges.Header(original,Changed(n=>n["product_image"]="C:/local/new.png")),"changed artwork invalidates header");
Expect(!ViewChanges.Header(original,Changed(n=>n["device"]!["id"]="mouse:2")),"device selection invalidates header");
Expect(!ViewChanges.Body(original,Changed(n=>n["pages"]!.AsArray().Add(new JsonObject()))),"page change invalidates readings");
Expect(!ViewChanges.Body(original,Changed(n=>n["state"]!.AsObject().Remove("dpi"))),"removed capability state invalidates readings");
var preferenceFile=Path.Combine(Path.GetTempPath(),"showdevice-prefs-"+Guid.NewGuid()+".json");
try{
    var prefs=new WindowPreferences{CompactList=true,Maximized=true,X=-1800,Y=100,Width=1000,Height=700};
    prefs.Save(preferenceFile);
    Expect(WindowPreferences.Load(preferenceFile)==prefs,"window and pane state survive disk roundtrip");
    var fitted=prefs.Fit(0,0,1280,720);
    Expect(fitted.X==0&&fitted.Y==20&&fitted.Width==1000,"disconnected monitor placement returns to work area");
    var small=(prefs with{Width=int.MaxValue,Height=-1}).Fit(-800,0,800,600);
    Expect(small.Width==800&&small.Height==420&&small.X==-800,"invalid dimensions clamp on negative-coordinate monitor");
    File.WriteAllText(preferenceFile,"{broken");
    Expect(WindowPreferences.Load(preferenceFile)==new WindowPreferences(),"corrupt preferences fall back safely");
    var app=new AppSettings{Language="en",IntervalSeconds=60,DeviceIntervals=new(){{"mouse",5}},ExcludedDevices=new(){"keyboard"},KnownDevices=new(){{"keyboard","Keyboard"}}};
    app.Save(preferenceFile);
    var saved=AppSettings.Load(preferenceFile);
    Expect(saved.Language=="en"&&saved.IntervalSeconds==60&&saved.DeviceIntervals["mouse"]==5&&saved.ExcludedDevices.Contains("keyboard")&&saved.KnownDevices.ContainsKey("keyboard"),"language, intervals and excluded-device recovery survive restart");
    File.WriteAllText(preferenceFile,"{\"IntervalSeconds\":-1,\"Language\":\"invalid\",\"DeviceIntervals\":null,\"ExcludedDevices\":null,\"KnownDevices\":null}");
    saved=AppSettings.Load(preferenceFile);
    Expect(saved.IntervalSeconds==5&&saved.Language=="zh"&&saved.DeviceIntervals.Count==0,"invalid settings normalize without crashing");
    L.Language="zh";Expect(L.T("Settings")=="设置","Chinese resources resolve");
    L.Language="en";Expect(L.T("Settings")=="Settings","English resources resolve");
    const string sleeping="No valid Razer replies; device may be asleep or interface unavailable. Waiting for device; retrying automatically.";
    L.Language="zh";
    Expect(ReadingNotice.MayBeSleeping(sleeping)&&ReadingNotice.Brief(sleeping)=="设备未响应，可能处于休眠。","sleep hint is concise and localized without claiming confirmed sleep");
    Expect(!ReadingNotice.MayBeSleeping("plugin timeout or closed pipe")&&!ReadingNotice.MayBeSleeping("invalid response checksum; retrying automatically"),"plugin and protocol failures are not mislabeled as sleep");
    Expect(!ReadingNotice.UncertainBattery("stale")&&ReadingNotice.UncertainBattery("unknown")&&!ReadingNotice.UncertainBattery("available")&&!ReadingNotice.UncertainBattery("offline")&&!ReadingNotice.UncertainBattery("unsupported"),"question mark is reserved for missing readings");
    Expect(!ReadingNotice.Brief("unknown internal error").Contains("internal"),"unknown diagnostics use a localized fallback");
    L.Language="en";Expect(ReadingNotice.Brief(sleeping)=="No response. Device may be asleep.","English notices remain available");
}finally{File.Delete(preferenceFile);if(File.Exists(preferenceFile+".tmp"))File.Delete(preferenceFile+".tmp");}

var parseError=Changed(n=>n["error"]="Razer response checksum mismatch");
Expect(ReadingNotice.IsParseFailure(ReadingNotice.Error(parseError))&&!ReadingNotice.IsParseFailure("request timed out")&&!ReadingNotice.IsParseFailure("invalid plugin manifest"),"parse failures are distinct from sleep and plugin failures");
Expect(!ViewChanges.ListRow(original,parseError)&&!ViewChanges.Body(original,parseError)&&ViewChanges.Header(original,parseError),"parse error alone updates icons without reloading artwork");
Expect(!ViewChanges.ListRow(parseError,original)&&!ViewChanges.Body(parseError,original),"recovery clears warning icons without a value change");
var card=new SurfaceRect(0,0,100,100);
Expect(SurfacePolicy.DragPosition(500,400,520,430,620,480)==(600,450),"drag preserves pointer grab offset in physical screen coordinates");
Expect(SurfacePolicy.DragPosition(500,400,520,430,520,430)==(500,400),"dragging back to the press position restores the original bounds");
Expect(SurfacePolicy.DragPosition(-1500,100,-1480,120,-1600,80)==(-1620,60),"drag supports negative monitor coordinates without DPI rescaling");
Expect(!SurfacePolicy.Exposed(card,new[]{new SurfaceRect(0,0,50,100),new SurfaceRect(50,0,100,100)}),"multiple overlapping windows can fully cover a card");
Expect(SurfacePolicy.Exposed(card,new[]{new SurfaceRect(0,0,99,100)}),"partly exposed card retains refresh demand");
Expect(!SurfacePolicy.Exposed(card,new[]{new SurfaceRect(-100,-100,200,200)}),"maximized cover suspends desktop demand");
Expect(SurfacePolicy.Exposed(new SurfaceRect(-400,0,-100,100),new[]{card}),"negative-coordinate monitor remains visible");
Expect(SurfacePolicy.Interpolate(47,80,0,420)==47&&SurfacePolicy.Interpolate(47,80,420,420)==80,"battery interpolation starts from cache and reaches exact new value");
var halfway=SurfacePolicy.Interpolate(80,47,210,420);
Expect(halfway>47&&halfway<80&&SurfacePolicy.Interpolate(halfway,90,0,420)==halfway,"decreasing and interrupted animations stay continuous");
Expect(SurfacePolicy.Interpolate(47,80,0,0)==80,"reduced motion skips interpolation");
var style=new SurfacePreferences{Width=double.NaN,TransitionMs=5000,Style="future.style",Devices=new(){"mouse","mouse"}};style.Normalize();
Expect(style.Width==320&&style.TransitionMs==1000&&style.Devices.Count==1,"surface preferences bound sizes, motion and duplicate devices");
var arrangement=JsonSerializer.Deserialize<SurfacePreferences>("{\"AllDevices\":false,\"Devices\":[\"keyboard\"]}")!;
Expect(arrangement.TrayAllDevices&&SurfacePolicy.Visible(arrangement,false,"mouse")&&!SurfacePolicy.Visible(arrangement,true,"mouse"),"legacy desktop selections do not hide tray devices");
arrangement.TrayAllDevices=false;arrangement.TrayDevices.Add("mouse");
Expect(SurfacePolicy.Visible(arrangement,false,"mouse")&&!SurfacePolicy.Visible(arrangement,false,"keyboard")&&SurfacePolicy.Visible(arrangement,true,"keyboard"),"tray and desktop selections are independent");
arrangement.MainOrder=new(){"mouse","keyboard","headset"};arrangement.TrayOrder=new(){"headset","mouse"};arrangement.DesktopOrder=new(){"keyboard","mouse"};
var arrangementCopy=JsonSerializer.Deserialize<SurfacePreferences>(JsonSerializer.Serialize(arrangement))!;arrangementCopy.Normalize();
Expect(arrangementCopy.MainOrder.SequenceEqual(arrangement.MainOrder)&&arrangementCopy.TrayOrder.SequenceEqual(arrangement.TrayOrder)&&arrangementCopy.DesktopOrder.SequenceEqual(arrangement.DesktopOrder)&&arrangementCopy.TrayDevices.SequenceEqual(new[]{"mouse"}),"independent orders and tray selection survive restart");
var deviceNames=new Dictionary<string,string>{{"keyboard","Keyboard"},{"mouse","Mouse"},{"headset","Headset"},{"new-b","New"},{"new-a","New"}};
string[] Arrange(IEnumerable<KeyValuePair<string,string>> devices)=>SurfacePolicy.Ordered(devices,arrangement.MainOrder,d=>d.Key,d=>d.Value).Select(d=>d.Key).ToArray();
Expect(Arrange(deviceNames).SequenceEqual(new[]{"mouse","keyboard","headset","new-a","new-b"})&&Arrange(deviceNames.Reverse()).SequenceEqual(Arrange(deviceNames)),"saved rank and deterministic fallback ignore discovery order");
Expect(Arrange(deviceNames.Where(d=>d.Key!="keyboard")).SequenceEqual(new[]{"mouse","headset","new-a","new-b"})&&Arrange(deviceNames)[1]=="keyboard","disconnected device keeps its saved position on reconnect");
var trayDemand=SurfacePolicy.Interest(arrangement,false,true,false);
Expect(!trayDemand.All&&trayDemand.Ids.SequenceEqual(new[]{"mouse"}),"tray requests only its selected devices");
var combinedDemand=SurfacePolicy.Interest(arrangement,false,true,true);
Expect(!combinedDemand.All&&combinedDemand.Ids.SequenceEqual(new[]{"keyboard","mouse"}),"visible compact surfaces combine selections");
Expect(SurfacePolicy.Interest(arrangement,true,true,true).All,"visible main window requests all nonexcluded devices");
var hiddenDemand=SurfacePolicy.Interest(arrangement,false,false,false);
Expect(!hiddenDemand.All&&hiddenDemand.Ids.Length==0,"hidden surfaces request no devices");
arrangement.TrayDevices.Clear();var emptyDemand=SurfacePolicy.Interest(arrangement,false,true,false);
Expect(!emptyDemand.All&&emptyDemand.Ids.Length==0&&!SurfacePolicy.Visible(arrangement,false,"mouse"),"empty tray selection neither displays nor requests all devices");
var malformedArrangement=JsonSerializer.Deserialize<SurfacePreferences>("{\"TrayDevices\":null,\"MainOrder\":[\"mouse\",\"mouse\",\"\",null],\"TrayOrder\":null,\"DesktopOrder\":null}")!;malformedArrangement.Normalize();
Expect(malformedArrangement.MainOrder.SequenceEqual(new[]{"mouse"})&&malformedArrangement.TrayDevices.Count==0&&malformedArrangement.TrayOrder.Count==0&&malformedArrangement.DesktopOrder.Count==0,"invalid saved order entries normalize safely");
var appearance=new Personalization();appearance.Desktop.Layout="details";appearance.Tray.Indicator="ring";
appearance.Desktop.DeviceBackgrounds["mouse"]=new(){Kind="color",Color="#123456"};
appearance.Desktop.Fields["mouse"]=new(){"dpi","dpi","battery.percent"};
var appearanceCopy=JsonSerializer.Deserialize<Personalization>(JsonSerializer.Serialize(appearance))!;appearanceCopy.Normalize();
Expect(appearanceCopy.Desktop.Layout=="details"&&appearanceCopy.Tray.Layout=="list"&&appearanceCopy.Desktop.Indicator=="icon"&&appearanceCopy.Tray.Indicator=="ring","desktop and tray styles persist independently");
Expect(appearanceCopy.Desktop.ForDevice("mouse").Color=="#123456"&&appearanceCopy.Tray.ForDevice("mouse").Kind=="system","device background overrides remain scoped to their surface");
Expect(appearanceCopy.Desktop.Fields["mouse"].Count==2,"detailed parameter selections deduplicate");
var invalidAppearance=new SurfaceAppearance{Layout="script",Indicator="future",LowColor="#GG0000",LowAt=100,MediumAt=-4};invalidAppearance.Normalize();
Expect(invalidAppearance.Layout=="list"&&invalidAppearance.Indicator=="icon"&&invalidAppearance.LowColor=="#C43838"&&invalidAppearance.LowAt<invalidAppearance.MediumAt,"invalid appearance safely falls back and preserves threshold order");
Expect(AppearanceRules.BatteryColor(new(),20)=="#C43838"&&AppearanceRules.BatteryColor(new(),40)=="#A96F00"&&AppearanceRules.BatteryColor(new(),80)=="#1A8F62","battery color switches at inclusive configurable thresholds");
var ringStart=AppearanceRules.RingPoint(0);var ringEnd=AppearanceRules.RingPoint(100);
Expect(Math.Abs(ringStart.Y-ringEnd.Y)<.0001&&ringStart.X<12&&ringEnd.X>12&&ringEnd.Y>12,"330-degree ring leaves its 30-degree gap centered at the bottom");
Expect(ThemePolicy.Resolve("system","app",false)=="light"&&ThemePolicy.Resolve("system","app",true)=="dark","automatic theme follows system changes");
Expect(ThemePolicy.Resolve("dark","app",false)=="dark"&&ThemePolicy.Resolve("dark","light",true)=="light","surface theme can override the global theme");
Expect(ThemePolicy.Resolve("dark","system",false)=="light","surface may follow the system independently of app preference");
Expect(ThemePolicy.Global("unknown")=="system"&&ThemePolicy.Surface("unknown")=="app","invalid themes fall back to inheritance");
var themed=new AppSettings{Theme="dark"};themed.Personalization.Desktop.Theme="light";themed.Personalization.Tray.Theme="system";
var themeCopy=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(themed))!;
Expect(themeCopy.Theme=="dark"&&themeCopy.Personalization.Desktop.Theme=="light"&&themeCopy.Personalization.Tray.Theme=="system","global and independent surface themes survive persistence");

Expect(AppearanceRules.BatteryColor(new(),80,true)=="#45D99C"&&AppearanceRules.BatteryColor(new(),20,true)=="#FF727A","dark battery defaults use brighter readable accents");
var customBattery=new SurfaceAppearance{HighColor="#123456"};
var lifted=AppearanceRules.BatteryColor(customBattery,80,true);
Expect(lifted!="#123456"&&customBattery.HighColor=="#123456"&&AppearanceRules.BatteryColor(customBattery,80)=="#123456","dark rendering preserves the stored custom color and light appearance");
Expect(Convert.ToByte(lifted.Substring(1,2),16)<Convert.ToByte(lifted.Substring(3,2),16)&&Convert.ToByte(lifted.Substring(3,2),16)<Convert.ToByte(lifted.Substring(5,2),16),"dark accent adaptation preserves custom hue channel order");
Expect(AppearanceRules.BatteryColor(new(){HighColor="#FFFFFF"},80,true)=="#FFFFFF"&&AppearanceRules.BatteryColor(new(){HighColor="#000000"},80,true)=="#A6A6A6","already bright colors stay unchanged and neutral accents stay neutral");

Expect(SurfacePolicy.FitPosition(1900,1100,320,191,new(0,0,1920,1080))==(1600,889),"dragged popup remains inside its monitor work area");
Expect(SurfacePolicy.FitPosition(-1800,30,320,191,new(-1920,0,0,1080))==(-1800,30),"popup dragging preserves negative-coordinate monitor positions");
var positioned=new SurfacePreferences{X=12,Y=34,TrayX=456,TrayY=78};
var positionCopy=JsonSerializer.Deserialize<SurfacePreferences>(JsonSerializer.Serialize(positioned))!;
Expect(positionCopy.X==12&&positionCopy.Y==34&&positionCopy.TrayX==456&&positionCopy.TrayY==78,"desktop and tray positions persist independently");

var legacyBackground=JsonSerializer.Deserialize<BackgroundSpec>("{\"Kind\":\"color\",\"Color\":\"#123456\"}")!;
Expect(legacyBackground.Opacity==1,"existing solid backgrounds remain opaque after migration");
var opacitySpec=new BackgroundSpec{Opacity=double.NaN};opacitySpec.Normalize();Expect(opacitySpec.Opacity==1,"invalid background opacity falls back to opaque");
opacitySpec.Opacity=-.2;opacitySpec.Normalize();Expect(opacitySpec.Opacity==0,"background opacity clamps to fully transparent");
opacitySpec.Opacity=1.5;opacitySpec.Normalize();Expect(opacitySpec.Opacity==1,"background opacity clamps to fully opaque");
var opacityPrefs=new Personalization();opacityPrefs.MainBackground.Opacity=.25;opacityPrefs.DeviceBackground.Opacity=.5;opacityPrefs.Desktop.Background.Opacity=.35;opacityPrefs.Tray.Background.Opacity=.45;opacityPrefs.Desktop.DeviceBackground.Opacity=.6;opacityPrefs.Tray.DeviceBackground.Opacity=.7;
opacityPrefs.DeviceBackgrounds["mouse"]=new(){Opacity=.1};opacityPrefs.Desktop.DeviceBackgrounds["mouse"]=new(){Opacity=.2};opacityPrefs.Tray.DeviceBackgrounds["mouse"]=new(){Opacity=0};
var savedOpacity=JsonSerializer.Deserialize<Personalization>(JsonSerializer.Serialize(opacityPrefs))!;savedOpacity.Normalize();
Expect(savedOpacity.MainBackground.Opacity==.25&&savedOpacity.DeviceBackground.Opacity==.5&&savedOpacity.Desktop.Background.Opacity==.35&&savedOpacity.Tray.Background.Opacity==.45&&savedOpacity.Desktop.DeviceBackground.Opacity==.6&&savedOpacity.Tray.DeviceBackground.Opacity==.7,"all six common background scopes preserve independent opacity");
Expect(savedOpacity.DeviceBackgrounds["mouse"].Opacity==.1&&savedOpacity.Desktop.ForDevice("mouse").Opacity==.2&&savedOpacity.Tray.ForDevice("mouse").Opacity==0,"per-device opacity overrides survive roundtrip independently");
