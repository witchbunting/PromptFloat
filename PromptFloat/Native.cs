using System.Runtime.InteropServices;
using System.Text;
namespace PromptFloat;

internal static class Native
{
    public const int WM_HOTKEY=0x312, WM_MOUSEACTIVATE=0x21, MA_NOACTIVATE=3;
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; public int Width=>Right-Left;public int Height=>Bottom-Top; }
    [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] internal struct Input {public uint Type;public InputUnion Data;}
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion {[FieldOffset(0)] public KeyboardInput Keyboard;[FieldOffset(0)]public MouseInput Mouse;}
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput {public ushort Vk,Scan;public uint Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    public delegate void WinEventDelegate(IntPtr hook,uint eventType,IntPtr window,int obj,int child,uint thread,uint time);
    private delegate bool EnumWindowDelegate(IntPtr window,IntPtr parameter);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumWindowDelegate callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(IntPtr window,StringBuilder text,int capacity);
    internal static IntPtr TestWindowByTitle(string title)
    {
        var result=IntPtr.Zero;EnumWindows((window,_)=>{var text=new StringBuilder(1024);GetWindowText(window,text,1024);if(text.ToString().Contains(title)){result=window;return false;}return true;},IntPtr.Zero);return result;
    }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]private static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]private static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
    internal static void PreventMouseActivation(IntPtr hwnd)
    {
        var style=GetWindowLongPtr(hwnd,-20).ToInt64();
        Marshal.SetLastPInvokeError(0);
        if(SetWindowLongPtr(hwnd,-20,new IntPtr(style|0x08000000))==IntPtr.Zero&&Marshal.GetLastWin32Error()!=0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr h,ref MonitorInfo info);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h,StringBuilder name,int count);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventDelegate callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("advapi32.dll",SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] private static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr info,int length,out int required);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid,uint index);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]private static extern bool CloseClipboard();
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SetClipboardData(uint format,IntPtr memory);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr GlobalAlloc(uint flags,UIntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")]private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")]private static extern IntPtr GlobalFree(IntPtr memory);
    public static void WriteClipboard(IntPtr owner,string text)
    {
        if(!OpenClipboard(owner))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"剪贴板正被其他程序使用。");
        try
        {
            if(!EmptyClipboard())throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Write(13,Encoding.Unicode.GetBytes(text+"\0"));
            foreach(var name in new[]{"CanIncludeInClipboardHistory","CanUploadToCloudClipboard"})
            {
                var format=RegisterClipboardFormat(name);if(format==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());Write(format,new byte[4]);
            }
        }
        finally{CloseClipboard();}
        static void Write(uint format,byte[] bytes)
        {
            var memory=GlobalAlloc(2,(UIntPtr)bytes.Length);if(memory==IntPtr.Zero)throw new OutOfMemoryException();
            try
            {
                var pointer=GlobalLock(memory);if(pointer==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try{Marshal.Copy(bytes,0,pointer,bytes.Length);}finally{GlobalUnlock(memory);}
                if(SetClipboardData(format,memory)==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                memory=IntPtr.Zero; // Ownership transferred to Windows; never free it here.
            }
            finally{if(memory!=IntPtr.Zero)GlobalFree(memory);}
        }
    }
    public static int Pid(IntPtr hwnd) {GetWindowThreadProcessId(hwnd,out var pid);return (int)pid;}
    public static string WindowClass(IntPtr hwnd) {var text=new StringBuilder(256);GetClassName(hwnd,text,256);return text.ToString();}
    public static Rect WorkArea(IntPtr hwnd) {var m=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};GetMonitorInfo(MonitorFromWindow(hwnd,2),ref m);return m.Work;}
    public static void Move(Window window,int x,int y) => SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,x,y,0,0,0x0015);
    public static Rect Bounds(Window window) {GetWindowRect(new WindowInteropHelper(window).Handle,out var rect);return rect;}
    public static bool IsExternal(IntPtr hwnd) => hwnd!=IntPtr.Zero && Pid(hwnd)!=Environment.ProcessId;
    public static int Integrity(int pid)
    {
        var process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)return -1;
        IntPtr buffer=IntPtr.Zero,token=IntPtr.Zero;
        try {if(!OpenProcessToken(process,8,out token))return -1;GetTokenInformation(token,25,IntPtr.Zero,0,out var needed);if(needed==0)return -1;buffer=Marshal.AllocHGlobal(needed);if(!GetTokenInformation(token,25,buffer,needed,out _))return -1;var sid=Marshal.ReadIntPtr(buffer);var count=Marshal.ReadByte(GetSidSubAuthorityCount(sid));return Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)(count-1)));}
        finally {if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(token!=IntPtr.Zero)CloseHandle(token);CloseHandle(process);}
    }
    public static bool Paste()=>CtrlKey(0x56);
    internal static bool CtrlKey(ushort key)
    {
        var keys=new[]{
            new Input {Type=1,Data=new InputUnion {Keyboard=new KeyboardInput {Vk=0x11}}},
            new Input {Type=1,Data=new InputUnion {Keyboard=new KeyboardInput {Vk=key}}},
            new Input {Type=1,Data=new InputUnion {Keyboard=new KeyboardInput {Vk=key,Flags=2}}},
            new Input {Type=1,Data=new InputUnion {Keyboard=new KeyboardInput {Vk=0x11,Flags=2}}}
        };
        return SendInput((uint)keys.Length,keys,Marshal.SizeOf<Input>())==keys.Length;
    }
    internal static bool TestKey(ushort key,bool shift=false)
    {
        var inputs=new List<Input>();
        void Add(ushort vk,uint flags=0)=>inputs.Add(new Input {Type=1,Data=new InputUnion {Keyboard=new KeyboardInput {Vk=vk,Flags=flags|(vk is >=0x21 and <=0x28?1u:0u)}}});
        if(shift)Add(0x10);Add(key);Add(key,2);if(shift)Add(0x10,2);return SendInput((uint)inputs.Count,inputs.ToArray(),Marshal.SizeOf<Input>())==inputs.Count;
    }
    internal static bool TestClick(IntPtr window,int x,int y,bool restoreCursor=true)
    {
        var point=new Point {X=x,Y=y};if(GetAncestor(WindowFromPoint(point),2)!=GetAncestor(window,2))return false;
        GetCursorPos(out var previous);SetCursorPos(x,y);
        var inputs=new[]{new Input {Type=0,Data=new InputUnion {Mouse=new MouseInput {Flags=2}}},new Input {Type=0,Data=new InputUnion {Mouse=new MouseInput {Flags=4}}}};
        var ok=SendInput(2,inputs,Marshal.SizeOf<Input>())==2;if(restoreCursor)SetCursorPos(previous.X,previous.Y);return ok;
    }
    internal static async Task<bool> TestDragAsync(IntPtr window,int x,int y,int dx,int dy)
    {
        if(GetAncestor(WindowFromPoint(new Point {X=x,Y=y}),2)!=GetAncestor(window,2))return false;
        GetCursorPos(out var previous);var held=false;
        bool Button(uint flags)=>SendInput(1,[new Input {Type=0,Data=new InputUnion {Mouse=new MouseInput {Flags=flags}}}],Marshal.SizeOf<Input>())==1;
        try
        {
            SetCursorPos(x,y);if(!Button(2))return false;held=true;await Task.Delay(60);
            for(var step=1;step<=6;step++){SetCursorPos(x+dx*step/6,y+dy*step/6);await Task.Delay(25);}
            if(!Button(4))return false;held=false;await Task.Delay(90);return true;
        }
        finally{if(held)Button(4);SetCursorPos(previous.X,previous.Y);}
    }
    internal static void TestRestoreCursor(Point point)=>SetCursorPos(point.X,point.Y);
    public static (uint modifiers,uint key) ParseHotkey(string text)
    {
        uint modifiers=0x4000,key=0;
        foreach(var piece in text.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
        {
            switch(piece.ToUpperInvariant()) {case "CTRL":modifiers|=2;break;case "ALT":modifiers|=1;break;case "SHIFT":modifiers|=4;break;case "WIN":modifiers|=8;break;
                default: if(key!=0)throw new ArgumentException("快捷键只能包含一个主键。");var converted=(Key)new KeyConverter().ConvertFromString(piece)!;key=(uint)KeyInterop.VirtualKeyFromKey(converted);break;}
        }
        if(key==0||(modifiers&15)==0)throw new ArgumentException("快捷键需包含 Ctrl、Alt、Shift 或 Win 及一个主键。");return(modifiers,key);
    }
}
