using System.Collections.Generic;

namespace Teleporter;

internal static class Commands
{

    private static Destination? _armed;

    internal static void Register()
    {
        Add("teleporter_here", "list the transition triggers in this scene", OnHere);
        Add("teleporter_find", "teleporter_find <text> - search destinations", OnFind);
        Add("teleporter_confirm", "open the portal armed by goto", OnConfirm);
        Add("teleporter_cancel", "close an unused portal", OnCancel);
        Add("teleporter_status", "show what this mod has open", OnStatus);
        Add("teleporter_goto", "teleporter_goto <scene> [exitPoint] - arm travel to a destination", OnGoto);
        Add("teleporter_keys", "teleporter_keys [text] - what this save says about keyed buildings", OnKeys);

        Add("goto", "short alias for teleporter_goto", OnGoto);
    }

    private static readonly HashSet<string> Ours = new();

    private static void Add(string name, string help, System.Action fn)
    {

        if (uConsole.CommandAlreadyRegistered(name))
        {

            if (!Ours.Contains(name))
                Log.Warning($"Command '{name}' is registered by something else; skipping it.");
            return;
        }

        uConsole.RegisterCommand(name, help, new System.Action(fn));
        Ours.Add(name);
        Log.Msg($"Registered '{name}'.");
    }

    private static void OnKeys()
    {
        string filter = uConsole.GetNumParameters() > 0 ? uConsole.GetString() : "";
        int resolved = 0, locked = 0, absent = 0, shown = 0;

        for (int i = 0; i < Destinations.Buildings.Length; i++)
        {
            var b = Destinations.Buildings[i];
            if (filter.Length > 0 && !b.Scene.Contains(filter, System.StringComparison.OrdinalIgnoreCase)
                && !b.Region.Contains(filter, System.StringComparison.OrdinalIgnoreCase)) continue;

            string? why = SaveDoorLookup.Resolve(b, out string guid);
            if (why == null) resolved++;
            else if (!SaveDoorLookup.RegionVisited(b.Region)) locked++;
            else absent++;

            if (filter.Length > 0 && shown < 40)
            {
                shown++;
                uConsole.Log($"#{i} {b.Scene} in {b.Region} ({b.Doors.Length} doors): {(why == null ? guid : why)}");
            }
        }

        uConsole.Log($"keyed buildings: {resolved} resolved, {locked} in unvisited regions, {absent} not in this world."
                     + (filter.Length == 0 ? " Pass a scene or region to list them." : ""));
        uConsole.Log($"SerializeAll patch has fired: {SerializationPatch.HasFired}.");
        ReportLiveDoors();
    }

    private static void ReportLiveDoors()
    {
        string here = GameManager.m_ActiveScene;
        var registry = LoadScene.m_LoadScenesList;
        if (registry == null || string.IsNullOrEmpty(here)) return;

        var live = new Dictionary<string, string>();
        for (int i = 0; i < registry.Count; i++)
        {
            var ls = registry[i];
            if (ls == null || ls.gameObject == null) continue;
            var id = ls.gameObject.GetComponent<ObjectGuid>();
            string pdid = id == null ? "" : id.PDID;
            if (!string.IsNullOrEmpty(pdid)) live[pdid] = ls.GetGUID();
        }
        if (live.Count == 0) return;

        int agree = 0, differ = 0, unknown = 0;
        foreach (var b in Destinations.Buildings)
        {
            if (b.Region != here) continue;
            if (SaveDoorLookup.Resolve(b, out string savedGuid) != null) { unknown++; continue; }

            bool matched = false, anyLoaded = false;
            foreach (var door in b.Doors)
            {
                if (!live.TryGetValue(door.Id, out string? liveGuid)) continue;
                anyLoaded = true;
                if (liveGuid == savedGuid) { matched = true; break; }
            }
            if (matched) agree++;
            else if (anyLoaded) differ++;
            else unknown++;
        }
        uConsole.Log($"live doors in {here}: {live.Count} carry an id; of this region's keyed buildings {agree} agree with the save, {differ} DIFFER, {unknown} could not be compared.");
    }

    private static void OnHere()
    {
        var registry = LoadScene.m_LoadScenesList;
        int n = registry == null ? 0 : registry.Count;
        uConsole.Log($"Scene '{GameManager.m_ActiveScene}': {n} entries in the trigger registry.");
        for (int i = 0; i < n; i++)
        {
            var ls = registry![i];
            if (ls == null) { uConsole.Log($"[{i}] <null entry>"); continue; }
            var pos = ls.transform.position;
            string kind = ls.m_TransitionOnContact ? "walk-in" : "click";
            uConsole.Log($"[{i}] -> {ls.m_SceneToLoad} ({kind}) exit={ls.m_ExitPointName} at {pos.x:F1},{pos.z:F1}");
        }
        uConsole.Log("This mod does not modify any of these. It spawns its own contact volume instead.");
    }

    private static void OnFind()
    {
        if (uConsole.GetNumParameters() < 1)
        {
            uConsole.Log($"{DestinationCatalog.Available().Count} destinations available. teleporter_find <text> to narrow.");
            return;
        }

        string q = uConsole.GetString();
        var hits = DestinationCatalog.Search(q);
        if (hits.Count == 0) { uConsole.Log($"No destination matches '{q}'."); return; }

        int shown = 0;
        foreach (var d in hits)
        {

            string? why = DestinationCatalog.RejectionReason(d);
            if (why != null) { uConsole.Log($"  {d.Scene} {d.ExitPoint} - UNAVAILABLE: {why}"); continue; }
            string warn = d.ReverseExists ? "" : "  (no return leg recorded)";
            uConsole.Log($"  {d.Scene} {d.ExitPoint} - {d.Display}{warn}");
            shown++;
        }
        uConsole.Log($"{shown} usable of {hits.Count} matching '{q}'.");
    }

    private static void OnGoto()
    {
        if (uConsole.GetNumParameters() < 1)
        {
            uConsole.Log("Usage: goto <scene> [exitPoint]   (teleporter_find <text> to search)");
            return;
        }

        string scene = uConsole.GetString();
        string? exitPoint = uConsole.NextParameterExists() ? uConsole.GetString() : null;
        if (!DestinationCatalog.TryFind(scene, exitPoint, out var dest, out var reason))
        {
            uConsole.Log($"Cannot travel to '{scene}{(exitPoint == null ? "" : " " + exitPoint)}': {reason}");
            return;
        }

        _armed = dest;
        uConsole.Log($"ARMED: {dest.Display} [{dest.Scene}], arriving at {dest.ExitPoint}.");

        string? departure = Portal.DepartureRefusal();
        if (departure != null)
            uConsole.Log($"  WARNING: {departure} teleporter_confirm will refuse until then.");
        if (dest.ArrivalCount > 1)
            uConsole.Log($"  Note: {dest.ArrivalCount} transitions arrive there, so the arrival marker may be ambiguous.");
        if (!dest.ReverseExists)
            uConsole.Log("  Note: the transition map records no return leg from there.");

        uConsole.Log("  Run teleporter_confirm to open the portal. It fires the moment it touches you.");
    }

    private static void OnConfirm()
    {
        if (_armed == null) { uConsole.Log("Nothing armed. Run goto <scene> first."); return; }

        var dest = _armed.Value;
        _armed = null;

        if (Portal.Spawn(dest, out string message)) uConsole.Log(message);
        else uConsole.Log("Could not open a portal: " + message);
    }

    private static void OnCancel()
    {

        bool wasArmed = _armed != null;
        bool wasOpen = Portal.IsLive;
        bool wasEntering = SeamlessEntry.IsActive;
        _armed = null;
        int removed = Portal.Despawn();
        SeamlessEntry.Forget();

        if (wasOpen) uConsole.Log($"Portal closed; {removed} registry entr(y/ies) removed.");
        if (wasArmed) uConsole.Log("Disarmed the pending destination.");
        if (wasEntering) uConsole.Log("Cancelled the pending entry into a Seamless Interiors building.");
        if (!wasOpen && !wasArmed && !wasEntering) uConsole.Log("Nothing armed and no portal open.");
    }

    private static void OnStatus()
    {
        uConsole.Log(BuildInfo.Stamp);
        uConsole.Log($"Scene: {GameManager.m_ActiveScene}. Portal open: {Portal.IsLive}.");
        uConsole.Log(_armed == null ? "Nothing armed." : $"Armed: -> {_armed.Value.Scene}");
    }
}
