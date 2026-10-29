using System.Collections.Generic;

namespace Teleporter;

internal static class DestinationCatalog
{

    internal static string? RejectionReason(Destination d) => StaticReason(d) ?? SaveReason(d);

    internal static string? SaveReason(Destination d)
    {

        if (SeamlessInteriorsCompat.Handles(d))
        {
            if (d.Building < 0 || d.Building >= Destinations.Buildings.Length) return null;
            string? absent = SaveDoorLookup.Resolve(Destinations.Buildings[d.Building], out _);
            return absent == SaveDoorLookup.AbsentReason ? absent : null;
        }
        if (d.Building < 0 || d.Building >= Destinations.Buildings.Length) return null;
        return SaveDoorLookup.Resolve(Destinations.Buildings[d.Building], out _);
    }

    internal static bool LockedUntilVisited(Destination d)
    {
        if (d.Building < 0 || d.Building >= Destinations.Buildings.Length) return false;
        if (SeamlessInteriorsCompat.Handles(d)) return false;
        if (StaticReason(d) != null) return false;
        return !SaveDoorLookup.RegionVisited(Destinations.Buildings[d.Building].Region);
    }

    private static string? StaticReason(Destination d)
    {

        string? seamless = SeamlessInteriorsCompat.Reason(d);
        if (seamless != null) return seamless;

        if (d.Building < 0 && d.CanBeInstanced && !SeamlessInteriorsCompat.Handles(d))
            return "the game keys this interior to the door walked through, and this destination names no building, so travel would land in a newly created copy rather than the real one";

        if (!d.AlwaysPresent)
            return "not present in every world, so it needs a live check this mod does not have yet";

        if (d.Variant == "SANDBOX" && SaveGameSystem.m_CurrentGameMode != SaveSlotType.SANDBOX)
            return "survival-only destination, and this is not a survival save";

        if (d.Variant.Length > 0 && d.Variant != "SANDBOX" && !IsContentUsable(d.Variant))
            return $"needs optional content '{d.Variant}', which is not installed or not owned";

        return null;
    }

    private static bool IsContentUsable(string configName)
    {
        var mgr = Il2CppTLD.OptionalContent.OptionalContentManager.Instance;
        if (mgr == null || !mgr.IsReadyToBeUsed) return false;
        if (!mgr.TryFindConfigFromName(configName, out var cfg) || cfg == null) return false;

        return mgr.IsContentOwned(cfg) && mgr.CanUseContent(cfg);
    }

    internal static List<Destination> Available()
    {
        var ok = new List<Destination>();
        foreach (var d in Destinations.All)
            if (RejectionReason(d) == null) ok.Add(d);
        return ok;
    }

    internal static bool TryFind(string scene, string? exitPoint, out Destination found, out string? reason)
    {
        Destination? firstMatch = null;
        string? firstReason = null;
        foreach (var d in Destinations.All)
        {
            if (!string.Equals(d.Scene, scene, System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (exitPoint != null && !string.Equals(d.ExitPoint, exitPoint, System.StringComparison.OrdinalIgnoreCase))
                continue;

            string? why = RejectionReason(d);
            if (why == null)
            {
                found = d;
                reason = null;
                return true;
            }
            if (firstMatch == null)
            {
                firstMatch = d;
                firstReason = why;
            }
        }

        found = firstMatch ?? default;
        reason = firstMatch != null
            ? firstReason
            : exitPoint == null ? "no destination by that scene name" : "no door of that scene arrives at that marker";
        return false;
    }

    internal static List<Destination> Search(string fragment)
    {
        var hits = new List<Destination>();
        foreach (var d in Destinations.All)
        {
            if (d.Scene.Contains(fragment, System.StringComparison.OrdinalIgnoreCase)
                || d.Display.Contains(fragment, System.StringComparison.OrdinalIgnoreCase)
                || d.ReachedFrom.Contains(fragment, System.StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(d);
            }
        }
        return hits;
    }
}
