using ShowDevice.Updates;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;

int passed=0;
void Check(bool value,string label){if(!value)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is InvalidDataException or CryptographicException or FormatException){Check(true,label);return;}throw new Exception("Accepted: "+label);}
using var key=RSA.Create(3072);using var other=RSA.Create(3072);
var root=Path.Combine(Path.GetTempPath(),"showdevice-updates-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
UpdateEnvelope Sign(UpdateManifest manifest){var bytes=JsonSerializer.SerializeToUtf8Bytes(manifest);return new(Convert.ToBase64String(bytes),Convert.ToBase64String(key.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pss)));}
string Zip(params string[] names){var path=Path.Combine(root,Guid.NewGuid()+".zip");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);foreach(var n in names){var entry=zip.CreateEntry(n);using var writer=new StreamWriter(entry.Open());writer.Write("test");}return path;}
try{
    var path=Zip("ShowDevice.exe","dist/showdevice.exe","release-version.txt");
    var m=new UpdateManifest(1,"1.2.3","win-x64","ShowDevice-1.2.3-win-x64-update.zip",new FileInfo(path).Length,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    var signed=Sign(m);Check(UpdateContract.Verify(signed,key.ExportSubjectPublicKeyInfoPem())==m,"valid RSA-PSS manifest");
    var minimal=m with{Flavor="minimal",Archive="ShowDevice-1.2.3-win-x64-minimal-update.zip"};
    Check(UpdateContract.Verify(Sign(minimal),key.ExportSubjectPublicKeyInfoPem(),"minimal")==minimal,"minimal signed manifest");
    Reject(()=>UpdateContract.Verify(Sign(minimal),key.ExportSubjectPublicKeyInfoPem()),"full install rejects minimal update");
    Reject(()=>UpdateContract.Verify(signed,key.ExportSubjectPublicKeyInfoPem(),"minimal"),"minimal install rejects full update");
    var legacy=JsonSerializer.SerializeToUtf8Bytes(new{m.Schema,m.Version,m.Platform,m.Archive,m.Size,m.Sha256});
    Check(UpdateContract.Verify(new(Convert.ToBase64String(legacy),Convert.ToBase64String(key.SignData(legacy,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))),key.ExportSubjectPublicKeyInfoPem()).Flavor=="full","legacy manifest defaults to full");
    Reject(()=>UpdateContract.Verify(signed,other.ExportSubjectPublicKeyInfoPem()),"wrong signing key");
    Reject(()=>UpdateContract.Verify(signed with{Manifest=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(m with{Version="2.0.0"}))},key.ExportSubjectPublicKeyInfoPem()),"tampered manifest");
    Reject(()=>UpdateContract.Verify(Sign(m with{Archive="../evil.zip"}),key.ExportSubjectPublicKeyInfoPem()),"signed but unsafe package name");
    Reject(()=>UpdateContract.Verify(Sign(m with{Platform="win-arm64"}),key.ExportSubjectPublicKeyInfoPem()),"wrong architecture");
    Reject(()=>UpdateContract.Verify(Sign(m with{Schema=2}),key.ExportSubjectPublicKeyInfoPem()),"future schema fails closed");
    UpdateContract.VerifyArchive(path,m);Check(true,"archive size and hash verified");
    Reject(()=>UpdateContract.VerifyArchive(path,m with{Sha256=new string('0',64)}),"corrupt archive");
    Check(UpdateContract.Newer("1.10.0","1.9.0")&&!UpdateContract.Newer("1.2.3","1.2.3")&&!UpdateContract.Newer("1.0.0","2.0.0"),"numeric version ordering rejects replay and downgrade");
    foreach(var name in new[]{"../outside","/absolute","C:/escape","dir/../../escape","file:stream","CON.txt","trailing. ","foo//bar"}){
        var bad=Zip("ShowDevice.exe","dist/showdevice.exe","release-version.txt",name);var dest=Path.Combine(root,Guid.NewGuid().ToString());Reject(()=>UpdateContract.Extract(bad,dest),"unsafe zip: "+name);Check(!Directory.Exists(dest),"validation before extraction");
    }
    Reject(()=>UpdateContract.Extract(Zip("ShowDevice.exe","showdevice.EXE","dist/showdevice.exe","release-version.txt"),Path.Combine(root,"duplicate")),"case-insensitive duplicate");
    Reject(()=>UpdateContract.Extract(Zip("other.txt"),Path.Combine(root,"missing")),"missing application/core");
    var target=Path.Combine(root,"valid");UpdateContract.Extract(path,target);Check(File.Exists(Path.Combine(target,"dist/showdevice.exe")),"valid package extracted");
    var statePath=Path.Combine(root,"install-state.json");var state=new InstallState("1.2.3","1.0.0",true);UpdateContract.WriteJson(statePath,state);Check(JsonSerializer.Deserialize<InstallState>(File.ReadAllText(statePath))==state,"trial marker survives restart");
    Console.WriteLine($"{passed} update checks passed");
}finally{Directory.Delete(root,true);}
