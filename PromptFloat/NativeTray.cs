using System.Runtime.InteropServices;
namespace PromptFloat;

internal sealed class NativeTray : IDisposable
{
    internal const int Callback=0x8031;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct IconData
    {
        public uint Size;public IntPtr Window;public uint Id,Flags,CallbackMessage;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern bool Shell_NotifyIcon(uint action,ref IconData data);
    [DllImport("user32.dll")]private static extern IntPtr CreateIcon(IntPtr instance,int width,int height,byte planes,byte bits,byte[] and,byte[] xor);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string message);
    private IconData data;private readonly Action show,menu;
    public uint ExplorerRestartMessage {get;}=RegisterWindowMessage("TaskbarCreated");
    public NativeTray(IntPtr window,Action show,Action menu)
    {
        this.show=show;this.menu=menu;var and=new byte[128];var xor=new byte[4096];
        for(var y=0;y<32;y++)for(var x=0;x<32;x++)
        {
            var inside=(x-15.5)*(x-15.5)+(y-15.5)*(y-15.5)<=15*15;
            if(!inside){and[y*4+x/8]|=(byte)(0x80>>(x%8));continue;}
            var p=(x>=9&&x<=12&&y>=7&&y<=25)||(x>=12&&x<=21&&(y>=7&&y<=10||y>=14&&y<=17))||(x>=19&&x<=22&&y>=9&&y<=15);
            var i=(y*32+x)*4;xor[i]=(byte)(p?255:233);xor[i+1]=(byte)(p?255:103);xor[i+2]=(byte)(p?255:66);xor[i+3]=255;
        }
        data=new IconData {Size=(uint)Marshal.SizeOf<IconData>(),Window=window,Id=1,Flags=7,CallbackMessage=Callback,Icon=CreateIcon(IntPtr.Zero,32,32,1,32,and,xor),Tip="PromptFloat · 本地提示词助手",Info="",InfoTitle=""};Add();
    }
    public void Add()=>Shell_NotifyIcon(0,ref data);
    public void Message(IntPtr parameter)
    {
        switch((int)(parameter.ToInt64()&0xffff)){case 0x203:show();break;case 0x205:case 0x7b:menu();break;}
    }
    public void Notice(string text)
    {
        data.Flags=16;data.InfoTitle="PromptFloat";data.Info=text.Length>250?text[..250]:text;data.InfoFlags=1;data.TimeoutOrVersion=2500;Shell_NotifyIcon(1,ref data);data.Flags=7;
    }
    public void Dispose(){Shell_NotifyIcon(2,ref data);if(data.Icon!=IntPtr.Zero)Native.DestroyIcon(data.Icon);}
}
