using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The look of the heads-up display, drawn with IMGUI: Roboto, rounded translucent panels, key caps for
/// controls, and icons drawn as textures (the font has no symbol glyphs). Everything is laid out on a
/// 1080-line virtual screen (see Begin), so the HUD keeps its proportions at any resolution.
/// </summary>
public static class S3Hud
{
    public static readonly Color Ink = new Color(0.97f, 0.97f, 0.95f);
    public static readonly Color Dim = new Color(0.97f, 0.97f, 0.95f, 0.62f);
    public static readonly Color Accent = new Color(1f, 0.80f, 0.32f);
    public static readonly Color Ice = new Color(0.62f, 0.86f, 1f);
    static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.12f, 0.58f);
    static readonly Color Shade = new Color(0, 0, 0, 0.45f);

    public static GUIStyle Title, Sub, Heading, Body, BodyBold, Small, Eyebrow, KeyText, Wrap;
    public static Texture2D Circle, Ring, Diamond, Triangle;
    static GUIStyle panel, pill, tag, bar, keyCap, button, buttonHover, buttonCurrent;
    static Font regular, medium, bold, black;

    /// <summary>Virtual pixels per screen pixel is 1 / Scale.</summary>
    public static float Scale { get; private set; } = 1;
    public static float Width => Screen.width / Scale;
    public static float Height => Screen.height / Scale;

    /// <summary>Call at the top of OnGUI: builds the styles once and scales the HUD to the screen.</summary>
    public static void Begin()
    {
        if (Title == null) Build();
        Scale = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2.5f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1));
    }

    static void Build()
    {
        regular = Resources.Load<Font>("Fonts/Roboto-Regular");
        medium = Resources.Load<Font>("Fonts/Roboto-Medium");
        bold = Resources.Load<Font>("Fonts/Roboto-Bold");
        black = Resources.Load<Font>("Fonts/Roboto-Black");
        GUIStyle Text(Font f, int size, TextAnchor anchor = TextAnchor.MiddleLeft) => new GUIStyle(GUI.skin.label)
        {
            font = f, fontSize = size, alignment = anchor, wordWrap = false, richText = false,
            padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), normal = { textColor = Color.white },
        };
        Title = Text(black, 46, TextAnchor.MiddleCenter);
        Sub = Text(medium, 20, TextAnchor.MiddleCenter);
        Heading = Text(black, 28);
        Body = Text(medium, 17);
        BodyBold = Text(bold, 17);
        Small = Text(medium, 14);
        Eyebrow = Text(bold, 12);
        KeyText = Text(bold, 13, TextAnchor.MiddleCenter);
        Wrap = new GUIStyle(Text(regular, 16, TextAnchor.UpperLeft)) { wordWrap = true };

        var white = Color.white;
        panel = Sliced(Rounded(48, 14, white), 14);
        pill = Sliced(Rounded(40, 20, white), 19);
        tag = Sliced(Rounded(24, 8, white), 8);
        bar = Sliced(Rounded(16, 8, white), 7);
        keyCap = Sliced(Rounded(24, 6, white), 6);
        button = Sliced(Rounded(32, 10, new Color(1, 1, 1, 0.09f)), 10);
        buttonHover = Sliced(Rounded(32, 10, new Color(1, 1, 1, 0.2f)), 10);
        buttonCurrent = Sliced(Rounded(32, 10, Accent), 10);
        foreach (var s in new[] { button, buttonCurrent })
        {
            s.font = medium; s.fontSize = 16; s.alignment = TextAnchor.MiddleLeft; s.padding = new RectOffset(16, 10, 0, 0);
        }
        button.normal.textColor = button.hover.textColor = button.active.textColor = Ink;
        button.hover.background = buttonHover.normal.background;
        button.active.background = buttonCurrent.normal.background;
        button.active.textColor = new Color(0.1f, 0.08f, 0.04f);
        buttonCurrent.hover.background = buttonCurrent.active.background = buttonCurrent.normal.background;
        buttonCurrent.normal.textColor = buttonCurrent.hover.textColor = buttonCurrent.active.textColor = new Color(0.12f, 0.09f, 0.03f);
        buttonCurrent.font = bold;

        Circle = Shape(64, (x, y) => 30 - Mathf.Sqrt(x * x + y * y));
        Ring = Shape(64, (x, y) => 5 - Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 24));
        Diamond = Shape(64, (x, y) => (30 - (Mathf.Abs(x) + Mathf.Abs(y))) * 0.7071f);
        // a downward-pointing triangle: flat side on top at y = 20, apex at (0, -26)
        Triangle = Shape(64, (x, y) => Mathf.Min(20 - y, Mathf.Min(0.870f * x + 0.492f * y + 12.8f, -0.870f * x + 0.492f * y + 12.8f)));
    }

    static GUIStyle Sliced(Texture2D tex, int border) => new GUIStyle
    {
        normal = { background = tex }, border = new RectOffset(border, border, border, border),
    };

    /// <summary>A white rounded square of the given colour, antialiased, for 9-slicing.</summary>
    static Texture2D Rounded(int size, float radius, Color color)
    {
        float half = size / 2f;
        return Make(size, (x, y) =>
        {
            float qx = Mathf.Abs(x) - (half - radius), qy = Mathf.Abs(y) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0);
            return radius - outside;
        }, color);
    }

    static Texture2D Shape(int size, System.Func<float, float, float> inside) => Make(size, inside, Color.white);

    /// <summary>Texture from a signed distance (positive inside, in pixels) around the centre.</summary>
    static Texture2D Make(int size, System.Func<float, float, float> inside, Color color)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color[size * size];
        float half = size / 2f;
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float a = Mathf.Clamp01(inside(i + 0.5f - half, j + 0.5f - half) + 0.5f);   // row 0 is the bottom
                px[j * size + i] = new Color(color.r, color.g, color.b, color.a * a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ------------------------------------------------------------------ drawing

    static void Draw(GUIStyle style, Rect r, Color tint)
    {
        if (Event.current.type != EventType.Repaint) return;
        var c = GUI.color;
        GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * c.a);
        style.Draw(r, GUIContent.none, false, false, false, false);
        GUI.color = c;
    }

    /// <summary>A dark translucent card.</summary>
    public static void Panel(Rect r, float alpha = 1) => Draw(panel, r, new Color(PanelColor.r, PanelColor.g, PanelColor.b, PanelColor.a * alpha));
    public static void Panel(Rect r, Color color) => Draw(panel, r, color);
    public static void Pill(Rect r, Color color) => Draw(pill, r, color);
    /// <summary>A thin rounded bar (meters), at most 16 high.</summary>
    public static void Bar(Rect r, Color color) => Draw(bar, r, color);

    /// <summary>A small label in a dark tag, centred on cx with its top at y (waypoint labels).</summary>
    public static void Tag(float cx, float y, string text, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        float w = Measure(text, Small).x;
        var r = new Rect(cx - w / 2 - 9, y, w + 18, 24);
        Draw(tag, r, new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.5f));
        Text(new Rect(r.x + 9, y + 3, w + 2, 18), text, Small, color, false);
    }

    /// <summary>Text centred on cx, vertically centred on cy.</summary>
    public static void Centered(float cx, float cy, string text, GUIStyle style, Color color, bool shadow = false)
    {
        var size = Measure(text, style);
        Text(new Rect(cx - size.x / 2, cy - size.y / 2, size.x + 2, size.y), text, style, color, shadow);
    }

    public static Vector2 Measure(string text, GUIStyle style) => style.CalcSize(new GUIContent(text));

    /// <summary>Text with a soft shadow, so it reads on snow as well as grass.</summary>
    public static void Text(Rect r, string text, GUIStyle style, Color color, bool shadow = true)
    {
        if (string.IsNullOrEmpty(text)) return;
        var c = GUI.color;
        if (shadow)
        {
            GUI.color = new Color(0, 0, 0, 0.35f * color.a * c.a);
            GUI.Label(new Rect(r.x + 1, r.y + 2, r.width, r.height), text, style);
        }
        GUI.color = new Color(color.r * c.r, color.g * c.g, color.b * c.b, color.a * c.a);
        GUI.Label(r, text, style);
        GUI.color = c;
    }

    public static void Icon(Rect r, Texture2D icon, Color color, bool outline = true)
    {
        if (Event.current.type != EventType.Repaint) return;
        var c = GUI.color;
        if (outline)
        {
            GUI.color = new Color(0, 0, 0, 0.5f * color.a * c.a);
            GUI.DrawTexture(new Rect(r.x - 2, r.y - 1, r.width + 4, r.height + 4), icon);
        }
        GUI.color = new Color(color.r, color.g, color.b, color.a * c.a);
        GUI.DrawTexture(r, icon);
        GUI.color = c;
    }

    /// <summary>A key cap; returns its width.</summary>
    public static float Key(float x, float cy, string key)
    {
        var size = Measure(key, KeyText);
        float w = Mathf.Max(24, size.x + 14), h = 24;
        var r = new Rect(x, cy - h / 2, w, h);
        Draw(keyCap, new Rect(r.x, r.y + 2, r.width, r.height), Shade);
        Draw(keyCap, r, new Color(0.95f, 0.95f, 0.93f, 0.95f));
        var c = GUI.color;
        GUI.color = new Color(0.1f, 0.11f, 0.14f, c.a);
        GUI.Label(r, key, KeyText);
        GUI.color = c;
        return w;
    }

    /// <summary>One control: key caps then what they do.</summary>
    public struct Hint
    {
        public string keys, action;
        public Hint(string keys, string action) { this.keys = keys; this.action = action; }
    }

    static float HintWidth(Hint h)
    {
        float w = 0;
        if (!string.IsNullOrEmpty(h.keys))
            foreach (var k in h.keys.Split(' ')) w += Mathf.Max(24, Measure(k, KeyText).x + 14) + 4;
        return w + 4 + Measure(h.action, Body).x;
    }

    /// <summary>A row of controls in a pill, centred on cx.</summary>
    public static void Hints(float cx, float cy, IList<Hint> hints)
    {
        if (hints == null || hints.Count == 0) return;
        const float gap = 26, pad = 18;
        float total = -gap;
        foreach (var h in hints) total += HintWidth(h) + gap;
        var r = new Rect(cx - total / 2 - pad, cy - 21, total + 2 * pad, 42);
        Pill(r, PanelColor);
        float x = r.x + pad;
        foreach (var h in hints)
        {
            if (!string.IsNullOrEmpty(h.keys))
                foreach (var k in h.keys.Split(' ')) x += Key(x, cy, k) + 4;
            x += 4;
            float w = Measure(h.action, Body).x;
            Text(new Rect(x, cy - 12, w + 2, 24), h.action, Body, Ink, false);
            x += w + gap;
        }
    }

    /// <summary>A line of text in a pill, centred on cx.</summary>
    public static void Note(float cx, float cy, string text, Color? color = null, GUIStyle style = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        style ??= Body;
        float w = Measure(text, style).x;
        var r = new Rect(cx - w / 2 - 18, cy - 19, w + 36, 38);
        Pill(r, PanelColor);
        Text(new Rect(r.x + 18, cy - 12, w + 2, 24), text, style, color ?? Ink, false);
    }

    /// <summary>A level-select style button.</summary>
    public static bool Button(Rect r, string label, bool current)
    {
        var style = current ? buttonCurrent : button;
        float fit = style.font != null ? Measure(label, style).x + style.padding.horizontal : 0;
        int size = style.fontSize;
        if (fit > r.width) style.fontSize = Mathf.Max(11, Mathf.FloorToInt(size * r.width / fit));
        bool clicked = GUI.Button(r, label, style);
        style.fontSize = size;
        return clicked;
    }
}
