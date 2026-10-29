namespace Teleporter
{

    internal readonly partial record struct Destination
    {

        internal string PlaceDisplay => Teleporter.PlaceNames.Place(this);

        internal string Display => Teleporter.PlaceNames.Label(this);
    }

    internal static partial class Destinations
    {

        internal static (string Scene, string Display)[] RegionList => Teleporter.PlaceNames.Regions();
    }
}

namespace Teleporter
{

    internal static class PlaceNames
    {
        private static readonly Dictionary<string, string> Cache = new(System.StringComparer.Ordinal);

        internal static string Place(Destination d)
        {
            if (d.PlaceKey.Length == 0) return d.PlaceEnglish;
            if (Cache.TryGetValue(d.PlaceKey, out var cached)) return cached;
            try
            {
                string text = Localization.Get(d.PlaceKey);
                if (string.IsNullOrEmpty(text) || text == d.PlaceKey) return d.PlaceEnglish;
                Cache[d.PlaceKey] = text;
                return text;
            }
            catch
            {
                return d.PlaceEnglish;
            }
        }

        private static (string Scene, string Display)[]? _regions;

        internal static (string Scene, string Display)[] Regions()
        {
            if (_regions != null) return _regions;
            var table = Destinations.RegionTable;
            var result = new (string Scene, string Display)[table.Length];
            bool complete = true;
            for (int i = 0; i < table.Length; i++)
            {
                string text = Resolve(table[i].Key);
                if (text.Length == 0) { complete = false; text = table[i].English; }
                result[i] = (table[i].Scene, text);
            }

            if (complete) _regions = result;
            return result;
        }

        private static string Resolve(string key)
        {
            try
            {
                string text = Localization.Get(key);
                return string.IsNullOrEmpty(text) || text == key ? "" : text;
            }
            catch
            {
                return "";
            }
        }

        internal static string Label(Destination d)
        {
            string place = Place(d);
            if (place == d.PlaceEnglish || !d.DisplayEnglish.StartsWith(d.PlaceEnglish, System.StringComparison.Ordinal))
                return d.DisplayEnglish;
            return place + d.DisplayEnglish.Substring(d.PlaceEnglish.Length);
        }

        internal static void Forget()
        {
            Cache.Clear();
            _regions = null;
        }
    }
}
