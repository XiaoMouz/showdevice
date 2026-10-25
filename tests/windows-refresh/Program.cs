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
