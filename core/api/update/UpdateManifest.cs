using System.Text.Json;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ShowDevice.Updates;

// V1 release contract: sign the exact manifest bytes, not reserialized JSON.
public sealed record UpdateManifest(int Schema, string Version, string Platform, string Archive, long Size, string Sha256);
public sealed record UpdateEnvelope(string Manifest, string Signature);
public sealed record ReleaseConfig(string Repository, string PublicKeyPem);
public sealed record InstallState(string Current, string? Previous = null, bool Trial = false, string? Rejected = null);

public static class UpdateContract {
    public const long MaxArchive = 768L * 1024 * 1024;
    public static bool ValidVersion(string value) => Regex.IsMatch(value, @"^\d{1,5}\.\d{1,5}\.\d{1,5}$") && System.Version.TryParse(value,out _);
    public static bool Newer(string candidate,string current) => ValidVersion(candidate)&&ValidVersion(current)&&System.Version.Parse(candidate)>System.Version.Parse(current);
    public static bool Configured(ReleaseConfig config) => Regex.IsMatch(config.Repository,@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")&&!string.IsNullOrWhiteSpace(config.PublicKeyPem);
    public static UpdateManifest Verify(UpdateEnvelope envelope, string publicKey) {
        var bytes=Convert.FromBase64String(envelope.Manifest);
        if(bytes.Length>16384)throw new InvalidDataException("Manifest too large");
        using var rsa=RSA.Create();rsa.ImportFromPem(publicKey);
        if(rsa.KeySize<3072||!rsa.VerifyData(bytes,Convert.FromBase64String(envelope.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new InvalidDataException("Invalid update signature");
        var m=JsonSerializer.Deserialize<UpdateManifest>(bytes)??throw new InvalidDataException("Missing manifest");
        if(m.Schema!=1||!ValidVersion(m.Version)||m.Platform!="win-x64"||m.Archive!=$"ShowDevice-{m.Version}-win-x64-update.zip"||m.Size<=0||m.Size>MaxArchive||!Regex.IsMatch(m.Sha256,@"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("Unsupported update manifest");
        return m;
    }
    public static void VerifyArchive(string path, UpdateManifest m) {
        using var stream=File.OpenRead(path);
        if(stream.Length!=m.Size||!Convert.ToHexString(SHA256.HashData(stream)).Equals(m.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update archive mismatch");
    }
    public static void Extract(string archive,string target) {
        if(Directory.Exists(target))throw new IOException("Extraction target already exists");
        using var zip=ZipFile.OpenRead(archive);
        if(zip.Entries.Count>20000)throw new InvalidDataException("Too many archive entries");
        long expanded=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Validate every entry before creating any files.
        foreach(var entry in zip.Entries){
            var name=entry.FullName.Replace('\\','/');var clean=name.TrimEnd('/');
            if(clean.Length==0||name.StartsWith('/')||!names.Add(clean)||((entry.ExternalAttributes>>16)&0xf000)==0xa000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("Unsafe archive entry");
            foreach(var part in clean.Split('/')){
                if(part.Length==0||part is "." or ".."||part.EndsWith('.')||part.EndsWith(' ')||part.IndexOfAny([':', '<', '>', '"', '|', '?', '*'])>=0||part.Any(char.IsControl)||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))throw new InvalidDataException("Unsafe archive path");
            }
            expanded=checked(expanded+entry.Length);
            if(entry.Length>512L*1024*1024||expanded>3L*1024*1024*1024)throw new InvalidDataException("Expanded archive too large");
        }
        if(!names.Contains("ShowDevice.exe")||!names.Contains("dist/showdevice.exe")||!names.Contains("release-version.txt"))throw new InvalidDataException("Incomplete application package");
        Directory.CreateDirectory(target);
        foreach(var entry in zip.Entries){
            var path=Path.Combine(target,entry.FullName.Replace('\\','/').Replace('/',Path.DirectorySeparatorChar));
            if(entry.FullName.EndsWith('/')||entry.FullName.EndsWith('\\')){Directory.CreateDirectory(path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,false);
        }
    }
    public static void WriteJson<T>(string path,T data){
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(data));File.Move(temporary,path,true);
    }
}
