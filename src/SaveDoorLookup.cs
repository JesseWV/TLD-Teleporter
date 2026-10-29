using System.Collections.Generic;

namespace Teleporter;

internal static class SaveDoorLookup
{

    private sealed class RegionRecords
    {

        internal readonly Dictionary<string, string> GuidByDoorId = new();

        internal readonly HashSet<string> Layouts = new();

        internal bool Visited;
    }

    private static readonly Dictionary<string, RegionRecords> _cache = new();
    private static string _cachedSlot = "";

    private static readonly HashSet<string> _loggedUnvisited = new();

    private static bool _disabled;

    internal static void Forget()
    {
        _cache.Clear();
        _cachedSlot = "";
        _loggedUnvisited.Clear();
    }

    internal static bool RegionVisited(string region)
    {
        if (region == GameManager.m_ActiveScene) return true;
        var records = Read(region);
        return records != null && records.Visited;
    }

    internal const string AbsentReason = "this world does not contain this building";

    internal static string? Resolve(KeyedBuilding building, out string guid)
    {
        guid = "";
        if (_disabled) return "reading the save is disabled after an earlier failure";

        if (building.Region == GameManager.m_ActiveScene)
        {
            string? liveAnswer = ResolveLive(building, out guid, out bool registered);
            if (registered) return liveAnswer;
        }

        var records = Read(building.Region);
        if (records == null) return "the save could not be read";
        if (!records.Visited)
            return $"this save has never loaded {PickerCommon.RegionDisplay(building.Region)}, so the game has not yet given this building's door an identity";

        string found = "";
        foreach (var door in building.Doors)
        {
            if (!IsLive(door, records.Layouts)) continue;
            if (!records.GuidByDoorId.TryGetValue(door.Id, out string? doorGuid) || string.IsNullOrEmpty(doorGuid)) continue;
            if (found.Length == 0) found = doorGuid;
            else if (found != doorGuid)
            {
                Log.Warning($"[keys] {building.Scene} in {building.Region} resolves to two GUIDs, {found} and {doorGuid}; refusing.");
                return "this building's doors disagree about which interior they open, so travel is refused";
            }
        }

        if (found.Length == 0)
            return AbsentReason;

        guid = found;
        return null;
    }

    private static int _liveFrame = -1;
    private static readonly Dictionary<string, string> _liveGuidByDoorId = new();

    private static string? ResolveLive(KeyedBuilding building, out string guid, out bool registered)
    {
        guid = "";
        registered = false;
        if (_liveFrame != UnityEngine.Time.frameCount)
        {
            _liveFrame = UnityEngine.Time.frameCount;
            _liveGuidByDoorId.Clear();
            var registry = LoadScene.m_LoadScenesList;
            if (registry != null)
            {
                for (int i = 0; i < registry.Count; i++)
                {
                    var ls = registry[i];
                    if (ls == null || ls.gameObject == null) continue;
                    var id = ls.gameObject.GetComponent<ObjectGuid>();
                    if (id == null || string.IsNullOrEmpty(id.PDID)) continue;

                    string g = ls.gameObject.activeInHierarchy ? (ls.GetGUID() ?? "") : "";
                    if (g.Length > 0 || !_liveGuidByDoorId.ContainsKey(id.PDID)) _liveGuidByDoorId[id.PDID] = g;
                }
            }
        }

        string found = "";
        foreach (var door in building.Doors)
        {
            if (!_liveGuidByDoorId.TryGetValue(door.Id, out string? liveGuid)) continue;
            registered = true;
            if (string.IsNullOrEmpty(liveGuid)) continue;
            if (found.Length == 0) found = liveGuid;
            else if (found != liveGuid)
            {
                Log.Warning($"[keys] {building.Scene} in {building.Region} has two live GUIDs, {found} and {liveGuid}; refusing.");
                return "this building's doors disagree about which interior they open, so travel is refused";
            }
        }
        if (!registered) return null;
        if (found.Length == 0) return AbsentReason;
        guid = found;
        return null;
    }

    private static bool IsLive(KeyedDoor door, HashSet<string> chosen)
    {
        foreach (var layout in door.Layouts)
        {
            string live = "";
            foreach (string member in layout.Members)
            {
                if (chosen.Contains(member)) { live = member; break; }
            }
            if (live.Length == 0) live = layout.OldSave;

            if (live.Length == 0 || live != layout.Member) return false;
        }
        return true;
    }

    private static string? AutosaveSlotFor(string slot)
    {
        var auto = SaveGameSlots.m_AutoSaveSlot;
        if (auto == null) return null;
        var slots = SaveGameSlots.m_SaveSlots;
        if (slots == null) return null;
        for (int i = 0; i < slots.Count; i++)
        {
            var named = slots[i];
            if (named != null && named.m_InternalName == slot)
                return named.m_GameId == auto.m_GameId ? auto.m_InternalName : null;
        }
        return null;
    }

    private static RegionRecords? Read(string region)
    {
        string slot = SaveGameSystem.GetCurrentSaveName();
        if (string.IsNullOrEmpty(slot)) return null;
        if (slot != _cachedSlot)
        {
            _cache.Clear();
            _loggedUnvisited.Clear();
            _cachedSlot = slot;
        }
        if (_cache.TryGetValue(region, out var cached)) return cached;

        var records = new RegionRecords();
        try
        {

            SceneSaveGameFormat? scene = null;
            string? auto = AutosaveSlotFor(slot);
            if (auto == null || !SaveGameSlots.TryLoadDataFromSlot<SceneSaveGameFormat>(auto, region, out scene) || scene == null)
                SaveGameSlots.TryLoadDataFromSlot<SceneSaveGameFormat>(slot, region, out scene);
            if (scene != null)
            {
                records.Visited = true;
                AddDoors(records, scene.m_LoadSceneTriggersSerialized);

                AddDoors(records, scene.m_LoadSceneParentSerialized);
                AddLayouts(records, scene.m_RandomSpawnObjectManagerSerialized);
            }
        }
        catch (System.Exception e)
        {
            _disabled = true;
            Log.Error($"[keys] reading {region} from slot {slot} failed; keyed interiors are off for this session: {e}");
            return null;
        }

        if (records.Visited)
        {
            _cache[region] = records;
            Log.Msg($"[keys] {region}: visited=True doors={records.GuidByDoorId.Count} layouts={records.Layouts.Count}");
        }
        else if (_loggedUnvisited.Add(region))
            Log.Msg($"[keys] {region}: visited=False");
        return records;
    }

    private static void AddDoors(RegionRecords records, string? serialized)
    {
        if (string.IsNullOrEmpty(serialized)) return;
        var list = Utils.DeserializeObject<StringArray>(serialized);
        if (list == null || list.strings == null) return;
        foreach (string text in list.strings)
        {
            if (string.IsNullOrEmpty(text)) continue;
            var proxy = Utils.DeserializeObject<LoadSceneSaveDataProxy>(text);
            if (proxy == null || string.IsNullOrEmpty(proxy.m_GameGuid) || string.IsNullOrEmpty(proxy.m_GUID)) continue;
            records.GuidByDoorId[proxy.m_GameGuid] = proxy.m_GUID;
        }
    }

    private static void AddLayouts(RegionRecords records, string? serialized)
    {
        if (string.IsNullOrEmpty(serialized)) return;
        var list = Utils.DeserializeObject<RandomSpawnObjectSaveList>(serialized);
        if (list == null || list.m_SaveDataList == null) return;
        for (int i = 0; i < list.m_SaveDataList.Count; i++)
        {
            string name = list.m_SaveDataList[i]?.m_ObjectName ?? "";
            if (name.Length > 0) records.Layouts.Add(name);
        }
    }
}
