namespace Teleporter;

internal sealed class TeleporterSettings : JsonModSettings
{
    internal static readonly TeleporterSettings Instance = new();

    [Name("Open teleporter key")]
    [Description("Opens the destination picker. N is unbound in vanilla; change it if your key bindings or another mod use N.")]
    public KeyCode openKey = KeyCode.N;

    [Name("Open teleporter modifier")]
    [Description("Keys to hold with the key above, if it clashes with another binding or mod. None by default. Exactly these modifiers must be held, no others. None uses the key by itself.")]
    public OpenModifier openModifier = OpenModifier.None;
}

internal enum OpenModifier { None, Ctrl, Shift, Alt, CtrlShift, CtrlAlt, ShiftAlt }
