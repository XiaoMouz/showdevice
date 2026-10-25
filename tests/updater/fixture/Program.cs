var version=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"release-version.txt")).Trim();
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"started"),version);
if(version=="1.0.2")return 3;
var health=Environment.GetEnvironmentVariable("SHOWDEVICE_UPDATE_HEALTH");
if(!string.IsNullOrEmpty(health))File.WriteAllText(health,"ready");
await Task.Delay(3000);
return 0;
