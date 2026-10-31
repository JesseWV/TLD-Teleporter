namespace Teleporter;

internal static class Picker
{
    private static bool _nguiFailed;
    private static int _closedByEscapeFrame = -1;
    private static int _lastBackFrame = -1;

    internal static int BackSeenFrame = -1;

    internal static bool IsOpen => NguiPicker.IsOpen || TeleporterPanel.IsOpen;

    internal static bool SwallowEscape => (IsOpen && !NguiPicker.IsConfirming) || _closedByEscapeFrame == Time.frameCount;

    internal static void Open()
    {

        string? busy = Portal.ActivityRefusal();
        if (busy != null) { HUDMessage.AddMessage(busy, false, false); Log.Msg("[picker] not opened: " + busy); return; }

        if (!_nguiFailed)
        {
            if (NguiPicker.TryOpen(out string? error)) return;
            _nguiFailed = true;
            Log.Error("[picker] NGUI picker could not open; using the Unity UI panel for this session. " + error);
        }
        TeleporterPanel.Open();
    }

    internal static void Close(bool byEscape = false)
    {
        if (byEscape) _closedByEscapeFrame = Time.frameCount;
        NguiPicker.Close();
        TeleporterPanel.Close();
    }

    internal static void Back()
    {

        if (Time.frameCount - _lastBackFrame <= 1) return;
        _lastBackFrame = Time.frameCount;
        if (NguiPicker.IsOpen && NguiPicker.Back()) return;
        Close(byEscape: true);
    }

    internal static void Update()
    {
        if (NguiPicker.IsOpen) NguiPicker.HandleInput();
    }
}
