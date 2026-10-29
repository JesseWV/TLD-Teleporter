using System.Reflection;

namespace Teleporter;

internal static class SeamlessInteriorsCompat
{
    private const string AssemblyName = "SeamlessInteriors";
    private const string ApiTypeName = "SeamlessInteriors.SeamlessInteriorsApi, SeamlessInteriors";

    private static bool _resolved;
    private static bool _failClosed;

    private static readonly Dictionary<string, List<string>> IdsByKey = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> SceneById = new(System.StringComparer.Ordinal);

    private static MethodInfo? _getPlayerInteriorId;
    private static MethodInfo? _getInteriorRoot;
    private static PropertyInfo? _areInteriorsReady;

    internal static bool Present { get; private set; }

    internal static bool Usable { get { Resolve(); return Present && !_failClosed; } }

    internal static string? Reason(Destination d)
    {
        Resolve();
        if (Present && _failClosed && IsInterior(d))
            return "Seamless Interiors is installed and its list of buildings could not be read, so no interior is offered";
        return null;
    }

    internal static bool Handles(Destination d) => HandledRegion(d) != null;

    internal static string? HandledRegion(Destination d)
    {
        if (VanillaMode) return null;
        return SeamlessRegion(d);
    }

    internal static bool VanillaMode;

    internal static bool IsSeamlessBuilding(Destination d) => SeamlessRegion(d) != null;

    private static string? SeamlessRegion(Destination d)
    {
        Resolve();
        if (!Present || _failClosed || !IsInterior(d)) return null;
        foreach (var region in d.Regions)
            if (IdsByKey.ContainsKey(Key(d.Scene, region))) return region;
        return null;
    }

    internal static IReadOnlyList<string> IdsFor(string scene, string region)
        => IdsByKey.TryGetValue(Key(scene, region), out var ids) ? ids : System.Array.Empty<string>();

    internal static string? SceneOf(string? id) => id != null && SceneById.TryGetValue(id, out var s) ? s : null;

    internal static string? PlayerInteriorId()
    {
        Resolve();
        if (_getPlayerInteriorId == null) return null;
        try { return _getPlayerInteriorId.Invoke(null, null) as string; }
        catch { return null; }
    }

    internal static bool InteriorsReady()
    {
        Resolve();
        if (_areInteriorsReady == null) return false;
        try { return (bool)_areInteriorsReady.GetValue(null)!; }
        catch { return false; }
    }

    internal static GameObject? InteriorRoot(string id)
    {
        Resolve();
        if (_getInteriorRoot == null) return null;
        try { return _getInteriorRoot.Invoke(null, new object[] { id }) as GameObject; }
        catch { return null; }
    }

    internal static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        Present = System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == AssemblyName);
        if (!Present)
        {
            Log.Msg("[seamless] Seamless Interiors not loaded; interiors are reached by portal as usual.");
            return;
        }

        try
        {
            var api = System.Type.GetType(ApiTypeName, throwOnError: true)!;
            int version = (int)api.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
            var ids = (string[])Method(api, "GetInteriorIds").Invoke(null, null)!;
            var getScene = Method(api, "GetInteriorSceneName", typeof(string));
            var getRegion = Method(api, "GetInteriorRegionSceneName", typeof(string));

            var handled = new List<string>();
            foreach (var id in ids)
            {
                var scene = (string?)getScene.Invoke(null, new object[] { id });
                var region = (string?)getRegion.Invoke(null, new object[] { id });
                if (string.IsNullOrEmpty(scene) || string.IsNullOrEmpty(region))
                    throw new System.InvalidOperationException($"building '{id}' has no scene or region");
                SceneById[id] = scene;
                string key = Key(scene, region);
                if (!IdsByKey.TryGetValue(key, out var list)) IdsByKey[key] = list = new List<string>();
                list.Add(id);
                handled.Add($"{id} ({scene} in {region})");
            }

            _getPlayerInteriorId = Method(api, "GetPlayerInteriorId");
            _getInteriorRoot = Method(api, "GetInteriorRoot", typeof(string));
            _areInteriorsReady = api.GetProperty("AreInteriorsReady", BindingFlags.Public | BindingFlags.Static)
                                 ?? throw new System.MissingMemberException(api.FullName, "AreInteriorsReady");

            Log.Msg($"[seamless] Seamless Interiors API v{version} loaded: {ids.Length} buildings are entered through their own doors rather than by portal: {string.Join(", ", handled)}.");
        }
        catch (System.Exception e)
        {
            _failClosed = true;
            IdsByKey.Clear();
            SceneById.Clear();
            _getPlayerInteriorId = null;
            _getInteriorRoot = null;
            _areInteriorsReady = null;
            Log.Warning($"[seamless] Seamless Interiors is loaded but its API could not be read; withholding every interior: {e.GetBaseException().Message}");
        }
    }

    private static bool IsInterior(Destination d) => System.Array.IndexOf(d.Regions, d.Scene) < 0;

    private static string Key(string scene, string region) => scene + "|" + region;

    private static MethodInfo Method(System.Type api, string name, params System.Type[] args)
        => api.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, args, null)
           ?? throw new System.MissingMethodException(api.FullName, name);
}
