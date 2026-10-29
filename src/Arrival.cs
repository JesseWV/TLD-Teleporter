namespace Teleporter;

internal static class Arrival
{

    private const float WatchSeconds = 5f;

    private const float ExpectSeconds = 60f;

    private static string _scene = "";
    private static string _region = "";
    private static Vector3 _doorPosition;
    private static float _expectUntil;
    private static float _watchUntil;
    private static bool _arrived;
    private static bool _applied;

    internal static void Expect(Destination dest, KeyedBuilding building)
    {
        _scene = dest.Scene;
        _region = building.Region;

        var door = building.Doors[0];
        _doorPosition = new Vector3(door.X, door.Y, door.Z);
        _expectUntil = Time.realtimeSinceStartup + ExpectSeconds;
        _watchUntil = 0f;
        _arrived = false;
        _applied = false;
    }

    internal static void Forget()
    {
        _scene = "";
        _region = "";
        _arrived = false;
        _applied = false;
        _expectUntil = 0f;
        _watchUntil = 0f;
        _blocked = 0;
    }

    internal static void OnSceneInitialized(string sceneName)
    {
        if (_scene.Length == 0) return;

        if (sceneName != _scene)
        {
            Log.Msg($"[arrival] scene '{sceneName}' initialized while waiting for '{_scene}'; still waiting.");
            return;
        }
        Log.Msg($"[arrival] '{sceneName}' initialized; active scene reads '{GameManager.m_ActiveScene}'.");
        _arrived = true;
        _watchUntil = Time.realtimeSinceStartup + WatchSeconds;
        Apply();
    }

    internal static void Update()
    {
        if (_scene.Length == 0) return;
        if (!_arrived)
        {

            if (Time.realtimeSinceStartup > _expectUntil) Forget();
            return;
        }
        if (Time.realtimeSinceStartup > _watchUntil)
        {
            if (_applied) Log.Msg($"[arrival] done watching {_scene}; return target is {_region}.");
            else Log.Warning($"[arrival] gave up on {_scene} without ever applying; blocked frames {_blocked}.");
            Forget();
            return;
        }
        Apply();
    }

    private static int _blocked;

    private static void Apply()
    {
        var td = GameManager.m_SceneTransitionData;
        if (td == null) return;
        if (GameManager.m_ActiveScene != _scene)
        {
            if (++_blocked == 1)
                Log.Msg($"[arrival] waiting: active scene is '{GameManager.m_ActiveScene}', expected '{_scene}'.");
            return;
        }

        bool changed = td.m_ForceNextSceneLoadTriggerScene != _region
                       || td.m_LastOutdoorScene != _region
                       || td.m_PosBeforeInteriorLoad != _doorPosition;
        if (!changed) return;

        td.m_ForceNextSceneLoadTriggerScene = _region;
        td.m_LastOutdoorScene = _region;
        td.m_PosBeforeInteriorLoad = _doorPosition;

        if (!_applied)
        {
            _applied = true;
            Log.Msg($"[arrival] {_scene}: return target set to {_region} at {_doorPosition.x:F1}, {_doorPosition.y:F1}, {_doorPosition.z:F1}.");
        }
    }
}
