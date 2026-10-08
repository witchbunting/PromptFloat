using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace PromptFloat;

// Current-user DPAPI secrets live outside settings/database/backup exports.
internal sealed class CredentialVault
{
    private readonly string path;
    public CredentialVault(string root)=>path=Path.Combine(root,"credentials.json");
    [StructLayout(LayoutKind.Sequential)]private struct Blob {public int Size;public IntPtr Data;}
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool CryptProtectData(ref Blob input,string description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)]private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr pointer);
    private static byte[] Transform(byte[] data,bool protect)
    {
        var input=new Blob {Size=data.Length,Data=Marshal.AllocHGlobal(data.Length)};Blob output=default;
        try
        {
            Marshal.Copy(data,0,input.Data,data.Length);
            var success=protect?CryptProtectData(ref input,"PromptFloat API",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!success)throw new InvalidOperationException("无法保存或解密密钥，请在当前 Windows 用户下重新填写。");
            var result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally {Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);Array.Clear(data);}
    }
    private Dictionary<string,string> Read()=>File.Exists(path)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))??[]:[];
    public string Get(string id)
    {
        try {var values=Read();return values.TryGetValue(id,out var value)?Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value),false)):"";}
        catch {return "";}
    }
    public void Save(IReadOnlyDictionary<string,string> changes)
    {
        if(changes.Count==0)return;
        var values=Read();
        foreach(var pair in changes)
        {
            if(pair.Value.Contains('\r')||pair.Value.Contains('\n'))throw new ArgumentException("密钥不能包含换行。");
            if(string.IsNullOrWhiteSpace(pair.Value))values.Remove(pair.Key);
            else values[pair.Key]=Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(pair.Value.Trim()),true));
        }
        var temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(values));File.Move(temp,path,true);
    }
}
