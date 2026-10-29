namespace Teleporter;

internal static class SeamlessEntry
{
    private enum Stage { None, Travelling, Entering, Verifying }

    private const float TravelSeconds = 180f;
    private const float EnterSeconds = 120f;
    private const float VerifySeconds = 3f;

    private const float DoorMatchMeters = 30f;

    private static Stage _stage;
    private static Destination _dest;
    private static string _region = "";
    private static string _targetId = "";
    private static float _deadline;

    internal static bool IsActive => _stage != Stage.None;

    internal static bool Begin(Destination dest, out string message)
    {
        Forget();
        string? region = SeamlessInteriorsCompat.HandledRegion(dest);
        if (region == null) { message = $"Cannot travel to {dest.Display}: Seamless Interiors does not handle it."; return false; }

        _dest = dest;
        _region = region;

        if (CurrentRegion() == region)
        {
            StartEntering();
            message = $"Entering {dest.Display} through its door.";
            Log.Msg($"[seamless] {dest.Scene}/{dest.ExitPoint}: already in {region}; entering through the door.");
            return true;
        }

        var leg = OutdoorLeg(dest, region);
        if (leg == null)
        {
            Forget();
            message = $"Cannot travel to {dest.Display}: no outdoor arrival in {region} can be offered.";
            return false;
        }
        if (!Portal.Spawn(leg.Value, out string portalMessage))
        {
            Forget();
            message = $"Cannot travel to {dest.Display}: {portalMessage}";
            return false;
        }

        _stage = Stage.Travelling;
        _deadline = Time.realtimeSinceStartup + TravelSeconds;
        message = $"Portal open to {leg.Value.Display} in {region}; {dest.Display} will be entered through its door on arrival.";
        Log.Msg($"[seamless] {dest.Scene}/{dest.ExitPoint}: first leg to {leg.Value.Scene}/{leg.Value.ExitPoint}.");
        return true;
    }

    internal static void Forget()
    {
        _stage = Stage.None;
        _region = "";
        _targetId = "";
    }

    internal static void OnSceneInitialized(string sceneName)
    {

        if (_stage == Stage.Travelling && sceneName == _region) StartEntering();
    }

    internal static void Update()
    {
        if (_stage == Stage.None) return;
        float now = Time.realtimeSinceStartup;

        if (!GameManager.HasPlayerObject() || GameManager.IsMainMenuActive())
        {
            if (GameManager.IsMainMenuActive()) Forget();
            return;
        }

        if (now > _deadline)
        {
            Log.Warning(_stage == Stage.Verifying
                ? $"[seamless] performed the door of {_targetId}, but Seamless Interiors reports the player in '{SeamlessInteriorsCompat.PlayerInteriorId()}'; not retrying."
                : $"[seamless] gave up on {_dest.Scene} in {_region} at stage {_stage}; the player stays where they are.");
            Forget();
            return;
        }

        switch (_stage)
        {
            case Stage.Entering:
                TryEnter(now);
                break;
            case Stage.Verifying:
                if (SeamlessInteriorsCompat.PlayerInteriorId() == _targetId)
                {
                    Log.Msg($"[seamless] inside {_targetId}, confirmed by Seamless Interiors.");
                    Forget();
                }
                break;
        }
    }

    private static void StartEntering()
    {
        _stage = Stage.Entering;
        _deadline = Time.realtimeSinceStartup + EnterSeconds;
    }

    private static void TryEnter(float now)
    {
        if (CurrentRegion() != _region) return;

        if (!SeamlessInteriorsCompat.InteriorsReady()) return;

        string? refusal = FindDoor(out var door, out _targetId);
        if (refusal != null || door == null)
        {
            Log.Warning($"[seamless] cannot enter {_dest.Display}: {refusal}");
            Forget();
            return;
        }

        var p = door.transform.position;
        Log.Msg($"[seamless] performing door {door.GetInstanceID()} -> {door.m_SceneToLoad}/{door.m_ExitPointName} at X {p.x:F1}, Z {p.z:F1} for {_targetId}.");
        _stage = Stage.Verifying;
        _deadline = now + VerifySeconds;
        door.PerformInteraction();
    }

    private static string? FindDoor(out LoadScene? door, out string targetIdOut)
    {
        door = null;
        targetIdOut = "";
        string targetId = "";

        var ids = SeamlessInteriorsCompat.IdsFor(_dest.Scene, _region);
        var roots = new List<(string Id, Vector3 Position)>();
        foreach (var id in ids)
        {
            var root = SeamlessInteriorsCompat.InteriorRoot(id);
            if (root != null) roots.Add((id, root.transform.position));
        }
        if (roots.Count == 0) return $"Seamless Interiors has not built {_dest.Scene} in {_region}";

        if (roots.Count == 1 && ids.Count == 1)
        {
            targetId = roots[0].Id;
        }
        else
        {
            if (_dest.Building < 0 || _dest.Building >= Destinations.Buildings.Length)
                return $"{ids.Count} Seamless Interiors buildings use {_dest.Scene} and the destination names none of them";
            var tableDoor = Destinations.Buildings[_dest.Building].Doors[0];
            var at = new Vector3(tableDoor.X, 0f, tableDoor.Z);
            float best = DoorMatchMeters;
            foreach (var (id, pos) in roots)
            {
                float d = FlatDistance(pos, at);
                if (d < best) { best = d; targetId = id; }
            }
            if (targetId.Length == 0) return $"no Seamless Interiors building using {_dest.Scene} stands within {DoorMatchMeters} m of the destination's door";
        }

        Vector3 target = roots.First(r => r.Id == targetId).Position;
        var portal = Portal.LiveTrigger;
        float bestDoor = float.MaxValue;
        bool bestMarker = false;
        var registry = LoadScene.m_LoadScenesList;
        for (int i = 0; registry != null && i < registry.Count; i++)
        {
            var ls = registry[i];
            if (ls == null || ls == portal || !ls.gameObject.activeInHierarchy) continue;
            if (!string.Equals(ls.m_SceneToLoad, _dest.Scene, System.StringComparison.OrdinalIgnoreCase)) continue;

            var p = ls.transform.position;
            float d = FlatDistance(p, target);
            if (d >= DoorMatchMeters) continue;

            if (roots.Any(r => r.Id != targetId && FlatDistance(p, r.Position) < d)) continue;

            bool marker = string.Equals(ls.m_ExitPointName, _dest.ExitPoint, System.StringComparison.OrdinalIgnoreCase);
            if ((marker && !bestMarker) || (marker == bestMarker && d < bestDoor))
            {
                door = ls;
                bestDoor = d;
                bestMarker = marker;
            }
        }
        targetIdOut = targetId;
        return door == null ? $"no door into {_dest.Scene} found near {targetId}" : null;
    }

    private static Destination? OutdoorLeg(Destination dest, string region)
    {
        string wanted = dest.ExitPoint.Replace("Enter", "Exit");
        Destination? samePlace = null, anywhere = null;
        foreach (var d in Destinations.All)
        {
            if (d.Scene != region || !PickerCommon.IsOutdoor(d)) continue;
            if (DestinationCatalog.RejectionReason(d) != null) continue;
            if (d.Place == dest.Place && d.ExitPoint == wanted) return d;
            if (d.Place == dest.Place) samePlace ??= d;
            anywhere ??= d;
        }
        return samePlace ?? anywhere;
    }

    internal static string CurrentRegion()
    {
        var td = GameManager.m_SceneTransitionData;
        string? save = td?.m_SceneSaveFilenameCurrent;
        return string.IsNullOrEmpty(save) ? GameManager.m_ActiveScene : save!;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
