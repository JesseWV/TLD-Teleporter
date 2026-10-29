using System.Collections.Generic;

namespace Teleporter;

internal static class PickerModel
{

    internal readonly struct Entry
    {
        internal Entry(Destination dest, bool locked) { Dest = dest; Locked = locked; }
        internal readonly Destination Dest;
        internal readonly bool Locked;
    }

    internal const string LockedHint = "Visit this region once to unlock";

    internal sealed class Place
    {
        internal string Key = "";
        internal string Display = "";
        internal readonly List<Entry> Doors = new();
        internal bool AllLocked
        {
            get
            {
                foreach (var e in Doors) if (!e.Locked) return false;
                return Doors.Count > 0;
            }
        }
    }

    internal const string PathsKey = "~Paths";

    private static bool _timed;

    internal static List<Entry> Entries()
    {
        float started = Time.realtimeSinceStartup;
        var entries = new List<Entry>();
        foreach (var d in Destinations.All)
        {
            if (DestinationCatalog.RejectionReason(d) == null) entries.Add(new Entry(d, false));
            else if (DestinationCatalog.LockedUntilVisited(d)) entries.Add(new Entry(d, true));
        }
        if (!_timed)
        {
            _timed = true;
            Log.Msg($"[picker] first listing took {(Time.realtimeSinceStartup - started) * 1000f:F0} ms for {Destinations.All.Length} doors.");
        }
        return entries;
    }

    internal static List<(string Scene, string Display)> Regions(List<Entry> entries)
    {
        var result = new List<(string Scene, string Display)>();
        foreach (var region in Destinations.RegionList)
        {
            string scene = region.Scene;
            if (entries.Exists(e => System.Array.IndexOf(e.Dest.Regions, scene) >= 0)) result.Add(region);
        }
        return result;
    }

    internal static List<Place> Places(List<Entry> entries, string region)
    {
        var byKey = new Dictionary<string, Place>();
        var places = new List<Place>();
        foreach (var e in entries)
        {
            if (System.Array.IndexOf(e.Dest.Regions, region) < 0) continue;
            if (!byKey.TryGetValue(e.Dest.Place, out var place))
            {
                place = new Place { Key = e.Dest.Place, Display = e.Dest.PlaceDisplay };
                byKey[e.Dest.Place] = place;
                places.Add(place);
            }
            place.Doors.Add(e);
        }

        places.Sort((a, b) =>
        {
            bool pa = a.Key == PathsKey, pb = b.Key == PathsKey;
            return pa != pb ? pb.CompareTo(pa) : string.Compare(a.Display, b.Display, System.StringComparison.OrdinalIgnoreCase);
        });
        foreach (var place in places)
        {
            place.Doors.Sort((a, b) =>
            {
                int ra = DoorRank(a.Dest), rb = DoorRank(b.Dest);
                return ra != rb ? ra.CompareTo(rb) : string.Compare(a.Dest.Door, b.Dest.Door, System.StringComparison.OrdinalIgnoreCase);
            });
        }
        return places;
    }

    private static int DoorRank(Destination d) =>
        d.Door.StartsWith("Inside", System.StringComparison.Ordinal) ? 0 :
        d.Door.StartsWith("Outside", System.StringComparison.Ordinal) ? 1 : 2;
}
