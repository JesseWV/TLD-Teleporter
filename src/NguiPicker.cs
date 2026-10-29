using System.Collections.Generic;

namespace Teleporter;

internal static class NguiPicker
{

    private const int PanelDepth = 60;
    private const int VisibleRows = 14;
    private const float RowPitch = 33f;
    private const float FirstRowY = 205f;
    private const float HeaderY = 240f;
    private const float Gap = 16f;
    private const float MaxWidth = 1180f;

    private static readonly float[] Shares = { 0.25f, 0.35f, 0.40f };

    private static readonly Color White = new(0.98f, 0.98f, 0.98f, 1f);
    private static readonly Color Grey = new(0.784f, 0.784f, 0.784f, 1f);
    private static readonly Color Sage = new(0.431f, 0.514f, 0.486f, 1f);
    private static readonly Color Warning = new(0.95f, 0.75f, 0.45f, 1f);
    private static readonly Color Locked = new(0.44f, 0.44f, 0.44f, 1f);
    private static readonly Color Here = new(0.35f, 0.85f, 0.90f, 1f);

    private static readonly Color Inside = new(0.84f, 0.73f, 0.55f, 1f);
    private static readonly Color InsideSelected = new(0.98f, 0.87f, 0.68f, 1f);
    private static readonly Color Outside = new(0.60f, 0.70f, 0.82f, 1f);
    private static readonly Color OutsideSelected = new(0.78f, 0.87f, 0.97f, 1f);
    private const float FocusedAlpha = 0.157f;
    private const float UnfocusedAlpha = 0.07f;

    private const int Regions = 0, Places = 1, Doors = 2;

    private sealed class Row
    {
        internal GameObject Root = null!;
        internal UISprite Strip = null!;
        internal UISprite Icon = null!;
        internal UILabel Label = null!;
        internal UILabel? Tag;
    }

    private const string InsideIcon = "ico_sheltered";
    private const string OutsideIcon = "ico_snowflake";
    private const string PathIcon = "map_transition_map";
    private const int IconSize = 22;
    private const float IconSpace = 30f;

    private const float TagSpace = 24f;

    private sealed class Column
    {
        internal float Center;
        internal float Width;
        internal UILabel Header = null!;
        internal UISprite Track = null!;
        internal UISprite Thumb = null!;
        internal readonly List<Row> Rows = new();
        internal int Count;
        internal int Selected;
        internal int Offset;
    }

    private static GameObject? _root;
    private static UIAtlas? _atlas;
    private static UIFont? _font;
    private static UILabel? _status;

    private static UILabel? _legend;

    private static UISprite? _travelStrip;
    private static UILabel? _travelLabel;

    private static bool _confirming;

    private static bool _vanillaChoice;
    private static GameObject? _modeRoot;
    private static UISprite? _modeSeamlessStrip, _modeVanillaStrip;
    private static UILabel? _modeSeamlessLabel, _modeVanillaLabel;
    private static int _confirmFrame;
    private static int _confirmEndedFrame = -10;

    internal static bool IsConfirming => _confirming;
    private static readonly Column[] _columns = new Column[3];

    private static List<PickerModel.Entry> _entries = new();
    private static List<(string Scene, string Display)> _regions = new();
    private static List<PickerModel.Place> _places = new();
    private static List<PickerModel.Entry> _doors = new();
    private static int _focus;

    internal static bool IsOpen => _root != null && _root.activeSelf;

    internal static bool TryOpen(out string? error)
    {
        error = null;
        try
        {
            if (_root == null && !Build(out error)) return false;
            SeamlessInteriorsCompat.VanillaMode = _vanillaChoice && SeamlessInteriorsCompat.Usable;
            LoadForCurrentScene();
            _root!.SetActive(true);
            PickerCommon.LockControls();
            PushInputContext();
            _status!.text = DefaultStatus();
            Redraw();
            GameAudioManager.PlayGUIMenuOpen();
            return true;
        }
        catch (System.Exception e)
        {
            error = e.ToString();
            PopInputContext();
            DestroyAll();
            PickerCommon.UnlockControls();
            return false;
        }
    }

    private static bool _contextPushed;

    private static void PushInputContext()
    {
        if (_contextPushed || _root == null) return;
        try
        {
            var panel = _root.GetComponent<UIPanel>();
            if (panel == null) { Log.Warning("[picker] no UIPanel to use as an input context; keys still reach the game."); return; }
            InputManager.PushContext(panel);
            _contextPushed = true;
        }
        catch (System.Exception e) { Log.Warning("[picker] could not take the input context; keys still reach the game. " + e.Message); }
    }

    private static void PopInputContext()
    {
        if (!_contextPushed) return;
        _contextPushed = false;
        try
        {
            var panel = _root != null ? _root.GetComponent<UIPanel>() : null;
            if (panel != null) InputManager.PopContext(panel);
        }
        catch (System.Exception e) { Log.Warning("[picker] could not release the input context. " + e.Message); }
    }

    internal static void Close()
    {
        _confirming = false;
        SeamlessInteriorsCompat.VanillaMode = false;
        PopInputContext();
        if (!IsOpen) return;
        _root!.SetActive(false);
        PickerCommon.UnlockControls();
        PlayCloseSound();
    }

    private static void PlayCloseSound()
    {
        GameAudioManager.PlayGUIButtonBack();
        try
        {
            var camera = GameManager.GetMainCamera();
            var view = camera != null && camera.transform.parent != null ? camera.transform.parent.gameObject : null;
            if (view != null) GameAudioManager.PlaySound("Play_CloseInventory", view);
        }
        catch (System.Exception e) { Log.Warning($"[picker] close sound failed: {e.Message}"); }
    }

    private static string DefaultStatus()
    {
        return Portal.DepartureIsInstanced() ? PickerCommon.InstancedWarning : "";
    }

    internal static bool Back()
    {

        return _confirming || Time.frameCount - _confirmEndedFrame <= 1;
    }

    internal static void HandleInput()
    {
        if (_confirming)
        {

            if (Time.frameCount > _confirmFrame + 1 && !InterfaceManager.IsPanelEnabled<Panel_Confirmation>())
            {
                EndConfirming();
                Log.Warning("[picker] the confirmation dialog closed without answering; the picker is usable again.");
            }
            return;
        }
        var context = InputManager.m_CurrentContext;

        float vertical = Utils.GetMenuMovementVertical(context, true, true);
        if (vertical > 0.1f) Move(-1);
        else if (vertical < -0.1f) Move(1);

        float horizontal = Utils.GetMenuMovementHorizontal(context, true, true);
        if (horizontal > 0.1f) MoveFocus(1);
        else if (horizontal < -0.1f) MoveFocus(-1);

        float wheel = InputManager.GetAxisScrollWheel(context);
        if (wheel > 0.01f) Scroll(ColumnUnderPointer(), -3);
        else if (wheel < -0.01f) Scroll(ColumnUnderPointer(), 3);

        if (InputManager.GetContinuePressed(context)) Continue();
    }

    private static void LoadForCurrentScene()
    {
        _entries = PickerModel.Entries();
        _regions = PickerModel.Regions(_entries);

        string region = PickerCommon.RegionForCurrentScene(null);
        var regions = _columns[Regions];
        regions.Count = _regions.Count;
        regions.Selected = System.Math.Max(0, _regions.FindIndex(r => r.Scene == region));
        regions.Offset = 0;
        EnsureVisible(regions);

        LoadPlaces();

        _focus = HerePlaceIndex() >= 0 ? Places : Regions;
    }

    private static void LoadPlaces()
    {
        var regions = _columns[Regions];
        _places = regions.Count == 0 ? new List<PickerModel.Place>() : PickerModel.Places(_entries, _regions[regions.Selected].Scene);
        var places = _columns[Places];
        places.Count = _places.Count;
        int here = HerePlaceIndex();

        places.Selected = here >= 0 ? here : System.Math.Max(0, _places.FindIndex(p => p.Key != PickerModel.PathsKey));
        places.Offset = 0;
        EnsureVisible(places);
        LoadDoors();
    }

    private static void LoadDoors()
    {
        var places = _columns[Places];
        _doors = places.Count == 0 ? new List<PickerModel.Entry>() : _places[places.Selected].Doors;
        var doors = _columns[Doors];
        doors.Count = _doors.Count;
        doors.Selected = 0;
        doors.Offset = 0;
    }

    private static int HerePlaceIndex()
    {
        string here = GameManager.m_ActiveScene;
        for (int i = 0; i < _places.Count; i++)
            foreach (var e in _places[i].Doors)
                if (IsInside(e.Dest, here)) return i;
        return -1;
    }

    private static bool IsInside(Destination d, string here) => d.Scene == here && !PickerCommon.IsOutdoor(d);

    private static void Move(int delta)
    {
        var column = _columns[_focus];
        if (column.Count == 0) return;
        int next = Mathf.Clamp(column.Selected + delta, 0, column.Count - 1);
        if (next == column.Selected) return;
        column.Selected = next;
        EnsureVisible(column);
        if (_focus == Regions) LoadPlaces();
        else if (_focus == Places) LoadDoors();
        _status!.text = DefaultStatus();
        GameAudioManager.PlayGUIScroll();
        Redraw();
    }

    private static void MoveFocus(int delta)
    {
        int next = Mathf.Clamp(_focus + delta, Regions, Doors);
        if (next == _focus || _columns[next].Count == 0) return;
        _focus = next;
        _status!.text = DefaultStatus();
        GameAudioManager.PlayGUIScroll();
        Redraw();
    }

    private static void Continue()
    {
        if (_focus < Doors) { MoveFocus(1); return; }
        TravelFrom(_columns[Doors].Selected);
    }

    private static void Scroll(int columnIndex, int delta)
    {
        var column = _columns[columnIndex];
        int next = Mathf.Clamp(column.Offset + delta, 0, System.Math.Max(0, column.Count - VisibleRows));
        if (next == column.Offset) return;
        column.Offset = next;
        Redraw();
    }

    private static void OnRowClicked(int columnIndex, int row)
    {
        var column = _columns[columnIndex];
        int index = column.Offset + row;
        if (index >= column.Count) return;

        if (_confirming) return;
        _focus = columnIndex;
        _status!.text = DefaultStatus();
        if (index != column.Selected)
        {
            column.Selected = index;
            if (columnIndex == Regions) LoadPlaces();
            else if (columnIndex == Places) LoadDoors();
        }
        GameAudioManager.PlayGUIButtonClick();
        Redraw();

        float now = Time.unscaledTime;
        bool twice = columnIndex == Doors && index == _lastClickIndex && now - _lastClickTime <= DoubleClickSeconds;
        _lastClickIndex = columnIndex == Doors ? index : -1;
        _lastClickTime = now;
        if (twice)
        {
            _lastClickIndex = -1;
            TravelFrom(index);
            if (!_confirming) Redraw();
        }
    }

    private const float DoubleClickSeconds = 0.35f;
    private static int _lastClickIndex = -1;
    private static float _lastClickTime;

    private static void TravelFrom(int index)
    {
        if (index < 0 || index >= _doors.Count) return;
        var dest = _doors[index].Dest;

        if (_doors[index].Locked)
        {
            _status!.text = PickerModel.LockedHint + ".";
            GameAudioManager.PlayGUIError();
            return;
        }
        string? refusal = PickerCommon.RefusalFromHere(dest);
        if (refusal != null)
        {
            _status!.text = refusal;
            GameAudioManager.PlayGUIError();
            return;
        }

        GameAudioManager.PlayGuiConfirm();
        _confirmFrame = Time.frameCount;
        _confirming = PickerCommon.ConfirmAndTravel(dest,
            onYes: () => { EndConfirming(); Picker.Close(); },
            onNo: () => { EndConfirming(); _status!.text = DefaultStatus(); Redraw(); });
    }

    private static void EndConfirming()
    {
        PickerCommon.RestoreConfirmBox();
        _confirming = false;
        _confirmEndedFrame = Time.frameCount;
    }

    private static void SetVanilla(bool vanilla)
    {
        if (_confirming || !IsOpen || vanilla == _vanillaChoice) return;
        _vanillaChoice = vanilla;
        SeamlessInteriorsCompat.VanillaMode = vanilla && SeamlessInteriorsCompat.Usable;
        GameAudioManager.PlayGUIButtonClick();
        ReloadKeepingSelection();
        Redraw();
    }

    private static void ReloadKeepingSelection()
    {
        var regions = _columns[Regions];
        var places = _columns[Places];
        var doors = _columns[Doors];
        string? region = regions.Count > 0 ? _regions[regions.Selected].Scene : null;
        string? place = places.Count > 0 ? _places[places.Selected].Key : null;
        string? exit = doors.Count > 0 ? _doors[doors.Selected].Dest.ExitPoint : null;
        string? scene = doors.Count > 0 ? _doors[doors.Selected].Dest.Scene : null;

        _entries = PickerModel.Entries();
        _regions = PickerModel.Regions(_entries);
        regions.Count = _regions.Count;
        regions.Selected = System.Math.Max(0, _regions.FindIndex(r => r.Scene == region));
        EnsureVisible(regions);

        _places = regions.Count == 0 ? new List<PickerModel.Place>() : PickerModel.Places(_entries, _regions[regions.Selected].Scene);
        places.Count = _places.Count;
        places.Selected = System.Math.Max(0, _places.FindIndex(p => p.Key == place));
        EnsureVisible(places);

        _doors = places.Count == 0 ? new List<PickerModel.Entry>() : _places[places.Selected].Doors;
        doors.Count = _doors.Count;
        doors.Selected = System.Math.Max(0, _doors.FindIndex(e => e.Dest.ExitPoint == exit && e.Dest.Scene == scene));
        EnsureVisible(doors);
    }

    private static void OnTravelClicked()
    {
        if (_confirming || !IsOpen) return;
        _focus = Doors;
        TravelFrom(_columns[Doors].Selected);
        if (!_confirming) Redraw();
    }

    private static int ColumnUnderPointer()
    {
        float uiX = (UnityEngine.Input.mousePosition.x / Mathf.Max(1f, Screen.width) - 0.5f) * 720f * Screen.width / Mathf.Max(1f, Screen.height);
        for (int i = 0; i < _columns.Length; i++)
            if (Mathf.Abs(uiX - _columns[i].Center) <= _columns[i].Width / 2f) return i;
        return _focus;
    }

    private static void EnsureVisible(Column column)
    {
        if (column.Selected < column.Offset) column.Offset = column.Selected;
        if (column.Selected >= column.Offset + VisibleRows) column.Offset = column.Selected - VisibleRows + 1;
        column.Offset = Mathf.Clamp(column.Offset, 0, System.Math.Max(0, column.Count - VisibleRows));
    }

    private static void Redraw()
    {
        string here = GameManager.m_ActiveScene;
        var regions = _columns[Regions];
        var places = _columns[Places];

        _columns[Regions].Header.text = "Region";
        _columns[Places].Header.text = regions.Count > 0 ? _regions[regions.Selected].Display : "";
        _columns[Doors].Header.text = places.Count > 0 ? _places[places.Selected].Display : "";

        string? hereRegion = PickerCommon.RegionPlayerIsIn();
        DrawColumn(Regions, i => _regions[i].Display, i => _regions[i].Scene == hereRegion, _ => false);
        DrawColumn(Places, i => _places[i].Display + (_places[i].AllLocked ? " *" : ""),
                   i => _places[i].Doors.Exists(e => IsInside(e.Dest, here)), i => _places[i].AllLocked,
                   icon: i => _places[i].Doors.Count > 0 ? _places[i].Doors[0].Dest.PlaceIcon : null);

        DrawColumn(Doors, i => DoorWords(_doors[i].Dest.Door) + (_doors[i].Locked ? " *" : ""),
                   _ => false, i => _doors[i].Locked,
                   i => PickerCommon.IsOutdoor(_doors[i].Dest) ? (Outside, OutsideSelected) : (Inside, InsideSelected),
                   i => DoorIcon(_doors[i].Dest), tag: i => IsSeamlessDoor(_doors[i].Dest) ? "SI" : "");
        DrawTravelButton();
        DrawModeSelector();
    }

    private static bool IsSeamlessDoor(Destination d)
    {
        if (!SeamlessInteriorsCompat.Present) return false;
        if (d.Place == PickerModel.PathsKey || d.Door.StartsWith("Toward ", System.StringComparison.Ordinal) || PickerCommon.IsOutdoor(d)) return false;
        return SeamlessInteriorsCompat.Handles(d);
    }

    private static string DoorIcon(Destination d)
    {
        if (d.Door.StartsWith("Toward ", System.StringComparison.Ordinal) || d.Place == PickerModel.PathsKey) return PathIcon;
        return PickerCommon.IsOutdoor(d) ? OutsideIcon : InsideIcon;
    }

    private static string DoorWords(string door)
    {
        foreach (var word in new[] { "Inside", "Outside" })
        {
            if (!door.StartsWith(word, System.StringComparison.Ordinal)) continue;
            string rest = door.Substring(word.Length).TrimStart(',', ' ');
            return rest.Length == 0 ? door : char.ToUpperInvariant(rest[0]) + rest.Substring(1);
        }
        return door;
    }

    private static void DrawTravelButton()
    {
        var doors = _columns[Doors];
        bool usable = doors.Count > 0 && !_doors[doors.Selected].Locked;
        _travelLabel!.color = usable ? White : Locked;
        _travelStrip!.color = new Color(White.r, White.g, White.b, usable ? FocusedAlpha : UnfocusedAlpha);
    }

    private static void DrawColumn(int columnIndex, System.Func<int, string> label, System.Func<int, bool> isHere,
                                   System.Func<int, bool> isLocked, System.Func<int, (Color Normal, Color Selected)>? tint = null,
                                   System.Func<int, string?>? icon = null, System.Func<int, string>? tag = null)
    {
        var column = _columns[columnIndex];
        bool focused = columnIndex == _focus;
        for (int i = 0; i < column.Rows.Count; i++)
        {
            var row = column.Rows[i];
            int index = column.Offset + i;
            bool used = index < column.Count;
            row.Root.SetActive(used);
            if (!used) continue;

            bool selected = index == column.Selected;
            row.Label.text = label(index);

            row.Label.color = isLocked(index) ? Locked : isHere(index) ? Here : selected ? White : Grey;
            var (normal, bright) = tint != null ? tint(index) : (Grey, White);
            Color iconColor = isLocked(index) ? Locked : selected ? bright : normal;

            string? sprite = icon?.Invoke(index);
            bool showIcon = !string.IsNullOrEmpty(sprite) && _atlas!.GetSprite(sprite) != null;
            row.Icon.gameObject.SetActive(showIcon);
            if (showIcon)
            {
                if (row.Icon.spriteName != sprite) row.Icon.spriteName = sprite;

                row.Icon.color = iconColor;
            }
            if (row.Tag != null)
            {
                string mark = tag?.Invoke(index) ?? "";
                row.Tag.text = mark;
                row.Tag.color = isLocked(index) ? Locked : selected ? White : Grey;
            }
            float alpha = selected ? (focused ? FocusedAlpha : UnfocusedAlpha) : 0f;
            row.Strip.color = new Color(White.r, White.g, White.b, alpha);
        }

        bool scrolls = column.Count > VisibleRows;
        column.Track.gameObject.SetActive(scrolls);
        column.Thumb.gameObject.SetActive(scrolls);
        if (!scrolls) return;
        float track = VisibleRows * RowPitch;
        float thumb = Mathf.Max(24f, track * VisibleRows / column.Count);
        float t = (float)column.Offset / (column.Count - VisibleRows);
        float top = FirstRowY + RowPitch / 2f;
        column.Thumb.height = (int)thumb;
        column.Thumb.transform.localPosition = new Vector3(column.Center + column.Width / 2f - 6f, top - thumb / 2f - t * (track - thumb), 0f);
    }

    private static bool Build(out string? error)
    {
        error = null;

        var anchor = InterfaceManager.s_CommonUIAnchor;
        if (anchor == null) { error = "InterfaceManager.s_CommonUIAnchor is null"; return false; }

        var hud = InterfaceManager.GetPanel<Panel_HUD>();
        _atlas = hud != null && hud.m_AltFireGamepadButtonSprite != null ? hud.m_AltFireGamepadButtonSprite.atlas : null;
        if (_atlas == null) { error = "no UI atlas: Panel_HUD.m_AltFireGamepadButtonSprite unavailable"; return false; }

        _font = GameManager.GetFontManager()?.GetUIFontForCharacterSet(Localization.GetCharacterSet());
        if (_font == null) { error = "no UI font from FontManager"; return false; }

        var panel = NGUITools.AddChild<UIPanel>(anchor.gameObject);
        _root = panel.gameObject;
        _root.name = "TLDTeleporter_NguiPicker";
        panel.depth = PanelDepth;

        float halfScreen = 360f * Screen.width / Mathf.Max(1f, Screen.height);
        float usable = Mathf.Min(MaxWidth, halfScreen * 2f - 60f);
        float left = -usable / 2f;

        int backdropWidth = (int)(halfScreen * 2f) + 160;
        Sprite(_root, "whiteSquare", new Color(0f, 0f, 0f, 1f), backdropWidth, 880, -1000, Vector3.zero, UIBasicSprite.Type.Simple);
        Sprite(_root, "bg_texture1", new Color(0.168f, 0.213f, 0.206f, 1f), backdropWidth, 880, -950, Vector3.zero, UIBasicSprite.Type.Sliced);

        Label(_root, "Teleporter", 26, true, true, White, (int)usable, new Vector3(left, 290f, 0f));
        Sprite(_root, "linebreak04", White, (int)usable, 2, 2, new Vector3(0f, 266f, 0f), UIBasicSprite.Type.Simple);
        _status = Label(_root, "", 14, false, false, Warning, (int)usable, new Vector3(left, -262f, 0f));

        Label(_root, "Arrows  move      Enter or double-click  travel      Esc  close", 14, false, false, Grey, (int)usable, new Vector3(left, -290f, 0f));

        BuildTravelButton(left + usable, -258f);
        BuildModeSelector(left + usable, 288f);

        float widths = usable - Gap * (Shares.Length - 1);
        float x = left;
        for (int c = 0; c < _columns.Length; c++)
        {
            float width = widths * Shares[c];
            _columns[c] = BuildColumn(c, x + width / 2f, width);
            x += width + Gap;
        }

        var doorsColumn = _columns[Doors];
        _legend = Label(_root, "* " + PickerModel.LockedHint, 14, false, false, Locked, (int)doorsColumn.Width,
                        new Vector3(doorsColumn.Center + doorsColumn.Width / 2f, 257f, 0f));
        _legend.alignment = NGUIText.Alignment.Right;
        _legend.pivot = UIWidget.Pivot.Right;

        _root.SetActive(false);
        return true;
    }

    private static void BuildTravelButton(float right, float y)
    {
        const float width = 150f, height = 30f;
        var go = NGUITools.AddChild(_root);
        go.name = "TravelButton";
        go.transform.localPosition = new Vector3(right - width / 2f, y, 0f);
        var collider = go.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(width, height, 1f);
        var button = go.AddComponent<UIButton>();
        EventDelegate.Set(button.onClick, new System.Action(OnTravelClicked));

        _travelStrip = Sprite(go, "selectionList2", new Color(White.r, White.g, White.b, FocusedAlpha), (int)width, (int)height, 5, Vector3.zero);
        _travelLabel = Label(go, "Travel", 16, true, true, White, (int)width, Vector3.zero);
        _travelLabel.alignment = NGUIText.Alignment.Center;
        _travelLabel.pivot = UIWidget.Pivot.Center;
        _travelLabel.transform.localPosition = Vector3.zero;
    }

    private static void BuildModeSelector(float right, float y)
    {
        const float width = 232f, height = 28f, gap = 2f;
        _modeRoot = NGUITools.AddChild(_root);
        _modeRoot.name = "ModeSelector";
        (UISprite, UILabel) One(string name, string text, float centerX, bool vanilla)
        {
            var go = NGUITools.AddChild(_modeRoot);
            go.name = name;
            go.transform.localPosition = new Vector3(centerX, y, 0f);
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(width, height, 1f);
            var button = go.AddComponent<UIButton>();
            EventDelegate.Set(button.onClick, new System.Action(() => SetVanilla(vanilla)));
            var strip = Sprite(go, "selectionList2", new Color(White.r, White.g, White.b, UnfocusedAlpha), (int)width, (int)height, 5, Vector3.zero);
            var label = Label(go, text, 14, true, false, Grey, (int)width, Vector3.zero);
            label.alignment = NGUIText.Alignment.Center;
            label.pivot = UIWidget.Pivot.Center;
            label.transform.localPosition = Vector3.zero;
            return (strip, label);
        }
        (_modeVanillaStrip, _modeVanillaLabel) = One("ModeVanilla", "Vanilla Interior Scenes", right - width / 2f, true);

        (_modeSeamlessStrip, _modeSeamlessLabel) = One("ModeSeamless", "SI: Outdoor Seamless Interiors", right - width * 1.5f - gap, false);
        _modeRoot.SetActive(false);
    }

    private static void DrawModeSelector()
    {
        bool show = SeamlessInteriorsCompat.Usable;
        _modeRoot!.SetActive(show);
        if (!show) return;
        bool vanilla = _vanillaChoice;

        const float pickedStrip = 0.45f, otherStrip = 0.04f;
        var otherText = new Color(0.45f, 0.45f, 0.45f, 1f);
        _modeSeamlessStrip!.color = new Color(White.r, White.g, White.b, vanilla ? otherStrip : pickedStrip);
        _modeVanillaStrip!.color = new Color(White.r, White.g, White.b, vanilla ? pickedStrip : otherStrip);
        _modeSeamlessLabel!.color = vanilla ? otherText : White;
        _modeVanillaLabel!.color = vanilla ? White : otherText;
    }

    private static Column BuildColumn(int columnIndex, float center, float width)
    {
        var column = new Column { Center = center, Width = width };
        float listHeight = VisibleRows * RowPitch;
        float listCenterY = FirstRowY + RowPitch / 2f - listHeight / 2f;

        column.Header = Label(_root!, "", 16, true, true, White, (int)width - 12, new Vector3(center - width / 2f + 6f, HeaderY, 0f));

        for (int i = 0; i < VisibleRows; i++)
        {
            var go = NGUITools.AddChild(_root);
            go.name = $"Column{columnIndex}Row{i}";
            go.transform.localPosition = new Vector3(center, FirstRowY - i * RowPitch, 0f);

            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(width - 16f, RowPitch, 1f);
            var button = go.AddComponent<UIButton>();
            int c = columnIndex, r = i;
            EventDelegate.Set(button.onClick, new System.Action(() => OnRowClicked(c, r)));

            var strip = Sprite(go, "selectionList2", new Color(White.r, White.g, White.b, 0f), (int)width, (int)RowPitch, 5, Vector3.zero);

            bool iconed = columnIndex != Regions;
            float slot = columnIndex == Doors ? TagSpace : 0f;
            float textLeft = -width / 2f + 12f + slot + (iconed ? IconSpace : 0f);
            var icon = Sprite(go, InsideIcon, Grey, IconSize, IconSize, 8, new Vector3(-width / 2f + 12f + slot + IconSize / 2f, 0f, 0f), UIBasicSprite.Type.Simple);
            var tagLabel = columnIndex == Doors ? Label(go, "", 12, true, false, Grey, (int)TagSpace, new Vector3(-width / 2f + 13f, 0f, 0f)) : null;
            icon.gameObject.SetActive(false);

            var label = Label(go, "", 15, false, false, Grey, (int)(width - 34 - slot - (iconed ? IconSpace : 0f)), new Vector3(textLeft, 0f, 0f));
            column.Rows.Add(new Row { Root = go, Strip = strip, Icon = icon, Label = label, Tag = tagLabel });
        }

        column.Track = Sprite(_root!, "scrollbar_back", White, 3, (int)listHeight, 3, new Vector3(center + width / 2f - 6f, listCenterY, 0f));
        column.Thumb = Sprite(_root!, "scrollbar_thumb", White, 6, 60, 4, Vector3.zero);
        return column;
    }

    private static UISprite Sprite(GameObject parent, string name, Color color, int width, int height, int depth, Vector3 position,
        UIBasicSprite.Type type = UIBasicSprite.Type.Sliced)
    {
        if (_atlas!.GetSprite(name) == null)
            Log.Warning($"[picker] the UI atlas has no sprite '{name}'; it will draw nothing");
        var sprite = NGUITools.AddChild<UISprite>(parent);
        sprite.atlas = _atlas;
        sprite.spriteName = name;
        sprite.type = type;
        sprite.color = color;
        sprite.width = width;
        sprite.height = height;
        sprite.depth = depth;
        sprite.transform.localPosition = position;
        return sprite;
    }

    private static UILabel Label(GameObject parent, string text, int size, bool bold, bool caps, Color color, int width, Vector3 position)
    {
        var label = NGUITools.AddChild<UILabel>(parent);
        label.bitmapFont = _font;
        label.fontSize = size;
        label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        label.capsLock = caps;
        label.color = color;
        label.alignment = NGUIText.Alignment.Left;
        label.pivot = UIWidget.Pivot.Left;
        label.overflowMethod = UILabel.Overflow.ClampContent;
        label.width = width;
        label.height = size + 10;
        label.depth = 10;
        label.transform.localPosition = position;
        label.text = text;
        return label;
    }

    private static void DestroyAll()
    {
        PopInputContext();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
        for (int i = 0; i < _columns.Length; i++) _columns[i] = null!;
    }
}
