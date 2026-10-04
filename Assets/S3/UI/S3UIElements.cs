using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The HUD's colours, for elements drawn in code. The same values are the USS variables in Hud.uss;
/// painted elements start from these and take overrides from their custom style properties.
/// </summary>
public static class S3Palette
{
    public static readonly Color Ink = Hex(0x1B2033), InkHi = Hex(0x2A3150), InkLo = Hex(0x0E1220);
    public static readonly Color Paper = Hex(0xFFF3D6), PaperLo = Hex(0xE6CF9C);
    public static readonly Color Gold = Hex(0xFFC23D), GoldHi = Hex(0xFFD86E), GoldLo = Hex(0xC98A12);
    public static readonly Color Ice = Hex(0x8FD6FF), Gem = Hex(0xC49BFF), GemHi = Hex(0xEADBFF);
    public static readonly Color Flag = Hex(0xE5383B), FlagHi = Hex(0xFF5A5C);

    public static Color Hex(int rgb, float a = 1) =>
        new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
}

/// <summary>Path helpers shared by the painted elements.</summary>
static class S3Paint
{
    public static void Poly(Painter2D p, Color color, params Vector2[] pts)
    {
        if (color.a <= 0) return;
        p.fillColor = color;
        p.BeginPath();
        p.MoveTo(pts[0]);
        for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
        p.ClosePath();
        p.Fill();
    }

    /// <summary>A rectangle with its four corners cut off at 45°.</summary>
    public static Vector2[] Chamfer(float x, float y, float w, float h, float c)
    {
        c = Mathf.Min(c, w / 2, h / 2);
        return new[]
        {
            new Vector2(x + c, y), new Vector2(x + w - c, y), new Vector2(x + w, y + c), new Vector2(x + w, y + h - c),
            new Vector2(x + w - c, y + h), new Vector2(x + c, y + h), new Vector2(x, y + h - c), new Vector2(x, y + c),
        };
    }

    /// <summary>The lit top facet of a chamfered block: everything above a diagonal from (0, 0.66h) to (w, 0.22h).</summary>
    public static Vector2[] TopFacet(float w, float h, float c)
    {
        c = Mathf.Min(c, w / 2, h / 2);
        float yl = Mathf.Max(c, h * 0.66f), yr = Mathf.Max(c, h * 0.22f);
        return new[] { new Vector2(c, 0), new Vector2(w - c, 0), new Vector2(w, c), new Vector2(w, yr), new Vector2(0, yl), new Vector2(0, c) };
    }

    public static Vector2[] Offset(Vector2[] pts, float dy)
    {
        var o = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) o[i] = pts[i] + new Vector2(0, dy);
        return o;
    }

    public static void Circle(Painter2D p, Vector2 c, float r, Color fill)
    {
        p.fillColor = fill;
        p.BeginPath();
        p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360));
        p.ClosePath();
        p.Fill();
    }
}

/// <summary>Base for painted elements: reads a fill / light / depth colour set from USS custom properties.</summary>
public abstract class S3PaintedElement : VisualElement
{
    static readonly CustomStyleProperty<Color> FillProp = new CustomStyleProperty<Color>("--fill");
    static readonly CustomStyleProperty<Color> LightProp = new CustomStyleProperty<Color>("--light");
    static readonly CustomStyleProperty<Color> DepthColorProp = new CustomStyleProperty<Color>("--depth-color");
    static readonly CustomStyleProperty<Color> StrokeProp = new CustomStyleProperty<Color>("--stroke");
    static readonly CustomStyleProperty<float> CutProp = new CustomStyleProperty<float>("--cut");
    static readonly CustomStyleProperty<float> DepthProp = new CustomStyleProperty<float>("--depth");

    protected Color fill = S3Palette.Ink, light = S3Palette.InkHi, depthColor = S3Palette.InkLo, stroke = S3Palette.Paper;
    protected float cut = 12, depth = 5;

    protected S3PaintedElement()
    {
        generateVisualContent += ctx =>
        {
            float w = layout.width, h = layout.height;
            if (w >= 1 && h >= 1) Paint(ctx.painter2D, w, h);
        };
        RegisterCallback<CustomStyleResolvedEvent>(e =>
        {
            var s = e.customStyle;
            if (s.TryGetValue(FillProp, out var c)) fill = c;
            if (s.TryGetValue(LightProp, out c)) light = c;
            if (s.TryGetValue(DepthColorProp, out c)) depthColor = c;
            if (s.TryGetValue(StrokeProp, out c)) stroke = c;
            if (s.TryGetValue(CutProp, out var f)) cut = f;
            if (s.TryGetValue(DepthProp, out f)) depth = f;
            MarkDirtyRepaint();
        });
    }

    protected abstract void Paint(Painter2D p, float w, float h);
}

/// <summary>The HUD's basic block: a chamfered card with a lit top facet and a hard extruded edge under it.</summary>
[UxmlElement]
public partial class FacetPanel : S3PaintedElement
{
    protected override void Paint(Painter2D p, float w, float h)
    {
        var shape = S3Paint.Chamfer(0, 0, w, h, cut);
        if (depth > 0) S3Paint.Poly(p, depthColor, S3Paint.Offset(shape, depth));
        S3Paint.Poly(p, fill, shape);
        S3Paint.Poly(p, light, S3Paint.TopFacet(w, h, cut));
    }
}

/// <summary>The scorecard: a pennant on a pole, notched on the fly end.</summary>
[UxmlElement]
public partial class Pennant : S3PaintedElement
{
    const float Pole = 10, Notch = 34;

    protected override void Paint(Painter2D p, float w, float h)
    {
        float x = Pole - 2;
        Vector2[] flag = { new Vector2(x, 0), new Vector2(w, 0), new Vector2(w - Notch, h / 2), new Vector2(w, h), new Vector2(x, h) };
        S3Paint.Poly(p, depthColor, S3Paint.Offset(flag, depth));
        S3Paint.Poly(p, fill, flag);
        float yl = h * 0.62f, yr = h * 0.2f;
        S3Paint.Poly(p, light, new Vector2(x, 0), new Vector2(w, 0), new Vector2(w - Notch * yr / (h / 2), yr), new Vector2(x, yl));
        // the pole, lit on its left half, with a gold ball on top
        S3Paint.Poly(p, depthColor, new Vector2(0, -8 + depth), new Vector2(Pole, -8 + depth), new Vector2(Pole, h + depth + 10), new Vector2(0, h + depth + 10));
        S3Paint.Poly(p, S3Palette.PaperLo, new Vector2(0, -8), new Vector2(Pole, -8), new Vector2(Pole, h + 10), new Vector2(0, h + 10));
        S3Paint.Poly(p, S3Palette.Paper, new Vector2(0, -8), new Vector2(Pole / 2, -8), new Vector2(Pole / 2, h + 10), new Vector2(0, h + 10));
        S3Paint.Circle(p, new Vector2(Pole / 2, -10), 8, S3Palette.GoldLo);
        S3Paint.Circle(p, new Vector2(Pole / 2 - 1.5f, -11.5f), 6, S3Palette.Gold);
    }
}

/// <summary>The centre banner: a ribbon with notched tails.</summary>
[UxmlElement]
public partial class Ribbon : S3PaintedElement
{
    protected override void Paint(Painter2D p, float w, float h)
    {
        float e = Mathf.Min(30, h * 0.4f);
        Vector2[] band = { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w - e, h / 2), new Vector2(w, h), new Vector2(0, h), new Vector2(e, h / 2) };
        S3Paint.Poly(p, depthColor, S3Paint.Offset(band, depth));
        S3Paint.Poly(p, fill, band);
        S3Paint.Poly(p, light, new Vector2(0, 0), new Vector2(w, 0), new Vector2(w - e * 0.9f, h * 0.45f), new Vector2(e * 0.9f, h * 0.45f));
    }
}

public enum S3Shape { Diamond, Flag, Gem, Ring, Ball, Mirror, Diffractor, Crosshair, Chevron }

/// <summary>A small vector icon: pips, waypoint icons, inventory pieces, the crosshair.</summary>
[UxmlElement]
public partial class ShapeIcon : S3PaintedElement
{
    S3Shape shape;

    [UxmlAttribute]
    public S3Shape Shape { get => shape; set { shape = value; MarkDirtyRepaint(); } }

    public ShapeIcon() { fill = S3Palette.Paper; light = S3Palette.Paper; depthColor = S3Palette.InkLo; }
    public ShapeIcon(S3Shape s) : this() { shape = s; }

    protected override void Paint(Painter2D p, float w, float h)
    {
        Vector2 c = new Vector2(w / 2, h / 2);
        float r = Mathf.Min(w, h) / 2;
        switch (shape)
        {
            case S3Shape.Diamond:
                if (depth > 0) S3Paint.Poly(p, depthColor, Kite(c + new Vector2(0, depth), r, r));
                S3Paint.Poly(p, fill, Kite(c, r, r));
                S3Paint.Poly(p, light, new Vector2(c.x, c.y - r), new Vector2(c.x + r, c.y), c, new Vector2(c.x - r, c.y));
                break;
            case S3Shape.Gem:
            case S3Shape.Diffractor:
            {
                float rx = shape == S3Shape.Gem ? r * 0.78f : r, top = shape == S3Shape.Gem ? 0.25f : 0f;
                Vector2 t = new Vector2(c.x, c.y - r), b = new Vector2(c.x, c.y + r), mid = new Vector2(0, c.y - r * top);
                Vector2 lft = new Vector2(c.x - rx, mid.y), rgt = new Vector2(c.x + rx, mid.y);
                if (depth > 0) S3Paint.Poly(p, depthColor, t + Vector2.up * depth, rgt + Vector2.up * depth, b + Vector2.up * depth, lft + Vector2.up * depth);
                S3Paint.Poly(p, fill, t, rgt, b, lft);
                S3Paint.Poly(p, light, t, c + new Vector2(0, r * 0.15f), b, lft);
                break;
            }
            case S3Shape.Flag:
            {
                float pw = Mathf.Max(3, w * 0.12f), x0 = c.x - r * 0.55f;
                S3Paint.Poly(p, depthColor, new Vector2(x0 - 1, c.y - r - 1), new Vector2(x0 + pw + 1, c.y - r - 1), new Vector2(x0 + pw + 1, c.y + r + 1), new Vector2(x0 - 1, c.y + r + 1));
                S3Paint.Poly(p, stroke, new Vector2(x0, c.y - r), new Vector2(x0 + pw, c.y - r), new Vector2(x0 + pw, c.y + r), new Vector2(x0, c.y + r));
                Vector2 a = new Vector2(x0 + pw, c.y - r), tip = new Vector2(c.x + r, c.y - r * 0.35f), d = new Vector2(x0 + pw, c.y + r * 0.3f);
                S3Paint.Poly(p, depthColor, a + new Vector2(0, 3), tip + new Vector2(0, 3), d + new Vector2(0, 3));
                S3Paint.Poly(p, fill, a, tip, d);
                S3Paint.Poly(p, light, a, tip, new Vector2(x0 + pw, c.y - r * 0.35f));
                break;
            }
            case S3Shape.Ring:
            {
                float lw = Mathf.Max(3, r * 0.3f);
                p.lineWidth = lw + 4;
                p.strokeColor = depthColor;
                p.BeginPath(); p.Arc(c, r - lw / 2 - 1, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
                p.lineWidth = lw;
                p.strokeColor = fill;
                p.BeginPath(); p.Arc(c, r - lw / 2 - 1, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
                break;
            }
            case S3Shape.Ball:
                S3Paint.Circle(p, c, r, depthColor);
                S3Paint.Circle(p, c, r - 2.5f, S3Palette.PaperLo);
                S3Paint.Circle(p, c - new Vector2(r * 0.12f, r * 0.12f), r * 0.72f, fill);
                break;
            case S3Shape.Mirror:
            {
                float s = w * 0.22f;
                Vector2 a = new Vector2(s, 0), b = new Vector2(w, 0), d = new Vector2(w - s, h), e = new Vector2(0, h);
                S3Paint.Poly(p, fill, a, b, d, e);
                S3Paint.Poly(p, light, a, new Vector2(w * 0.62f, 0), new Vector2(w * 0.3f, h), e);
                break;
            }
            case S3Shape.Crosshair:
            {
                p.lineWidth = 5;
                p.strokeColor = depthColor;
                Outline(p, Kite(c, r - 3, r - 3));
                p.lineWidth = 2.5f;
                p.strokeColor = stroke;
                Outline(p, Kite(c, r - 3, r - 3));
                S3Paint.Poly(p, depthColor, Kite(c, r * 0.3f + 1.5f, r * 0.3f + 1.5f));
                S3Paint.Poly(p, fill, Kite(c, r * 0.3f, r * 0.3f));
                break;
            }
            case S3Shape.Chevron:
                S3Paint.Poly(p, depthColor, new Vector2(c.x, 0), new Vector2(w, h), new Vector2(0, h));
                S3Paint.Poly(p, fill, new Vector2(c.x, 3), new Vector2(w - 4, h - 2), new Vector2(c.x, h * 0.72f), new Vector2(4, h - 2));
                break;
        }
    }

    static Vector2[] Kite(Vector2 c, float rx, float ry) =>
        new[] { new Vector2(c.x, c.y - ry), new Vector2(c.x + rx, c.y), new Vector2(c.x, c.y + ry), new Vector2(c.x - rx, c.y) };

    static void Outline(Painter2D p, Vector2[] pts)
    {
        p.lineJoin = LineJoin.Miter;
        p.BeginPath();
        p.MoveTo(pts[0]);
        for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
        p.ClosePath();
        p.Stroke();
    }
}

/// <summary>The power meter: skewed segments from green through gold to red, with optional markers.</summary>
[UxmlElement]
public partial class SegmentBar : S3PaintedElement
{
    const int N = 20;
    const float Gap = 4;
    float value, poleMark = -1, ghost = -1;

    public float Value { get => value; set { if (this.value != value) { this.value = value; MarkDirtyRepaint(); } } }
    /// <summary>Where a straight-up shot just crosses the pole (0..1), or negative for none.</summary>
    public float PoleMark { get => poleMark; set { if (poleMark != value) { poleMark = value; MarkDirtyRepaint(); } } }
    /// <summary>The last shot's power (0..1), or negative for none.</summary>
    public float Ghost { get => ghost; set { if (ghost != value) { ghost = value; MarkDirtyRepaint(); } } }

    /// <summary>Segment colour along the meter: green, gold, red.</summary>
    public static Color Ramp(float t) =>
        t < 0.5f ? Color.Lerp(S3Palette.Hex(0x5BD158), S3Palette.Gold, t * 2) : Color.Lerp(S3Palette.Gold, S3Palette.Hex(0xFF4A3D), t * 2 - 1);

    protected override void Paint(Painter2D p, float w, float h)
    {
        float skew = h * 0.36f, sw = (w - skew - Gap * (N - 1)) / N;
        Color off = new Color(S3Palette.Paper.r, S3Palette.Paper.g, S3Palette.Paper.b, 0.13f);
        for (int i = 0; i < N; i++)
        {
            float x = i * (sw + Gap);
            bool lit = (i + 0.5f) / N <= value;
            Color col = lit ? Ramp(i / (N - 1f)) : off;
            S3Paint.Poly(p, col, new Vector2(x + skew, 0), new Vector2(x + skew + sw, 0), new Vector2(x + sw, h), new Vector2(x, h));
            if (lit) S3Paint.Poly(p, new Color(1, 1, 1, 0.25f), new Vector2(x + skew, 0), new Vector2(x + skew + sw, 0), new Vector2(x + skew * 0.6f + sw, h * 0.4f), new Vector2(x + skew * 0.6f, h * 0.4f));
        }
        if (ghost >= 0)
        {
            float gx = ghost * (w - skew) + skew * 0.5f;
            S3Paint.Poly(p, new Color(1, 1, 1, 0.7f), new Vector2(gx - 1.5f, -4), new Vector2(gx + 1.5f, -4), new Vector2(gx + 1.5f, h + 4), new Vector2(gx - 1.5f, h + 4));
        }
        if (poleMark >= 0 && poleMark <= 1)
        {
            float px = poleMark * (w - skew) + skew * 0.5f;
            S3Paint.Poly(p, S3Palette.InkLo, new Vector2(px - 4, -10), new Vector2(px + 4, -10), new Vector2(px + 4, h + 12), new Vector2(px - 4, h + 12));
            S3Paint.Poly(p, S3Palette.Ice, new Vector2(px - 2, -8), new Vector2(px + 2, -8), new Vector2(px + 2, h + 10), new Vector2(px - 2, h + 10));
        }
    }
}

/// <summary>A quarter-circle gauge with a needle: the launch angle (0..90°).</summary>
[UxmlElement]
public partial class AngleGauge : S3PaintedElement
{
    float degrees = 24;

    public float Degrees { get => degrees; set { if (degrees != value) { degrees = value; MarkDirtyRepaint(); } } }

    protected override void Paint(Painter2D p, float w, float h)
    {
        float r = Mathf.Min(w, h) - 6;
        Vector2 o = new Vector2(4, h - 4);
        p.lineCap = LineCap.Round;
        p.lineWidth = 5;
        p.strokeColor = new Color(S3Palette.Paper.r, S3Palette.Paper.g, S3Palette.Paper.b, 0.18f);
        p.BeginPath(); p.Arc(o, r, Angle.Degrees(-90), Angle.Degrees(0)); p.Stroke();
        p.strokeColor = S3Palette.Gold;
        p.BeginPath(); p.Arc(o, r, Angle.Degrees(-degrees), Angle.Degrees(0)); p.Stroke();
        float a = degrees * Mathf.Deg2Rad;
        p.lineWidth = 4;
        p.strokeColor = S3Palette.Paper;
        p.BeginPath(); p.MoveTo(o); p.LineTo(o + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * (r - 6)); p.Stroke();
        S3Paint.Circle(p, o, 4, S3Palette.Paper);
    }
}
