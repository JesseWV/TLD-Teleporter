namespace Teleporter;

internal static class PickerCommon
{
    internal const string InstancedWarning =
        "You can't travel from inside this building. Step outside first.";

    private static bool _controlsLocked;
    private static PlayerControlMode _previousControlMode;

    internal static PlayerControlMode? ModeBeforeOurLock => _controlsLocked ? _previousControlMode : null;

    internal static void LockControls()
    {
        if (_controlsLocked) return;
        var pm = GameManager.GetPlayerManagerComponent();
        if (pm == null) return;
        _previousControlMode = pm.GetControlMode();
        pm.SetControlMode(PlayerControlMode.Locked);
        _controlsLocked = true;
    }

    internal static void UnlockControls()
    {
        if (!_controlsLocked) return;
        _controlsLocked = false;
        var pm = GameManager.GetPlayerManagerComponent();

        if (pm != null && pm.GetControlMode() == PlayerControlMode.Locked)
            pm.SetControlMode(_previousControlMode);
    }

    internal static bool IsOutdoor(Destination d) => d.Regions.Length == 1 && d.Regions[0] == d.Scene;

    internal static string RegionDisplay(string? scene)
    {
        foreach (var (s, display) in Destinations.RegionList)
            if (s == scene) return display;
        return scene ?? "";
    }

    internal static string? RegionPlayerIsIn()
    {
        var td = GameManager.m_SceneTransitionData;
        foreach (var candidate in new[] { GameManager.m_ActiveScene, td?.m_SceneSaveFilenameCurrent, td?.m_LastOutdoorScene })
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            foreach (var (scene, _) in Destinations.RegionList)
                if (scene == candidate) return scene;
        }
        return null;
    }

    internal static string RegionForCurrentScene(string? fallback)
    {
        string here = GameManager.m_ActiveScene;
        string? region = RegionPlayerIsIn();
        if (region != null) return region;
        foreach (var d in Destinations.All)
            if (d.Scene == here && d.Regions.Length > 0) return d.Regions[0];
        return fallback ?? Destinations.RegionList[0].Scene;
    }

    internal static string? RefusalFromHere(Destination dest)
    {
        string? departure = Portal.DepartureRefusal();
        if (departure != null) return departure;

        if (!IsOutdoor(dest) && AlreadyInside(dest))
            return $"You are already in {dest.PlaceDisplay}.";
        return null;
    }

    internal static bool AlreadyInside(Destination dest)
    {
        string? inside = SeamlessInteriorsCompat.PlayerInteriorId();
        if (inside == null) return dest.Scene == GameManager.m_ActiveScene;
        return SeamlessInteriorsCompat.Handles(dest)
               && SeamlessInteriorsCompat.SceneOf(inside) == dest.Scene
               && SeamlessInteriorsCompat.IdsFor(dest.Scene, SeamlessEntry.CurrentRegion()).Count == 1;
    }

    internal const string ConfirmKey = "GAMEPLAY_Confirm", CancelKey = "GAMEPLAY_Cancel";

    internal static bool ConfirmAndTravel(Destination dest, System.Action? onYes = null, System.Action? onNo = null)
    {

        bool vanilla = SeamlessInteriorsCompat.VanillaMode && SeamlessInteriorsCompat.IsSeamlessBuilding(dest);
        var confirm = InterfaceManager.GetPanel<Panel_Confirmation>();
        if (confirm == null)
        {
            Log.Warning("[picker] Panel_Confirmation not found; not travelling.");
            return false;
        }
        string question = IsOutdoor(dest) ? $"Travel to {RegionDisplay(dest.Scene)}, {dest.Display}?" : $"Travel to {dest.Display}?";

        if (SeamlessInteriorsCompat.Present && !IsOutdoor(dest) && dest.Place != PickerModel.PathsKey)
            question = (SeamlessInteriorsCompat.IsSeamlessBuilding(dest) && !vanilla ? "Outdoor Seamless Interior" : "Vanilla Interior Scene") + "\n" + question;
        confirm.ShowConfirmPanel(question, ConfirmKey, CancelKey,
            (System.Action)(() =>
            {
                RestoreConfirmBox();
                onYes?.Invoke();
                SeamlessInteriorsCompat.VanillaMode = vanilla;
                try { Travel(dest); }
                finally { SeamlessInteriorsCompat.VanillaMode = false; }
            }),

            (System.Action<bool>)(_ => { RestoreConfirmBox(); SeamlessInteriorsCompat.VanillaMode = false; onNo?.Invoke(); }));
        SolidConfirmBox(confirm);
        return true;
    }

    private static UISprite? _confirmBox;
    private static float _confirmBoxAlpha;

    private static void SolidConfirmBox(Panel_Confirmation confirm)
    {
        RestoreConfirmBox();
        var parent = confirm.m_GenericMessageGroup?.m_Parent;
        if (parent == null) return;
        foreach (var sprite in parent.GetComponentsInChildren<UISprite>(true))
        {
            if (sprite.name != "Background" || sprite.spriteName != "inv_statBack") continue;
            _confirmBox = sprite;
            _confirmBoxAlpha = sprite.alpha;
            sprite.alpha = 1f;
            return;
        }
        Log.Warning("[picker] the confirmation dialog's box was not found; it stays see-through.");
    }

    internal static void RestoreConfirmBox()
    {
        if (_confirmBox == null) return;
        try { _confirmBox.alpha = _confirmBoxAlpha; }
        catch (System.Exception e) { Log.Warning($"[picker] could not restore the confirmation box: {e.Message}"); }
        _confirmBox = null;
    }

    internal static bool CtrlDown => UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);

    internal static bool ShiftDown => UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
    internal static bool AltDown => UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);

    internal static bool OpenModifiersHeld()
    {
        var m = TeleporterSettings.Instance.openModifier;
        bool ctrl = m is OpenModifier.Ctrl or OpenModifier.CtrlShift or OpenModifier.CtrlAlt;
        bool shift = m is OpenModifier.Shift or OpenModifier.CtrlShift or OpenModifier.ShiftAlt;
        bool alt = m is OpenModifier.Alt or OpenModifier.CtrlAlt or OpenModifier.ShiftAlt;
        return CtrlDown == ctrl && ShiftDown == shift && AltDown == alt;
    }

    private static void Travel(Destination dest)
    {

        if (!GameManager.HasPlayerObject() || GameManager.IsMainMenuActive() || string.IsNullOrEmpty(GameManager.m_ActiveScene)) return;
        string? departure = Portal.DepartureRefusal();
        if (departure != null) { Log.Warning($"[picker] refused {dest.Scene}/{dest.ExitPoint}: {departure}"); return; }
        string? refusal = DestinationCatalog.RejectionReason(dest);
        if (refusal != null)
        {
            Log.Warning($"[picker] refused {dest.Scene}/{dest.ExitPoint}: {refusal}");
            return;
        }

        if (Portal.Spawn(dest, out string message)) Log.Msg("[picker] " + message);
        else Log.Warning("[picker] " + message);
    }
}
