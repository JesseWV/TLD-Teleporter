namespace Teleporter;

[HarmonyPatch(typeof(LoadScene), nameof(LoadScene.SerializeAll))]
internal static class SerializationPatch
{

    internal static bool HasFired { get; private set; }

    private static LoadScene? _withheld;
    private static int _index = -1;

    private static void Prefix()
    {
        HasFired = true;
        _withheld = null;
        _index = -1;

        var registry = LoadScene.m_LoadScenesList;
        var portal = Portal.LiveTrigger;
        if (registry == null || portal == null) return;

        for (int i = registry.Count - 1; i >= 0; i--)
        {
            if (registry[i] != null && registry[i].Pointer == portal.Pointer)
            {
                _withheld = portal;
                _index = i;
                registry.RemoveAt(i);
                Log.Msg("[portal] held out of the departure scene's saved door list.");
                break;
            }
        }
    }

    private static void Postfix()
    {
        var registry = LoadScene.m_LoadScenesList;
        if (_withheld == null || registry == null) { _withheld = null; return; }

        if (_index >= 0 && _index <= registry.Count) registry.Insert(_index, _withheld);
        else registry.Add(_withheld);

        _withheld = null;
        _index = -1;
    }
}
