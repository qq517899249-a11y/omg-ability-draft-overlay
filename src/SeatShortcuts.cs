namespace OmgAd;
public static class SeatShortcuts
{
    public const uint Modifiers=0x4003; // Ctrl + Alt + no repeat.
    public static Keys Key(int seat)=>seat==9?Keys.D0:Keys.D1+seat;
    public static string Label(int seat)=>$"Ctrl+Alt+{(seat==9?0:seat+1)}";
    public static int Seat(int hotkeyId)=>hotkeyId is >=100 and <=109?hotkeyId-100:-1;
}
