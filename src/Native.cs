using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmgAd;
public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] public struct POINT {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct SIZE {public int Width,Height;}
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct BLEND {public byte Operation,Flags,Alpha,Format;}
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h,ref POINT p);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h,uint affinity);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dest,ref POINT pos,ref SIZE size,IntPtr source,ref POINT origin,uint key,ref BLEND blend,uint flags);
    public static bool PaintLayered(IntPtr handle,Bitmap bitmap,Point location)
    {
        var screen=GetDC(IntPtr.Zero);var memory=CreateCompatibleDC(screen);IntPtr image=IntPtr.Zero,previous=IntPtr.Zero;
        try
        {
            image=bitmap.GetHbitmap(Color.FromArgb(0));previous=SelectObject(memory,image);
            var pos=new POINT{X=location.X,Y=location.Y};var origin=new POINT();
            var size=new SIZE{Width=bitmap.Width,Height=bitmap.Height};var blend=new BLEND{Alpha=255,Format=1};
            return UpdateLayeredWindow(handle,screen,ref pos,ref size,memory,ref origin,0,ref blend,2);
        }
        finally
        {
            if(previous!=IntPtr.Zero)SelectObject(memory,previous);
            if(image!=IntPtr.Zero)DeleteObject(image);
            if(memory!=IntPtr.Zero)DeleteDC(memory);
            if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);
        }
    }
    public static IntPtr FindDota()
    {
        foreach(var p in Process.GetProcessesByName("dota2"))using(p){if(p.MainWindowHandle!=IntPtr.Zero)return p.MainWindowHandle;}
        return IntPtr.Zero;
    }
    public static Rectangle Bounds(IntPtr handle)
    {
        if(!GetClientRect(handle,out var r))return Rectangle.Empty;
        var p=new POINT();ClientToScreen(handle,ref p);return new Rectangle(p.X,p.Y,r.Right-r.Left,r.Bottom-r.Top);
    }
    public static Bitmap Capture(Rectangle bounds)
    {
        var bmp=new Bitmap(bounds.Width,bounds.Height);
        using var g=Graphics.FromImage(bmp);g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);return bmp;
    }
}
