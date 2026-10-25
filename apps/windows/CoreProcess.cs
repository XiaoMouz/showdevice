using System.Diagnostics;
using System.Text;
namespace ShowDevice;

internal static class CoreProcess {
    // JSON Lines is UTF-8 on all three pipes, regardless of the Windows code page.
    public static ProcessStartInfo StartInfo(string executable)=>new(executable){
        UseShellExecute=false,CreateNoWindow=true,
        RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
        StandardInputEncoding=new UTF8Encoding(false,true),
        StandardOutputEncoding=new UTF8Encoding(false,true),
        StandardErrorEncoding=new UTF8Encoding(false,true),
    };
}
