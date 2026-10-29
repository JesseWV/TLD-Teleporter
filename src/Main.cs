namespace Teleporter;

public sealed class Main : MelonMod
{

    private static bool _hotkeysDisabled;

    public override void OnInitializeMelon()
    {
        Log.Bind(LoggerInstance);
        Log.Msg(BuildInfo.Stamp);
        Log.Msg($"{Destinations.All.Length} destinations in the table.");
        TeleporterSettings.Instance.AddToModSettings("Teleporter");
    }

    public override void OnLateInitializeMelon()
    {

        SeamlessInteriorsCompat.Resolve();
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {

        Picker.Close();

        Arrival.OnSceneInitialized(sceneName);

        SeamlessEntry.OnSceneInitialized(sceneName);

        SaveDoorLookup.Forget();

        PlaceNames.Forget();

        Commands.Register();

        Portal.Despawn();
    }

    public override void OnUpdate()
    {
        try
        {
            Portal.Arm();
            Arrival.Update();
            SeamlessEntry.Update();
        }
        catch (System.Exception e)
        {
            Log.Error($"Teleporter arrival handling failed: {e}");
            Portal.Despawn();
            Arrival.Forget();
            SeamlessEntry.Forget();
        }

        if (_hotkeysDisabled) return;
        try
        {
            HandleHotkeys();
        }
        catch (System.Exception e)
        {
            _hotkeysDisabled = true;
            Log.Error($"Teleporter hotkeys disabled after an exception: {e}");
            Picker.Close();
        }
    }

    public override void OnApplicationQuit()
    {
        Portal.Despawn();
    }

    private static bool PlayerInterrupted()
    {
        var struggle = GameManager.GetPlayerStruggleComponent();
        if (struggle != null && (struggle.InStruggle() || struggle.IsPlayerDeadOrDying())) return true;
        var player = GameManager.GetPlayerManagerComponent();
        return player != null && player.PlayerIsDead();
    }

    private static void HandleHotkeys()
    {
        KeyCode key = TeleporterSettings.Instance.openKey;
        var context = InputManager.m_CurrentContext;

        bool chord = PickerCommon.OpenModifiersHeld();

        if (Picker.IsOpen)
        {
            if (!GameManager.HasPlayerObject() || GameManager.IsMainMenuActive()) { Picker.Close(); return; }

            if (PlayerInterrupted()) { Log.Msg("[picker] closed: the player is in a struggle or dead."); Picker.Close(); return; }

            if (InputManager.GetKeyDown(context, KeyCode.Escape) || Picker.BackSeenFrame >= Time.frameCount - 1)
            {
                Picker.BackSeenFrame = -1;
                Picker.Back();
                return;
            }
            if (key != KeyCode.None && chord && InputManager.GetKeyDown(context, key)) { Picker.Close(); return; }
            Picker.Update();
            return;
        }

        if (key == KeyCode.None) return;
        if (!GameManager.HasPlayerObject() || GameManager.IsMainMenuActive() || string.IsNullOrEmpty(GameManager.m_ActiveScene)) return;

        if (InterfaceManager.IsOverlayActiveCached() || uConsole.IsOn()) return;
        if (chord && InputManager.GetKeyDown(context, key)) Picker.Open();
    }
}
