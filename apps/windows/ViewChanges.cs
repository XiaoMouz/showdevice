using System.Text.Json;
namespace ShowDevice;

// Compare only what each region displays. Reading timestamps still update the model
// and title clock, but must not invalidate native controls or decoded artwork.
internal static class ViewChanges {
    private static bool Same(JsonElement a,JsonElement b,string key){
        bool hasA=a.TryGetProperty(key,out var av),hasB=b.TryGetProperty(key,out var bv);
        return hasA==hasB&&(!hasA||JsonElement.DeepEquals(av,bv));
    }
    private static bool Reading(JsonElement a,JsonElement b,string key){
        bool hasA=a.TryGetProperty(key,out var av),hasB=b.TryGetProperty(key,out var bv);
        return hasA==hasB&&(!hasA||(Same(av,bv,"value")&&Same(av,bv,"availability")));
    }
    public static bool Header(JsonElement a,JsonElement b)=>
        Same(a.GetProperty("device"),b.GetProperty("device"),"id")&&
        Same(a.GetProperty("device"),b.GetProperty("device"),"name")&&
        Same(a,b,"device_kind")&&Same(a,b,"plugin")&&Same(a,b,"product_image");
    private static bool BatteryFailure(JsonElement a,JsonElement b)=>ReadingNotice.IsParseFailure(ReadingNotice.Error(a))==ReadingNotice.IsParseFailure(ReadingNotice.Error(b));
    public static bool ListRow(JsonElement a,JsonElement b)=>
        BatteryFailure(a,b)&&
        Same(a.GetProperty("device"),b.GetProperty("device"),"name")&&Same(a,b,"device_kind")&&
        Reading(a.GetProperty("state"),b.GetProperty("state"),"battery.percent")&&
        Reading(a.GetProperty("state"),b.GetProperty("state"),"battery.charging");
    public static bool Body(JsonElement a,JsonElement b){
        if(!BatteryFailure(a,b)||!Same(a,b,"pages")||!Same(a,b,"capabilities")||!Same(a,b,"device_kind"))return false;
        var before=a.GetProperty("state");var after=b.GetProperty("state");
        return before.EnumerateObject().Count()==after.EnumerateObject().Count()&&
            before.EnumerateObject().All(p=>Reading(before,after,p.Name));
    }
}
