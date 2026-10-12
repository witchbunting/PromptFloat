using System.Runtime.InteropServices;
using System.Windows.Threading;
namespace PromptFloat;

// Observe button presses only while a menu is open. Keep the native hook on
// its own message loop so rendering or input providers cannot delay it.
internal sealed class MenuMouse : IDisposable
{
    private delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    private struct Message {public IntPtr Window;public uint Id;public UIntPtr WParam;public IntPtr LParam;public uint Time;public Native.Point Point;public uint Private;}
    private readonly Hook callback;
    private readonly Dispatcher dispatcher;
    private IntPtr handle;
    private uint threadId;
    private volatile bool disposed;
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SetWindowsHookEx(int kind,Hook proc,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll",EntryPoint="PeekMessageW")]private static extern bool PeekMessage(out Message message,IntPtr window,uint min,uint max,uint remove);
    [DllImport("user32.dll",EntryPoint="GetMessageW")]private static extern int GetMessage(out Message message,IntPtr window,uint min,uint max);
    [DllImport("user32.dll",EntryPoint="PostThreadMessageW")]private static extern bool PostThreadMessage(uint thread,uint message,UIntPtr w,IntPtr l);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll",EntryPoint="DispatchMessageW")]private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string? name);
    public MenuMouse(Func<Native.Point,bool> inside,Action dismiss)
    {
        dispatcher=Application.Current.Dispatcher;
        callback=(code,message,data)=>{
            if(code>=0&&!disposed&&message.ToInt32() is 0x201 or 0x204 or 0x207 or 0x20B)
            {
                var point=Marshal.PtrToStructure<Native.Point>(data);
                try
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>{
                        if(!disposed&&!inside(point))dismiss();
                    }));
                }
                catch(InvalidOperationException) { /* Dispatcher is shutting down. */ }
            }
            // Never consume the click or call a UI Automation provider here.
            return CallNextHookEx(handle,code,message,data);
        };
        var ready=new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            try
            {
                threadId=Native.GetCurrentThreadId();
                PeekMessage(out _,IntPtr.Zero,0,0,0); // Create the queue before Dispose can post WM_QUIT.
                if(disposed){ready.TrySetResult(null);return;}
                handle=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
                if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                ready.TrySetResult(null);
                while(!disposed&&GetMessage(out var message,IntPtr.Zero,0,0)>0)
                {
                    TranslateMessage(ref message);DispatchMessage(ref message);
                }
            }
            catch(Exception e){ready.TrySetResult(e);}
            finally {if(handle!=IntPtr.Zero){UnhookWindowsHookEx(handle);handle=IntPtr.Zero;}}
        }) {IsBackground=true,Name="PromptFloat.MenuMouse"};
        thread.Start();
        if(!ready.Task.Wait(TimeSpan.FromMilliseconds(900))){Dispose();throw new TimeoutException("菜单点击监听启动超时。");}
        if(ready.Task.Result is Exception error){Dispose();throw error;}
    }
    public void Dispose()
    {
        disposed=true;
        if(threadId!=0)PostThreadMessage(threadId,0x12,UIntPtr.Zero,IntPtr.Zero);
    }
}
