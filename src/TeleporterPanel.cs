using System.Collections.Generic;
using Il2CppInterop.Runtime;

using UnityEngine.Events;
using UnityEngine.EventSystems;
using UButton = UnityEngine.UI.Button;
using UCanvasScaler = UnityEngine.UI.CanvasScaler;
using UContentSizeFitter = UnityEngine.UI.ContentSizeFitter;
using UGraphicRaycaster = UnityEngine.UI.GraphicRaycaster;
using UImage = UnityEngine.UI.Image;
using ULayoutElement = UnityEngine.UI.LayoutElement;
using URectMask2D = UnityEngine.UI.RectMask2D;
using UScrollRect = UnityEngine.UI.ScrollRect;
using UText = UnityEngine.UI.Text;
using UVerticalLayoutGroup = UnityEngine.UI.VerticalLayoutGroup;

namespace Teleporter;

internal static class TeleporterPanel
{
    private const int SortingOrder = 30000;
    private const float RowHeight = 44f;

    private static readonly Color WindowColor = new(0.07f, 0.08f, 0.09f, 0.96f);
    private static readonly Color ListColor = new(0.11f, 0.12f, 0.13f, 1f);
    private static readonly Color ButtonColor = new(0.18f, 0.20f, 0.22f, 1f);
    private static readonly Color SelectedColor = new(0.33f, 0.40f, 0.46f, 1f);
    private static readonly Color LockedColor = new(0.13f, 0.14f, 0.15f, 1f);
    private static readonly Color TextColor = new(0.90f, 0.90f, 0.88f, 1f);
    private static readonly Color StatusColor = new(0.95f, 0.75f, 0.45f, 1f);

    private static GameObject? _root;
    private static Transform? _regionContent;
    private static Transform? _placeContent;
    private static Transform? _doorContent;
    private static UText? _placeHeader;
    private static UText? _doorHeader;
    private static UText? _status;
    private static Font? _font;
    private static string? _selectedRegion;
    private static string? _selectedPlace;
    private static bool _controlsLocked;
    private static PlayerControlMode _previousControlMode;

    internal static PlayerControlMode? ModeBeforeOurLock => _controlsLocked ? _previousControlMode : null;
    private static int _closedByEscapeFrame = -1;

    internal static bool IsOpen => _root != null && _root.activeSelf;

    internal static bool SwallowEscape => IsOpen || _closedByEscapeFrame == Time.frameCount;

    internal static void Open()
    {
        try
        {
            EnsureEventSystem();
            if (_root == null) Build();
            _root!.SetActive(true);
            LockControls();
            _selectedRegion = RegionForCurrentScene();
            _selectedPlace = null;
            _status!.text = DefaultStatus();
            Refresh();
        }
        catch (System.Exception e)
        {
            Log.Error($"[panel] failed to open: {e}");
            Close();
        }
    }

    internal static void Close(bool byEscape = false)
    {
        if (byEscape) _closedByEscapeFrame = Time.frameCount;
        if (_root != null) _root.SetActive(false);
        UnlockControls();
    }

    private static string DefaultStatus()
    {
        string? departure = Portal.DepartureRefusal();
        if (departure != null) return departure;
        return PickerModel.Entries().Exists(e => e.Locked) ? "* " + PickerModel.LockedHint : "";
    }

    private static void Refresh()
    {
        var entries = PickerModel.Entries();
        string here = GameManager.m_ActiveScene;

        var regions = PickerModel.Regions(entries);
        if (_selectedRegion == null || !regions.Exists(r => r.Scene == _selectedRegion))
            _selectedRegion = regions.Count > 0 ? regions[0].Scene : null;

        Clear(_regionContent!);
        foreach (var region in regions)
        {
            string scene = region.Scene;
            var button = MakeButton(_regionContent!, region.Display, () => { _selectedRegion = scene; _selectedPlace = null; Refresh(); },
                scene == _selectedRegion ? SelectedColor : ButtonColor);
            Height(button.gameObject, RowHeight);
        }

        var places = _selectedRegion == null ? new List<PickerModel.Place>() : PickerModel.Places(entries, _selectedRegion);
        if (_selectedPlace == null || !places.Exists(p => p.Key == _selectedPlace))
            _selectedPlace = PlaceContaining(places, here) ?? (places.Count > 0 ? places[0].Key : null);

        _placeHeader!.text = RegionDisplay(_selectedRegion) + (here == _selectedRegion ? "   - you are here" : "");
        Clear(_placeContent!);
        foreach (var place in places)
        {
            string key = place.Key;
            string label = place.Display + (place.AllLocked ? " *" : "")
                           + (PlaceContaining(new List<PickerModel.Place> { place }, here) != null ? "   - you are here" : "");
            var button = MakeButton(_placeContent!, label, () => { _selectedPlace = key; Refresh(); },
                key == _selectedPlace ? SelectedColor : ButtonColor);
            Height(button.gameObject, RowHeight);
        }

        var selected = places.Find(p => p.Key == _selectedPlace);
        _doorHeader!.text = selected?.Display ?? "";
        Clear(_doorContent!);
        if (selected == null) return;
        foreach (var entry in selected.Doors)
        {
            var dest = entry.Dest;

            var button = entry.Locked
                ? MakeButton(_doorContent!, dest.Door + " *", () => { _status!.text = PickerModel.LockedHint + "."; }, LockedColor)
                : MakeButton(_doorContent!, dest.Door, () => Choose(dest), ButtonColor);
            Height(button.gameObject, RowHeight);
        }
    }

    private static string? PlaceContaining(List<PickerModel.Place> places, string here)
    {
        foreach (var place in places)
            foreach (var e in place.Doors)
                if (e.Dest.Scene == here && !IsOutdoor(e.Dest)) return place.Key;
        return null;
    }

    private static void Choose(Destination dest)
    {
        string? departure = Portal.DepartureRefusal();
        if (departure != null) { _status!.text = departure; return; }
        if (IsOutdoor(dest) ? dest.Scene == GameManager.m_ActiveScene : PickerCommon.AlreadyInside(dest))
        {

            _status!.text = IsOutdoor(dest)
                ? $"You are already in {RegionDisplay(dest.Scene)}. Travelling within the same region is not supported yet."
                : $"You are already in {dest.PlaceDisplay}.";
            return;
        }

        Close();
        var confirm = InterfaceManager.GetPanel<Panel_Confirmation>();
        if (confirm == null)
        {
            Log.Warning("[panel] Panel_Confirmation not found; not travelling.");
            return;
        }
        string question = IsOutdoor(dest) ? $"Travel to {RegionDisplay(dest.Scene)}, {dest.Display}?" : $"Travel to {dest.Display}?";
        confirm.ShowConfirmPanel(question, PickerCommon.ConfirmKey, PickerCommon.CancelKey, (System.Action)(() => Travel(dest)), null);
    }

    private static void Travel(Destination dest)
    {

        if (!GameManager.HasPlayerObject() || GameManager.IsMainMenuActive() || string.IsNullOrEmpty(GameManager.m_ActiveScene)) return;
        if (Portal.DepartureRefusal() != null) return;
        string? refusal = DestinationCatalog.RejectionReason(dest);
        if (refusal != null)
        {
            Log.Warning($"[panel] refused {dest.Scene}/{dest.ExitPoint}: {refusal}");
            return;
        }

        if (Portal.Spawn(dest, out string message)) Log.Msg("[panel] " + message);
        else Log.Warning("[panel] " + message);
    }

    private static string RegionForCurrentScene()
    {
        string here = GameManager.m_ActiveScene;
        foreach (var (scene, _) in Destinations.RegionList)
            if (scene == here) return scene;
        foreach (var d in Destinations.All)
            if (d.Scene == here && d.Regions.Length > 0) return d.Regions[0];
        return _selectedRegion ?? Destinations.RegionList[0].Scene;
    }

    private static string RegionDisplay(string? scene)
    {
        foreach (var (s, display) in Destinations.RegionList)
            if (s == scene) return display;
        return scene ?? "";
    }

    private static bool IsOutdoor(Destination d) => d.Regions.Length == 1 && d.Regions[0] == d.Scene;

    private static void LockControls()
    {
        if (_controlsLocked) return;
        var pm = GameManager.GetPlayerManagerComponent();
        if (pm == null) return;
        _previousControlMode = pm.GetControlMode();
        pm.SetControlMode(PlayerControlMode.Locked);
        _controlsLocked = true;
    }

    private static void UnlockControls()
    {
        if (!_controlsLocked) return;
        _controlsLocked = false;
        var pm = GameManager.GetPlayerManagerComponent();

        if (pm != null && pm.GetControlMode() == PlayerControlMode.Locked)
            pm.SetControlMode(_previousControlMode);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var camera = GameManager.GetMainCamera();
        if (camera == null) return;
        var go = camera.gameObject;
        if (go.GetComponent<EventSystem>() == null) go.AddComponent<EventSystem>();
        if (go.GetComponent<StandaloneInputModule>() == null) go.AddComponent<StandaloneInputModule>();
    }

    private static void Build()
    {

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _root = new GameObject("TLDTeleporter_Picker");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = _root.AddComponent<UCanvasScaler>();
        scaler.uiScaleMode = UCanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<UGraphicRaycaster>();

        var dim = Child(_root.transform, "Dim");
        Place(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.AddComponent<UImage>().color = new Color(0f, 0f, 0f, 0.55f);

        var window = Child(_root.transform, "Window");
        var windowRect = window.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.sizeDelta = new Vector2(1400f, 800f);
        window.AddComponent<UImage>().color = WindowColor;

        var title = Label(window.transform, "Teleporter", 34, TextAnchor.MiddleLeft);
        Place(title.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -70f), new Vector2(-100f, -14f));

        var close = MakeButton(window.transform, "X", () => Close(), ButtonColor, TextAnchor.MiddleCenter);
        Place(close.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-78f, -70f), new Vector2(-18f, -14f));

        const float top = -120f, headerTop = -78f, headerBottom = -112f, bottom = 70f;
        float[] left = { 28f, 348f, 848f };
        float[] right = { 332f, 832f, 1372f };

        var regionHeader = Label(window.transform, "Region", 22, TextAnchor.MiddleLeft);
        Column(regionHeader.gameObject, left[0], right[0], headerBottom, headerTop);
        _placeHeader = Label(window.transform, "", 22, TextAnchor.MiddleLeft);
        Column(_placeHeader.gameObject, left[1], right[1], headerBottom, headerTop);
        _doorHeader = Label(window.transform, "", 22, TextAnchor.MiddleLeft);
        Column(_doorHeader.gameObject, left[2], right[2], headerBottom, headerTop);

        _regionContent = ScrollList(window.transform, "Regions", left[0], right[0], bottom, top);
        _placeContent = ScrollList(window.transform, "Places", left[1], right[1], bottom, top);
        _doorContent = ScrollList(window.transform, "Doors", left[2], right[2], bottom, top);

        _status = Label(window.transform, "", 20, TextAnchor.MiddleLeft);
        _status.color = StatusColor;
        Place(_status.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 12f), new Vector2(-28f, 62f));

        _root.SetActive(false);
    }

    private static void Column(GameObject go, float left, float right, float bottomFromTop, float topFromTop)
    {
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(left, bottomFromTop);
        rect.offsetMax = new Vector2(right, topFromTop);
    }

    private static GameObject Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void Height(GameObject go, float height)
    {
        var element = go.AddComponent<ULayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
    }

    private static UText Label(Transform parent, string text, int size, TextAnchor alignment)
    {
        var go = Child(parent, "Label");
        var label = go.AddComponent<UText>();
        label.font = _font;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = TextColor;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    private static UButton MakeButton(Transform parent, string text, System.Action onClick, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var go = Child(parent, "Button");
        var image = go.AddComponent<UImage>();
        image.color = color;
        var button = go.AddComponent<UButton>();
        button.targetGraphic = image;

        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(onClick));
        var label = Label(go.transform, text, 22, alignment);
        Place(label.gameObject, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-10f, 0f));
        return button;
    }

    private static Transform ScrollList(Transform parent, string name, float left, float right, float bottomFromBottom, float topFromTop)
    {
        var scroll = Child(parent, name);
        var scrollRect = scroll.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(0f, 1f);
        scrollRect.offsetMin = new Vector2(left, bottomFromBottom);
        scrollRect.offsetMax = new Vector2(right, topFromTop);
        scroll.AddComponent<UImage>().color = ListColor;

        var viewport = Child(scroll.transform, "Viewport");
        Place(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.AddComponent<URectMask2D>();

        var content = Child(viewport.transform, "Content");
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        var layout = content.AddComponent<UVerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.AddComponent<UContentSizeFitter>().verticalFit = UContentSizeFitter.FitMode.PreferredSize;

        var scroller = scroll.AddComponent<UScrollRect>();
        scroller.viewport = viewport.GetComponent<RectTransform>();
        scroller.content = contentRect;
        scroller.horizontal = false;
        scroller.movementType = UScrollRect.MovementType.Clamped;
        scroller.scrollSensitivity = 40f;
        return content.transform;
    }

    private static void Clear(Transform content)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }
}
