using System.Text.Json;
namespace ShowDevice;

internal sealed class BatteryReminderRule {
    public bool LowEnabled {get;set;}
    public int LowPercent {get;set;}=20;
    public bool ChargedEnabled {get;set;}
    public int ChargedPercent {get;set;}=80;
    public int? BackgroundIntervalSeconds {get;set;}
    public void Normalize(){LowPercent=Math.Clamp(LowPercent,1,99);ChargedPercent=Math.Clamp(ChargedPercent,1,100);if(BackgroundIntervalSeconds is int seconds)BackgroundIntervalSeconds=Math.Clamp(seconds,60,3600);}
}
internal sealed record BatteryReminder(string DeviceId,string Name,int Percent,bool Charged);
internal sealed class BatteryReminders {
    private readonly Dictionary<string,(bool Low,bool Charged,int LowAt,int ChargedAt)> episodes=new();
    public IEnumerable<BatteryReminder> Evaluate(JsonElement devices,AppSettings settings,long now){
        foreach(var id in episodes.Keys.Where(id=>!settings.BatteryReminders.ContainsKey(id)||settings.ExcludedDevices.Contains(id)).ToArray())episodes.Remove(id);
        foreach(var item in devices.EnumerateArray()){
            var device=item.GetProperty("device");string id=device.GetProperty("id").GetString()!;
            if(id.StartsWith("simulation:",StringComparison.Ordinal)||settings.ExcludedDevices.Contains(id)||!settings.BatteryReminders.TryGetValue(id,out var rule))continue;
            var old=episodes.GetValueOrDefault(id);
            bool low=rule.LowEnabled&&old.LowAt==rule.LowPercent&&old.Low;
            bool charged=rule.ChargedEnabled&&old.ChargedAt==rule.ChargedPercent&&old.Charged;
            episodes[id]=(low,charged,rule.LowPercent,rule.ChargedPercent);
            if(item.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.String&&!string.IsNullOrEmpty(error.GetString()))continue;
            var state=item.GetProperty("state");
            if(!state.TryGetProperty("battery.percent",out var reading)||!Fresh(reading,now,settings.DeviceIntervals.GetValueOrDefault(id,settings.IntervalSeconds))||!reading.TryGetProperty("value",out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var percent)||!double.IsFinite(percent)||percent<0||percent>100)continue;
            bool? charging=null;
            if(state.TryGetProperty("battery.charging",out var charge)&&Fresh(charge,now,settings.DeviceIntervals.GetValueOrDefault(id,settings.IntervalSeconds))&&charge.TryGetProperty("value",out var cv)&&cv.ValueKind is JsonValueKind.True or JsonValueKind.False)charging=cv.GetBoolean();
            // A slower background poll may first observe a completed charge.
            // Only explicit device confirmation counts; 100% alone does not.
            if(percent==100&&state.TryGetProperty("battery.status",out var status)&&Fresh(status,now,settings.DeviceIntervals.GetValueOrDefault(id,settings.IntervalSeconds))&&status.TryGetProperty("value",out var sv)&&sv.ValueKind==JsonValueKind.String&&sv.GetString()=="Fully charged")charging=true;
            // Hysteresis prevents threshold jitter; disconnection does not re-arm.
            if(percent>=rule.LowPercent+5)low=false;
            if(charging==false||percent<=rule.ChargedPercent-5)charged=false;
            BatteryReminder? reminder=null;
            string name=device.GetProperty("name").GetString()??id;
            if(rule.LowEnabled&&!low&&charging!=true&&percent<=rule.LowPercent){low=true;reminder=new(id,name,(int)Math.Round(percent),false);}
            if(rule.ChargedEnabled&&!charged&&charging==true&&percent>=rule.ChargedPercent){charged=true;reminder=new(id,name,(int)Math.Round(percent),true);}
            episodes[id]=(low,charged,rule.LowPercent,rule.ChargedPercent);
            if(reminder!=null)yield return reminder;
        }
    }
    private static bool Fresh(JsonElement reading,long now,int interval)=>
        reading.TryGetProperty("availability",out var a)&&a.GetString()=="available"&&
        reading.TryGetProperty("updated_at_ms",out var t)&&t.ValueKind==JsonValueKind.Number&&t.TryGetInt64(out var updated)&&
        updated<=now+5000&&now-updated<=Math.Max(120,interval*2)*1000L;
}
