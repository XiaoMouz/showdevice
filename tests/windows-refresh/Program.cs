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
Expect(ViewChanges.Header(original,battery)&&!ViewChanges.ListRow(original,battery)&&!ViewChanges.Body(original,battery),"battery change remains visible without reloading artwork");
foreach(var availability in new[]{"stale","offline","unsupported","unknown"}){
    var state=Changed(n=>n["state"]!["battery.percent"]!["availability"]=availability);
    Expect(!ViewChanges.ListRow(original,state)&&!ViewChanges.Body(original,state)&&ViewChanges.Header(original,state),availability+" transition updates readings and preserves image");
}
Expect(!ViewChanges.Header(original,Changed(n=>n["product_image"]="C:/local/new.png")),"changed artwork invalidates header");
Expect(!ViewChanges.Header(original,Changed(n=>n["device"]!["id"]="mouse:2")),"device selection invalidates header");
Expect(!ViewChanges.Body(original,Changed(n=>n["pages"]!.AsArray().Add(new JsonObject()))),"page change invalidates readings");
Expect(!ViewChanges.Body(original,Changed(n=>n["state"]!.AsObject().Remove("dpi"))),"removed capability state invalidates readings");
