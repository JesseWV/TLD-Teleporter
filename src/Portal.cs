using System.Collections.Generic;

namespace Teleporter;

internal static class Portal
{
    private const string ObjectName = "TLDTeleporter_Portal";

    private static GameObject? _live;

    private static string _pendingGuid = "";

    private static int _armingFrames;

    private const int ArmingLimit = 120;

    internal static bool IsLive => _live != null;

    internal static LoadScene? LiveTrigger => _live == null ? null : _live.GetComponent<LoadScene>();

    internal static bool Spawn(Destination dest, out string message)
    {

        if (SeamlessInteriorsCompat.Handles(dest)) return SeamlessEntry.Begin(dest, out message);

        Despawn();

        var player = GameManager.GetPlayerTransform();
        if (player == null) { message = "No player transform; cannot place a portal."; return false; }

        string? hijack = SeamlessHijack(dest, player.position);
        if (hijack != null) { message = $"Cannot travel to {dest.Display}: {hijack}"; return false; }

        string keyedGuid = "";
        if (dest.CanBeInstanced)
        {

            if (dest.Building < 0 || dest.Building >= Destinations.Buildings.Length)
            {
                message = $"Cannot travel to {dest.Display}: the destination table names no building for it.";
                return false;
            }
            var building = Destinations.Buildings[dest.Building];
            string? refusal = SaveDoorLookup.Resolve(building, out keyedGuid);
            if (refusal != null) { message = $"Cannot travel to {dest.Display}: {refusal}"; return false; }
            Arrival.Expect(dest, building);
        }

        var go = new GameObject(ObjectName);

        var reference = FirstLiveTrigger();
        if (reference != null) go.layer = reference.gameObject.layer;

        go.transform.position = player.position;

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(3f, 3f, 3f);
        box.enabled = keyedGuid.Length == 0;

        var ls = go.AddComponent<LoadScene>();
        ls.m_SceneToLoad = dest.Scene;
        ls.m_ExitPointName = dest.ExitPoint;
        ls.m_SceneCanBeInstanced = dest.CanBeInstanced;
        ls.m_TransitionOnContact = true;
        ls.m_Active = true;

        _live = go;
        _pendingGuid = keyedGuid;
        _armingFrames = 0;
        message = $"Portal open to {dest.Display} [{dest.Scene}], arriving at {dest.ExitPoint}.";
        Log.Msg($"[portal] -> {dest.Scene}/{dest.ExitPoint} instanced={dest.CanBeInstanced}"
                        + (keyedGuid.Length > 0 ? $" key={keyedGuid} (arming)" : ""));
        return true;
    }

    private static string? SeamlessHijack(Destination dest, Vector3 at)
    {
        foreach (var id in SeamlessInteriorsCompat.IdsFor(dest.Scene, SeamlessEntry.CurrentRegion()))
        {
            var root = SeamlessInteriorsCompat.InteriorRoot(id);
            if (root == null) continue;
            var p = root.transform.position;
            float dx = p.x - at.x, dz = p.z - at.z;
            if (dx * dx + dz * dz < 40f * 40f)
                return $"you are within 40 m of {id}, which Seamless Interiors would take the portal for the door of; step away first";
        }
        return null;
    }

    internal static void Arm()
    {
        if (_pendingGuid.Length == 0 || _live == null) return;

        var ls = _live.GetComponent<LoadScene>();
        var box = _live.GetComponent<BoxCollider>();
        if (ls == null || box == null) { Despawn(); Arrival.Forget(); return; }

        if (string.IsNullOrEmpty(ls.GetGUID()))
        {
            if (++_armingFrames <= ArmingLimit) return;
            Log.Warning($"[portal] gave up after {ArmingLimit} frames: the portal never got a GUID of its own, so the real building's key could not be set.");
            Despawn();
            Arrival.Forget();
            return;
        }

        string generated = ls.GetGUID();
        string wanted = _pendingGuid;
        _pendingGuid = "";

        ls.m_GUID = wanted;
        string readBack = ls.GetGUID();
        if (readBack != wanted)
        {

            Log.Error($"[portal] refusing to open: the key did not take (wanted {wanted}, read {readBack}).");
            Despawn();
            Arrival.Forget();
            return;
        }

        box.enabled = true;
        Log.Msg($"[portal] armed after {_armingFrames} frames: replaced generated {generated} with {readBack}.");
    }

    internal static int Despawn()
    {
        int removed = PurgeRegistryNulls(_live);
        if (_live != null)
        {
            UnityEngine.Object.Destroy(_live);
            _live = null;
        }
        _pendingGuid = "";
        _armingFrames = 0;
        return removed;
    }

    private static int PurgeRegistryNulls(GameObject? ours)
    {
        var registry = LoadScene.m_LoadScenesList;
        if (registry == null) return 0;

        int removed = 0;

        for (int i = registry.Count - 1; i >= 0; i--)
        {
            var entry = registry[i];
            if (entry == null)
            {
                registry.RemoveAt(i);
                removed++;
                continue;
            }
            if (ours != null && entry.gameObject == ours)
            {
                registry.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    private static LoadScene? FirstLiveTrigger()
    {
        var registry = LoadScene.m_LoadScenesList;
        if (registry == null) return null;
        for (int i = 0; i < registry.Count; i++)
        {
            var ls = registry[i];
            if (ls != null && ls.gameObject != null && ls.gameObject.name != ObjectName) return ls;
        }
        return null;
    }

    internal static bool DepartureIsInstanced()
    {

        if (SeamlessInteriorsCompat.PlayerInteriorId() != null) return false;

        var td = GameManager.m_SceneTransitionData;
        if (td == null) return false;

        string saveName = td.m_SceneSaveFilenameCurrent;
        string sceneName = GameManager.m_ActiveScene;
        if (string.IsNullOrEmpty(saveName) || string.IsNullOrEmpty(sceneName)) return false;

        return saveName != sceneName;
    }
}
