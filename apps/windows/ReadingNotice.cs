namespace ShowDevice;
internal static class ReadingNotice {
    public static bool IsCharging(System.Text.Json.JsonElement state)=>
        !(state.TryGetProperty("battery.percent",out var battery)&&battery.GetProperty("availability").GetString()=="offline")&&
        state.TryGetProperty("battery.charging",out var charging)&&
        charging.GetProperty("availability").GetString()=="available"&&
        charging.TryGetProperty("value",out var value)&&value.ValueKind==System.Text.Json.JsonValueKind.True;
    // Prefer known failure categories; a timeout alone does not prove sleep.
    public static bool MayBeSleeping(string? error){
        if(string.IsNullOrWhiteSpace(error))return false;
        var text=error.ToLowerInvariant();
        if(text.Contains("plugin")||text.Contains("handle retired")||text.Contains("checksum")||text.Contains("invalid")||text.Contains("denied"))return false;
        return text.Contains("may be asleep")||text.Contains("timed out")||text.Contains("timeout")||text.Contains("not responding");
    }
    public static string Brief(string? error){
        var text=(error??"").ToLowerInvariant();
        if(text.Contains("denied")||text.Contains("access is denied"))return L.T("Access denied. Check permissions.");
        if(text.Contains("local core"))return L.T("Device service unavailable. Restart the app.");
        if(text.Contains("plugin")||text.Contains("handle retired")||text.Contains("reconnect the device")){
            if(!MayBeSleeping(error))return L.T("Connection interrupted. Reconnect the device.");
        }
        if(MayBeSleeping(error))return L.T("No response. Device may be asleep.");
        if(IsParseFailure(error)||text.Contains("invalid"))return L.T("Unexpected device response. Try refreshing.");
        if(text.Contains("enumeration")||text.Contains("not found")||text.Contains("disconnected"))return L.T("Device not found. Check the connection.");
        return L.T("Unable to read. Try refreshing.");
    }
    public static string? Error(System.Text.Json.JsonElement device)=>device.TryGetProperty("error",out var error)&&error.ValueKind==System.Text.Json.JsonValueKind.String?error.GetString():null;
    public static bool IsParseFailure(string? error){
        var text=(error??"").ToLowerInvariant();
        return text.Contains("checksum")||text.Contains("malformed")||text.Contains("parse")||
            (text.Contains("invalid")&&(text.Contains("response")||text.Contains("payload")||text.Contains("battery")));
    }
    public static bool UncertainBattery(string? availability)=>availability=="unknown";
    public static string BatteryHint(string? availability)=>L.T(availability=="stale"?"Last known battery; current level unconfirmed.":"Battery level unconfirmed.");
}
