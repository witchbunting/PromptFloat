using System.Windows.Threading;
namespace PromptFloat;

internal sealed class BallWindow : Window
{
    private readonly Host host;private readonly DispatcherTimer idle=new() {Interval=TimeSpan.FromSeconds(6)};
    private Native.Point down;private Native.Rect origin;private bool pressing,dragging,halfHidden;private int anchorX,anchorY;
    public BallWindow(Host host)
    {
        this.host=host;Title="PromptFloat";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;Focusable=false;
        var circle=new Border {Background=Ui.Brush("#4267E9"),CornerRadius=new CornerRadius(99),BorderBrush=Ui.Brush("#B8C7FF"),BorderThickness=new Thickness(1),Child=new TextBlock {Text="P",Foreground=Brushes.White,FontFamily=new FontFamily("Segoe UI"),FontSize=22,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},ToolTip="PromptFloat · Ctrl + Alt + P",Cursor=Cursors.Hand};Content=circle;
        SourceInitialized+=(_,_)=>{var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);Native.PreventMouseActivation(source.Handle);source.AddHook((IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)=>{if(msg==Native.WM_MOUSEACTIVATE){handled=true;return new IntPtr(Native.MA_NOACTIVATE);}return IntPtr.Zero;});ApplySettings();};
        Loaded+=(_,_)=>RestorePosition();
        MouseLeftButtonDown+=(_,e)=>{Reveal();Native.GetCursorPos(out down);origin=Native.Bounds(this);pressing=true;dragging=false;CaptureMouse();e.Handled=true;};
        MouseMove+=(_,_)=>{idle.Stop();idle.Start();if(!pressing)return;Native.GetCursorPos(out var current);var dx=current.X-down.X;var dy=current.Y-down.Y;if(Math.Abs(dx)+Math.Abs(dy)>5&&!dragging){dragging=true;host.Surfaces.BeginBallDrag();}if(dragging)Native.Move(this,origin.Left+dx,origin.Top+dy);};
        MouseLeftButtonUp+=async(_,e)=>{if(!pressing)return;pressing=false;ReleaseMouseCapture();if(dragging){Clamp(true);SavePosition();}else await host.TogglePanelAsync();e.Handled=true;};
        MouseEnter+=(_,_)=>{Reveal();idle.Stop();};MouseLeave+=(_,_)=>idle.Start();
        MouseRightButtonUp+=async(_,_)=>{Reveal();await host.Input.CaptureAsync(host.Store.Settings);var menu=host.Menu(this);menu.PlacementTarget=circle;menu.IsOpen=true;};
        idle.Tick+=(_,_)=>{idle.Stop();if(host.Store.Settings.HalfHide&&!IsMouseOver&&!host.IsPanelVisible)HalfHide();};
        Closing+=(_,e)=>{if(!host.Exiting){e.Cancel=true;Hide();}};
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplaysChanged;
    }
    public void ApplySettings(){Width=Height=host.Store.Settings.BallSize;Opacity=host.Store.Settings.Opacity;if(Content is Border circle)circle.ToolTip="PromptFloat · "+host.Store.Settings.PanelHotkey;}
    private void DisplaysChanged(object? sender,EventArgs e)=>Dispatcher.BeginInvoke(()=>{Reveal();Clamp(false);SavePosition();});
    private void RestorePosition()
    {
        var s=host.Store.Settings;var rect=Native.Bounds(this);var work=Native.WorkArea(new WindowInteropHelper(this).Handle);
        var saved=s.BallPositionSaved||s.BallX!=-1||s.BallY!=-1;
        Native.Move(this,saved?(int)s.BallX:work.Right-rect.Width-24,saved?(int)s.BallY:work.Bottom-rect.Height-80);Clamp(false);SavePosition();idle.Start();
    }
    public void Clamp(bool snap)
    {
        var rect=Native.Bounds(this);var work=Native.WorkArea(new WindowInteropHelper(this).Handle);var x=Math.Clamp(rect.Left,work.Left,Math.Max(work.Left,work.Right-rect.Width));var y=Math.Clamp(rect.Top,work.Top,Math.Max(work.Top,work.Bottom-rect.Height));
        if(snap&&host.Store.Settings.Snap){if(x-work.Left<24)x=work.Left;else if(work.Right-(x+rect.Width)<24)x=work.Right-rect.Width;}
        Native.Move(this,x,y);anchorX=x;anchorY=y;
    }
    private void SavePosition(){if(halfHidden)return;var rect=Native.Bounds(this);anchorX=rect.Left;anchorY=rect.Top;host.Store.Settings.BallX=anchorX;host.Store.Settings.BallY=anchorY;host.Store.Settings.BallPositionSaved=true;Ui.Run(()=>host.Store.SaveSettings(host.Store.Settings));}
    private void HalfHide(){var r=Native.Bounds(this);var area=Native.WorkArea(new WindowInteropHelper(this).Handle);anchorX=r.Left;anchorY=r.Top;if(r.Left==area.Left){Native.Move(this,r.Left-r.Width/2,r.Top);halfHidden=true;}else if(r.Right==area.Right){Native.Move(this,r.Left+r.Width/2,r.Top);halfHidden=true;}}
    public void Reveal(){if(halfHidden){Native.Move(this,anchorX,anchorY);halfHidden=false;}}
    public void DisposeEvents(){idle.Stop();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplaysChanged;}
}
