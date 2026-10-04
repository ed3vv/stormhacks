using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The heads-up display and menus, in UI Toolkit (S3Hud.uxml + Hud.uss, laid out at 1920×1080 and
/// scaled to the screen). S3Golf owns the game state and pushes it here every frame; this class only
/// shows it, and reports menu clicks back through callbacks.
/// </summary>
public class S3HudView : MonoBehaviour
{
    public enum Menu { None, Pause, Levels, Results, HoleDone, Title }
    public enum MarkerKind { Cup, Gem, Pole, Ball }

    public struct LevelEntry
    {
        public string title, sub;
        public int best;          // strokes, 0 if never finished
        public bool puzzle;       // shows a tick instead of a score
        public LevelEntry(string title, string sub, int best, bool puzzle) { this.title = title; this.sub = sub; this.best = best; this.puzzle = puzzle; }
    }

    public struct LevelColumn
    {
        public string title, sub, modifier;
        public LevelEntry[] entries;
    }

    UIDocument doc;
    S3Player player;
    Camera cam;
    VisualElement root, hud, replayOverlay, replayFill, markers, crosshair, pips, chips, sealedChip, stats, statLaunch, hintCard, inventory, power, hints, status, prompt, menuLayer, bottom;
    Label replayLine, mapName, holeTitle, holeOf, sealedText, pinValue, launchValue, hintTitle, hintBody, bannerTitle, bannerSub, statusText, powerPct, poleLabel;
    Pennant pennant;
    Ribbon banner;
    SegmentBar powerBar;
    AngleGauge gauge;
    string chipsKey, hintsKey, inventoryKey;
    int pipCount = -1;
    float bannerUntil;
    readonly List<VisualElement> markerPool = new List<VisualElement>();
    int markersUsed;

    public Menu Current { get; private set; }

    /// <summary>The view on this GameObject, created (with its UIDocument) if needed.</summary>
    public static S3HudView Ensure(GameObject host, S3Player player)
    {
        var view = host.GetComponent<S3HudView>();
        if (!view) view = host.AddComponent<S3HudView>();
        view.player = player;
        view.cam = player.GetComponent<Camera>();
        view.Bind();
        return view;
    }

    void Bind()
    {
        if (root != null && root.panel != null) return;
        if (!doc) doc = GetComponent<UIDocument>();
        if (!doc)
        {
            doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = CreatePanelSettings();
            doc.visualTreeAsset = Resources.Load<VisualTreeAsset>("S3Hud");
        }
        doc.rootVisualElement.StretchToParentSize();   // the default theme does this, and we don't load it
        root = doc.rootVisualElement.Q("root");
        if (root == null) return;
        hud = root.Q("hud");
        markers = root.Q("markers");
        crosshair = root.Q("crosshair");
        pennant = root.Q<Pennant>("pennant");
        mapName = root.Q<Label>("mapName");
        holeTitle = root.Q<Label>("holeTitle");
        holeOf = root.Q<Label>("holeOf");
        pips = root.Q("pips");
        chips = root.Q("chips");
        sealedChip = root.Q("sealed");
        sealedText = root.Q<Label>("sealedText");
        stats = root.Q("stats");
        statLaunch = root.Q("statLaunch");
        pinValue = root.Q<Label>("pinValue");
        launchValue = root.Q<Label>("launchValue");
        gauge = root.Q<AngleGauge>("launchGauge");
        hintCard = root.Q("hintCard");
        hintTitle = root.Q<Label>("hintTitle");
        hintBody = root.Q<Label>("hintBody");
        banner = root.Q<Ribbon>("banner");
        bannerTitle = root.Q<Label>("bannerTitle");
        bannerSub = root.Q<Label>("bannerSub");
        bottom = root.Q("bottom");
        prompt = root.Q("prompt");
        status = root.Q("status");
        statusText = root.Q<Label>("statusText");
        inventory = root.Q("inventory");
        power = root.Q("power");
        powerPct = root.Q<Label>("powerPct");
        powerBar = root.Q<SegmentBar>("powerBar");
        poleLabel = root.Q<Label>("poleLabel");
        hints = root.Q("hints");
        menuLayer = root.Q("menuLayer");
        replayOverlay = root.Q("replay");
        replayFill = root.Q("replayFill");
        replayLine = root.Q<Label>("replayLine");
        chipsKey = hintsKey = inventoryKey = null;
        pipCount = -1;
        markerPool.Clear();
        Show(menuLayer, false);
        Current = Menu.None;
    }

    /// <summary>Laid out at 1920×1080 and scaled to the screen, so the HUD keeps its proportions at any resolution.</summary>
    static PanelSettings CreatePanelSettings()
    {
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.name = "S3 HUD Panel";
        settings.hideFlags = HideFlags.DontSave;
        settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("S3Theme");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 0.5f;
        settings.sortingOrder = 10;
        return settings;
    }

    bool Ready
    {
        get
        {
            if (root == null || root.panel == null) Bind();
            return root != null;
        }
    }

    void Update()
    {
        if (banner != null && banner.ClassListContains("banner--shown") && Time.realtimeSinceStartup > bannerUntil)
            banner.RemoveFromClassList("banner--shown");
    }

    static void Show(VisualElement e, bool on)
    {
        if (e != null) e.EnableInClassList("hidden", !on);
    }

    static Label L(string text, string classes)
    {
        var l = new Label(text);
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) l.AddToClassList(c);
        return l;
    }

    static T With<T>(T e, string classes) where T : VisualElement
    {
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) e.AddToClassList(c);
        return e;
    }

    static VisualElement Keys(VisualElement parent, string keys, bool small = false)
    {
        foreach (var k in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            parent.Add(L(k, small ? "key key--small" : "key"));
        return parent;
    }

    // ------------------------------------------------------------------ in-game HUD

    public void SetBiome(S3World.Biome b)
    {
        if (!Ready) return;
        root.EnableInClassList("biome-meadow", b == S3World.Biome.Meadow);
        root.EnableInClassList("biome-desert", b == S3World.Biome.Desert);
        root.EnableInClassList("biome-basin", b == S3World.Biome.Basin);
    }

    /// <summary>The pennant: map, "Hole 2 / 5", one pip per hole, and stat chips underneath.</summary>
    public void SetScorecard(string map, string title, string of, int count, int current, IList<(string label, string value)> chipList, string sealedLine)
    {
        if (!Ready) return;
        mapName.text = map.ToUpperInvariant();
        holeTitle.text = title;
        holeOf.text = of;
        if (pipCount != count)
        {
            pipCount = count;
            pips.Clear();
            for (int i = 0; i < count; i++) pips.Add(With(new ShapeIcon(S3Shape.Diamond), "pip"));
        }
        for (int i = 0; i < pips.childCount; i++)
        {
            pips[i].EnableInClassList("pip--done", i < current);
            pips[i].EnableInClassList("pip--now", i == current);
        }
        string key = "";
        foreach (var c in chipList) key += c.label + "\u0001" + c.value + "\u0002";
        if (key != chipsKey)
        {
            chipsKey = key;
            chips.Clear();
            foreach (var c in chipList)
            {
                var chip = With(new FacetPanel(), "chip");
                chip.Add(L(c.label, "chip-label"));
                if (!string.IsNullOrEmpty(c.value)) chip.Add(L(c.value, "display chip-value"));
                chips.Add(chip);
            }
        }
        Show(sealedChip, !string.IsNullOrEmpty(sealedLine));
        sealedText.text = sealedLine ?? "";
    }

    /// <summary>Top right: distance to the pin and (while aiming) the launch angle. Negative pin hides it all.</summary>
    public void SetStats(float pinMetres, float launchDegrees)
    {
        if (!Ready) return;
        Show(stats, pinMetres >= 0);
        if (pinMetres < 0) return;
        pinValue.text = $"{pinMetres:0}";
        Show(statLaunch, launchDegrees >= 0);
        if (launchDegrees < 0) return;
        launchValue.text = $"{launchDegrees:0}";
        gauge.Degrees = launchDegrees;
    }

    /// <summary>The power meter; poleMark (0..1) marks a straight-up shot that just crosses the pole, ghost the last shot.</summary>
    public void SetPower(bool visible, float value, float poleMark, float ghost)
    {
        if (!Ready) return;
        Show(power, visible);
        if (!visible) return;
        powerPct.text = $"{value * 100:0}%";
        powerBar.Value = value;
        powerBar.PoleMark = poleMark;
        powerBar.Ghost = ghost;
        Show(poleLabel, poleMark >= 0 && poleMark <= 1);
        if (poleMark >= 0) poleLabel.style.left = Length.Percent(Mathf.Clamp01(poleMark) * 100);
    }

    /// <summary>The key hints along the bottom, or a line of text instead (note) if one is given.</summary>
    public void SetHints(string note, IList<(string keys, string action)> items)
    {
        if (!Ready) return;
        string key = note ?? "";
        if (note == null && items != null) foreach (var it in items) key += it.keys + "\u0001" + it.action + "\u0002";
        if (key == hintsKey) return;
        hintsKey = key;
        hints.Clear();
        if (note != null) hints.Add(L(note, "hint-note"));
        else if (items != null)
            foreach (var it in items)
            {
                var item = With(new VisualElement(), "hint-item");
                Keys(item, it.keys);
                item.Add(L(it.action, "hint-action"));
                hints.Add(item);
            }
        Show(hints, hints.childCount > 0);
    }

    /// <summary>"Click to play" while the mouse is free; it takes the hints' place.</summary>
    public void SetPrompt(bool visible)
    {
        if (!Ready) return;
        Show(prompt, visible);
        bottom.EnableInClassList("bottom--prompt", visible);
    }

    public void SetStatus(string text)
    {
        if (!Ready) return;
        Show(status, !string.IsNullOrEmpty(text));
        statusText.text = text ?? "";
    }

    public void SetCrosshair(bool visible, bool onTarget)
    {
        if (!Ready) return;
        Show(crosshair, visible);
        crosshair.EnableInClassList("crosshair--target", onTarget);
    }

    public void SetHintCard(bool visible, string title, string body)
    {
        if (!Ready) return;
        Show(hintCard, visible);
        if (!visible) return;
        hintTitle.text = title;
        hintBody.text = body;
    }

    /// <summary>Puzzle pieces in hand: one slot per kind the level has.</summary>
    public void SetInventory(bool visible, int mirrors, int mirrorsLeft, int diffractors, int diffractorsLeft, bool holdingMirror)
    {
        if (!Ready) return;
        Show(inventory, visible);
        if (!visible) return;
        string key = $"{mirrors},{mirrorsLeft},{diffractors},{diffractorsLeft},{holdingMirror}";
        if (key == inventoryKey) return;
        inventoryKey = key;
        inventory.Clear();
        void Slot(string k, S3Shape shape, string iconClass, int left, bool held)
        {
            var slot = With(new FacetPanel(), "slot");
            slot.EnableInClassList("slot--held", held && left > 0);
            slot.EnableInClassList("slot--empty", left == 0);
            slot.Add(With(new ShapeIcon(shape), "slot-icon " + iconClass));
            slot.Add(With(L(k, "key key--small"), "slot-key"));
            slot.Add(L($"{left}", "display slot-count"));
            inventory.Add(slot);
        }
        if (mirrors > 0) Slot("1", S3Shape.Mirror, "slot-icon--mirror", mirrorsLeft, holdingMirror);
        if (diffractors > 0) Slot("2", S3Shape.Diffractor, "slot-icon--diffractor", diffractorsLeft, !holdingMirror);
    }

    /// <summary>The centre ribbon: first line big, the rest underneath. Gold to celebrate, ink for information.</summary>
    public void Banner(string text, float seconds, bool celebrate)
    {
        if (!Ready || string.IsNullOrEmpty(text)) return;
        int nl = text.IndexOf('\n');
        bannerTitle.text = nl < 0 ? text : text.Substring(0, nl);
        bannerSub.text = nl < 0 ? "" : text.Substring(nl + 1);
        Show(bannerSub, nl >= 0);
        banner.EnableInClassList("banner--info", !celebrate);
        bannerUntil = Time.realtimeSinceStartup + seconds;
        banner.RemoveFromClassList("banner--shown");
        banner.schedule.Execute(() => banner.AddToClassList("banner--shown")).StartingIn(30);
    }

    public void HideBanner() { if (Ready) bannerUntil = 0; }

    /// <summary>The instant replay's overlay: what's playing and how far along. The menus step aside meanwhile.</summary>
    public void SetReplay(bool active, string line, float progress)
    {
        if (!Ready) return;
        Show(replayOverlay, active);
        Show(menuLayer, !active && Current != Menu.None);
        if (!active) return;
        replayLine.text = line;
        replayFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100);
    }

    /// <summary>Hides the in-game HUD (the menus stay).</summary>
    public void SetHudVisible(bool on) { if (Ready) Show(hud, on); }

    // ------------------------------------------------------------------ waypoint markers

    public void BeginMarkers() => markersUsed = 0;

    public void EndMarkers()
    {
        for (int i = markersUsed; i < markerPool.Count; i++) Show(markerPool[i], false);
    }

    /// <summary>
    /// A waypoint at an S³ point, projected exactly like the shader projects geometry; off screen
    /// (or behind you) it sticks to the screen edge with an arrow pointing the way.
    /// </summary>
    public void Marker(Vector4 target, MarkerKind kind, string label, float alpha = 1, float scale = 1)
    {
        if (!Ready || !cam || root.panel == null) return;
        Matrix4x4 frame = player.ViewFrame();
        Vector4 q = Matrix4x4.Scale(new Vector3(1, 1, -1)) * frame.transpose * target;
        var dir = new Vector3(q.x, q.y, -q.z);          // Unity camera space: +z forward
        Rect area = root.layout;
        if (area.width < 1) return;
        const float m = 56;
        float top = 150, bottomLimit = area.height - 150;
        if (!hintCard.ClassListContains("hidden")) top = Mathf.Max(top, hintCard.worldBound.yMax + 16);
        Vector2 c = area.center, p;
        bool onScreen = false;
        if (dir.z > 1e-4f)
        {
            p = RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, cam.transform.TransformPoint(dir.normalized * 10f), cam);
            onScreen = p.x > m && p.x < area.width - m && p.y > m && p.y < area.height - m;
        }
        else p = c + new Vector2(dir.x, -dir.y).normalized * 1e4f;   // behind: push towards its side
        Vector2 v = p - c;
        if (!onScreen)
        {
            if (v.sqrMagnitude < 1e-6f) v = Vector2.up;
            float s = Mathf.Min((area.width / 2 - m) / Mathf.Max(Mathf.Abs(v.x), 1e-4f), (area.height / 2 - m) / Mathf.Max(Mathf.Abs(v.y), 1e-4f));
            p = c + v * s;
        }
        p.y = Mathf.Clamp(p.y, top, bottomLimit);

        VisualElement mk;
        if (markersUsed < markerPool.Count) mk = markerPool[markersUsed];
        else
        {
            mk = With(new VisualElement(), "marker");
            mk.pickingMode = PickingMode.Ignore;
            mk.Add(With(new ShapeIcon(), "marker-icon"));
            mk.Add(With(new ShapeIcon(S3Shape.Chevron), "marker-arrow"));
            var tag = With(new FacetPanel(), "marker-tag");
            tag.Add(L("", "marker-label"));
            mk.Add(tag);
            markers.Add(mk);
            markerPool.Add(mk);
        }
        markersUsed++;
        Show(mk, true);
        foreach (MarkerKind k in Enum.GetValues(typeof(MarkerKind)))
            mk.EnableInClassList("marker--" + k.ToString().ToLowerInvariant(), k == kind);
        var icon = (ShapeIcon)mk[0];
        icon.Shape = kind == MarkerKind.Cup ? S3Shape.Flag : kind == MarkerKind.Gem ? S3Shape.Gem : kind == MarkerKind.Pole ? S3Shape.Ring : S3Shape.Ball;
        var arrow = mk[1];
        Show(arrow, !onScreen);
        if (!onScreen)
        {
            Vector2 d = v.normalized;
            arrow.style.translate = new Translate(d.x * 26, d.y * 26);
            arrow.style.rotate = new Rotate(Angle.Degrees(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90));
        }
        ((Label)mk[2][0]).text = label;
        Show(mk[2], !string.IsNullOrEmpty(label));
        mk.style.left = p.x;
        mk.style.top = p.y;
        mk.style.opacity = alpha;
        mk.style.scale = new Scale(new Vector3(scale, scale, 1));
    }

    // ------------------------------------------------------------------ menus

    VisualElement OpenMenu(Menu which)
    {
        if (root == null || root.panel == null) Bind();
        Current = which;
        menuLayer.Clear();
        menuLayer.EnableInClassList("menu-layer--light", which == Menu.HoleDone);
        menuLayer.EnableInClassList("menu-layer--title", which == Menu.Title);
        Show(menuLayer, true);
        HideBanner();
        return menuLayer;
    }

    public void CloseMenu()
    {
        if (!Ready) return;
        Current = Menu.None;
        menuLayer.Clear();
        Show(menuLayer, false);
    }

    static VisualElement Title(string text)
    {
        var t = With(new Ribbon(), "menu-title");
        t.Add(L(text, "display menu-title-label"));
        return t;
    }

    static VisualElement Clickable(VisualElement e, Action onClick)
    {
        e.AddManipulator(new Clickable(() => { S3Audio.Click(); onClick(); }));
        return e;
    }

    static VisualElement MenuButton(string label, string key, bool primary, Action onClick)
    {
        var b = With(new FacetPanel(), primary ? "menu-button menu-button--primary" : "menu-button");
        b.Add(L(label, "display menu-button-label"));
        b.Add(L(key, "key"));
        return Clickable(b, onClick);
    }

    static VisualElement Foot(IList<(string keys, string action)> items)
    {
        var foot = With(new VisualElement(), "menu-foot");
        foreach (var it in items)
        {
            Keys(foot, it.keys);
            foot.Add(L(it.action, "menu-foot-label"));
        }
        return foot;
    }

    /// <summary>Every hole and puzzle, a column per map; the one you're on is gold.</summary>
    public void ShowLevels(IList<LevelColumn> columns, int currentColumn, int currentIndex, Action<int, int> pick)
    {
        var layer = OpenMenu(Menu.Levels);
        var box = With(new VisualElement(), "levels");
        box.Add(Title("Choose a level"));
        var cols = With(new VisualElement(), "levels-columns");
        for (int ci = 0; ci < columns.Count; ci++)
        {
            var colData = columns[ci];
            var col = With(new VisualElement(), "level-col");
            var head = With(new Pennant(), "level-col-head " + colData.modifier);
            head.Add(L(colData.sub.ToUpperInvariant(), "eyebrow level-col-sub"));
            head.Add(L(colData.title, "display level-col-title"));
            col.Add(head);
            for (int i = 0; i < colData.entries.Length; i++)
            {
                var e = colData.entries[i];
                var card = With(new FacetPanel(), "level-card");
                card.EnableInClassList("level-card--current", ci == currentColumn && i == currentIndex);
                card.Add(L($"{i + 1}", "display level-num"));
                var text = With(new VisualElement(), "level-text");
                text.Add(L(e.title, "level-card-title"));
                text.Add(L(e.sub, "level-card-sub"));
                card.Add(text);
                string best = e.best <= 0 ? "–" : e.puzzle ? "Solved" : $"Best {e.best}";
                var bestLabel = L(best, "display level-best");
                bestLabel.EnableInClassList("level-best--none", e.best <= 0);
                card.Add(bestLabel);
                int c = ci, idx = i;
                Clickable(card, () => pick(c, idx));
                col.Add(card);
            }
            cols.Add(col);
        }
        box.Add(cols);
        box.Add(Foot(new[] { ("L", "Close"), ("Esc", "Pause menu") }));
        layer.Add(box);
    }

    static readonly (string group, (string keys, string action)[] rows)[] Controls =
    {
        ("GOLF", new[] { ("A D", "Aim"), ("W S", "Launch height"), ("Shift", "Fine aim"), ("Space", "Power, then shoot"), ("RMB", "Cancel"), ("C", "Level the view") }),
        ("YOUR BALL", new[] { ("Tab", "Walk around"), ("F", "Back to the ball"), ("B", "Replay last spot (+1)"), ("R", "Restart hole"), ("N", "Start over") }),
        ("LASER PUZZLES", new[] { ("LMB", "Place or pick up"), ("Q E", "Turn piece"), ("1 2", "Switch piece"), ("H", "Puzzle hint") }),
        ("WALKING", new[] { ("W A S D", "Walk"), ("Shift", "Run"), ("Space", "Jump"), ("M", "Next map"), ("G", "Graphics mode") }),
    };

    /// <summary>The pause menu: resume / restart / levels on the left, the controls on the right.</summary>
    public void ShowPause(string restartLabel, Action resume, Action restart, Action levels, string graphicsLabel, Action toggleGraphics)
    {
        var layer = OpenMenu(Menu.Pause);
        var box = With(new VisualElement(), "pause");
        var left = With(new VisualElement(), "pause-left");
        left.Add(Title("Paused"));
        left.Add(MenuButton("Resume", "Esc", true, resume));
        left.Add(MenuButton(restartLabel, "R", false, restart));
        left.Add(MenuButton("Level select", "L", false, levels));
        left.Add(MenuButton(graphicsLabel, "G", false, toggleGraphics));
        box.Add(left);

        var controls = With(new FacetPanel(), "controls");
        controls.Add(L("Controls", "display controls-heading"));
        foreach (var (group, rows) in Controls)
        {
            controls.Add(L(group, "eyebrow controls-group"));
            var row = With(new VisualElement(), "controls-row");
            foreach (var (keys, action) in rows)
            {
                var item = Keys(With(new VisualElement(), "control"), keys, true);
                item.Add(L(action, "control-action"));
                row.Add(item);
            }
            controls.Add(row);
        }
        box.Add(controls);
        layer.Add(box);
    }

    /// <summary>
    /// After the ball drops: how it went, and Watch replay (if there is one) / Replay hole / Next. It pops in
    /// over a light dim so the confetti still shows.
    /// </summary>
    public void ShowHoleResult(int hole, string name, bool good, string line, Action watch, Action replayHole, Action next, string nextLabel)
    {
        var layer = OpenMenu(Menu.HoleDone);
        var card = With(new VisualElement(), "hole-card");
        card.Add(L($"HOLE {hole} COMPLETE", "eyebrow hole-card-eyebrow"));
        var title = With(new Ribbon(), good ? "hole-card-title" : "hole-card-title hole-card-title--plain");
        title.Add(L(name, "display hole-card-name"));
        card.Add(title);
        card.Add(L(line, "hole-card-line"));
        var row = With(new VisualElement(), "hole-card-buttons");
        if (watch != null) row.Add(MenuButton("Watch replay", "W", false, watch));
        row.Add(MenuButton("Replay hole", "R", false, replayHole));
        row.Add(MenuButton(nextLabel, "Enter", true, next));
        card.Add(row);
        layer.Add(card);
        card.schedule.Execute(() => card.AddToClassList("hole-card--in")).StartingIn(30);
    }

    /// <summary>
    /// The title screen, over the world turning as a glass ball: "Antipolf" on the big gold ribbon, flanked by
    /// the pole rings (the marker for a shot's antipode), a tagline, then Play and Level select.
    /// </summary>
    public void ShowTitle(Action play, Action levels)
    {
        var layer = OpenMenu(Menu.Title);
        var box = With(new VisualElement(), "title-screen");
        box.Add(L("MINI GOLF ON A THREE-SPHERE", "eyebrow title-eyebrow"));
        var ribbon = With(new Ribbon(), "title-ribbon");
        var row = With(new VisualElement(), "row title-row");
        row.Add(With(new ShapeIcon(S3Shape.Ring), "title-ring"));
        row.Add(L("Antipolf", "display title-name"));
        row.Add(With(new ShapeIcon(S3Shape.Ring), "title-ring"));
        ribbon.Add(row);
        box.Add(ribbon);
        box.Add(L("Every straight line comes back around. Hit it high enough and it lands on the far side of the world.", "title-tagline"));
        var buttons = With(new VisualElement(), "title-buttons");
        buttons.Add(MenuButton("Play", "Enter", true, play));
        buttons.Add(MenuButton("Level select", "L", false, levels));
        box.Add(buttons);
        layer.Add(box);
        box.schedule.Execute(() => box.AddToClassList("title-screen--in")).StartingIn(30);
    }

    /// <summary>End of a map: a golf scorecard, hole by hole, with the total against par.</summary>
    public void ShowResults(string title, IList<int> par, IList<int> strokes, IList<(string keys, string action)> foot)
    {
        var layer = OpenMenu(Menu.Results);
        var box = With(new VisualElement(), "results");
        box.Add(Title(title));
        var table = With(new FacetPanel(), "sc-table");

        var head = With(new VisualElement(), "sc-row");
        head.Add(L("", "sc-name"));
        for (int i = 0; i < par.Count; i++) head.Add(L($"HOLE {i + 1}", "eyebrow sc-head"));
        head.Add(With(L("TOTAL", "eyebrow sc-head"), "sc-cell--total"));
        table.Add(head);

        int parTotal = 0, strokeTotal = 0, parPlayed = 0;
        bool complete = true;
        var parRow = With(new VisualElement(), "sc-row");
        parRow.Add(L("Par", "display sc-name"));
        var strokeRow = With(new VisualElement(), "sc-row");
        strokeRow.Add(L("Strokes", "display sc-name"));
        VisualElement Cell(string text, string extra)
        {
            var cell = With(new FacetPanel(), "sc-cell " + extra);
            cell.Add(L(text, "display sc-val"));
            return cell;
        }
        for (int i = 0; i < par.Count; i++)
        {
            parTotal += par[i];
            parRow.Add(Cell($"{par[i]}", "sc-cell--par-row"));
            int s = i < strokes.Count ? strokes[i] : 0;
            if (s <= 0) { complete = false; strokeRow.Add(Cell("–", "")); continue; }
            strokeTotal += s;
            parPlayed += par[i];
            strokeRow.Add(Cell($"{s}", s < par[i] ? "sc-cell--under" : s > par[i] ? "sc-cell--over" : ""));
        }
        parRow.Add(Cell($"{parTotal}", "sc-cell--par-row sc-cell--total"));
        int diff = strokeTotal - parPlayed;
        strokeRow.Add(Cell($"{strokeTotal}", "sc-cell--total " + (diff < 0 ? "sc-cell--under" : diff > 0 ? "sc-cell--over" : "")));
        table.Add(parRow);
        table.Add(strokeRow);
        box.Add(table);

        var summary = With(new VisualElement(), "sc-summary");
        summary.Add(L(diff == 0 ? "Even" : diff > 0 ? $"+{diff}" : $"{diff}", "display sc-big"));
        int played = 0;
        foreach (var n in strokes) if (n > 0) played++;
        summary.Add(L(complete ? "TO PAR" : $"TO PAR  ·  {played} OF {par.Count} HOLES", "eyebrow sc-big-label"));
        box.Add(summary);
        if (foot != null) box.Add(Foot(foot));
        layer.Add(box);
    }
}
