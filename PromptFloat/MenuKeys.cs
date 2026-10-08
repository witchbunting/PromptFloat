using System.Runtime.InteropServices;
namespace PromptFloat;

// Only active while a menu is open; handles navigation keys without moving
// keyboard focus away from the original editor.
internal sealed class MenuKeys : IDisposable
{
    private delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    private readonly Hook callback;
    private IntPtr handle;
    [DllImport("user32.dll")]private static extern IntPtr SetWindowsHookEx(int kind,Hook proc,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string? name);
    public MenuKeys(Action<int> pressed)
    {
        callback=(code,message,data)=>{
            if(code>=0)
            {
                var key=Marshal.ReadInt32(data);
                if(key is 0x26 or 0x28 or 0x25 or 0x27 or 0x0D or 0x1B)
                {
                    if(message.ToInt32() is 0x100 or 0x104)Application.Current.Dispatcher.BeginInvoke(()=>pressed(key));
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(handle,code,message,data);
        };
        handle=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
    }
    public void Dispose(){if(handle!=IntPtr.Zero){UnhookWindowsHookEx(handle);handle=IntPtr.Zero;}}
}
