using System.Runtime.InteropServices;

namespace OmgAd;

public static class PlayerAreas
{
    public static RectangleF Card(int seat,Size size)=>new((seat<5?122f:1281f)/1676*size.Width,
        (128f+seat%5*147)/942*size.Height,274f/1676*size.Width,131f/942*size.Height);
    public static int Hit(Point point,Size size)
    {
        for(int seat=0;seat<10;seat++)
        {
            var r=Card(seat,size);
            // Name / ID area and portrait only: clicking a drafted skill keeps the current view.
            if(r.Contains(point)&&(point.Y<r.Top+76f/942*size.Height||
                (seat<5?point.X<r.Left+67f/1676*size.Width:point.X>r.Right-67f/1676*size.Width)))return seat;
        }
        return -1;
    }
    public static int HitScreen(Point point,Rectangle viewport)=>Hit(new Point(point.X-viewport.X,point.Y-viewport.Y),viewport.Size);
}

// Observes clicks and always forwards them to Windows/Dota; never steals focus or consumes input.
public sealed class PlayerClickListener:IDisposable
{
    delegate IntPtr MouseProc(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct MouseData {public Native.POINT Point;public uint Mouse,Flags,Time;public UIntPtr Extra;}
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,MouseProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    readonly MouseProc callback;IntPtr hook;
    public event Action<Point>? Clicked;
    public bool Installed=>hook!=IntPtr.Zero;
    public PlayerClickListener(){callback=OnMouse;hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);}
    IntPtr OnMouse(int code,IntPtr message,IntPtr data)
    {
        if(code>=0&&message.ToInt32()==0x0202)
        {
            var m=Marshal.PtrToStructure<MouseData>(data);
            if((m.Flags&1)==0)try{Clicked?.Invoke(new Point(m.Point.X,m.Point.Y));}catch { /* Do not disrupt the game's mouse path. */ }
        }
        return CallNextHookEx(hook,code,message,data);
    }
    public void Dispose(){if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}}
}
