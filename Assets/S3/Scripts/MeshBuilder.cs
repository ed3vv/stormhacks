using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Collects flat-shaded, vertex-coloured triangles in local Euclidean metre coordinates.
/// Faces are pre-tessellated to <see cref="maxEdge"/> so the exponential map onto S^3
/// bends them smoothly instead of leaving long straight chords.
/// </summary>
public class MeshBuilder
{
    public readonly List<Vector3> verts = new List<Vector3>();
    public readonly List<Vector3> normals = new List<Vector3>();
    public readonly List<Color> colors = new List<Color>();
    public readonly List<int> tris = new List<int>();
    public readonly List<Vector4> props = new List<Vector4>();      // uv1: prop's foot on S^3 (wind phase)
    public readonly List<Vector4> propInfo = new List<Vector4>();   // uv2: (see-through index, wind sway, 0, 0)
    public float maxEdge;

    public MeshBuilder(float maxEdge = 0.5f) { this.maxEdge = maxEdge; }

    int Add(Vector3 p, Vector3 n, Color c)
    {
        verts.Add(p); normals.Add(n); colors.Add(c);
        props.Add(Vector4.zero); propInfo.Add(Vector4.zero);
        return verts.Count - 1;
    }

    static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        return n.sqrMagnitude > 1e-14f ? n.normalized : Vector3.up;
    }

    int Segments(float length) => Mathf.Max(1, Mathf.CeilToInt(length / maxEdge - 1e-4f));

    // ------------------------------------------------------------------ primitives

    /// <summary>Quad a-b-c-d (in order around the edge), tessellated as a grid.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        Vector3 n = FaceNormal(a, b, d);
        int nu = Segments(Mathf.Max(Vector3.Distance(a, b), Vector3.Distance(d, c)));
        int nv = Segments(Mathf.Max(Vector3.Distance(a, d), Vector3.Distance(b, c)));
        int start = verts.Count;
        for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++)
            {
                float s = (float)i / nu, t = (float)j / nv;
                Add(Vector3.Lerp(Vector3.Lerp(a, b, s), Vector3.Lerp(d, c, s), t), n, col);
            }
        for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int k = start + j * (nu + 1) + i;
                tris.Add(k); tris.Add(k + nu + 1); tris.Add(k + 1);
                tris.Add(k + 1); tris.Add(k + nu + 1); tris.Add(k + nu + 2);
            }
    }

    /// <summary>Triangle, tessellated barycentrically.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        Vector3 n = FaceNormal(a, b, c);
        int k = Segments(Mathf.Max(Vector3.Distance(a, b), Mathf.Max(Vector3.Distance(b, c), Vector3.Distance(c, a))));
        var idx = new int[k + 1, k + 1];
        for (int i = 0; i <= k; i++)
            for (int j = 0; j <= k - i; j++)
                idx[i, j] = Add(a + (b - a) * ((float)i / k) + (c - a) * ((float)j / k), n, col);
        for (int i = 0; i < k; i++)
            for (int j = 0; j < k - i; j++)
            {
                tris.Add(idx[i, j]); tris.Add(idx[i + 1, j]); tris.Add(idx[i, j + 1]);
                if (i + j < k - 1) { tris.Add(idx[i + 1, j]); tris.Add(idx[i + 1, j + 1]); tris.Add(idx[i, j + 1]); }
            }
    }

    /// <summary>Axis-aligned box.</summary>
    public void Box(Vector3 min, Vector3 max, Color col)
    {
        Vector3 P(int x, int y, int z) => new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
        Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), col); // -z
        Quad(P(1, 0, 1), P(0, 0, 1), P(0, 1, 1), P(1, 1, 1), col); // +z
        Quad(P(0, 0, 1), P(0, 0, 0), P(0, 1, 0), P(0, 1, 1), col); // -x
        Quad(P(1, 0, 0), P(1, 0, 1), P(1, 1, 1), P(1, 1, 0), col); // +x
        Quad(P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), col); // top
        Quad(P(0, 0, 1), P(1, 0, 1), P(1, 0, 0), P(0, 0, 0), col); // bottom
    }

    /// <summary>Box with arbitrary orthonormal axes r, u, f around centre c, with half sizes along each.</summary>
    public void OrientedBox(Vector3 c, Vector3 r, Vector3 u, Vector3 f, Vector3 half, Color col)
    {
        Vector3 P(int i, int j, int k) => c + r * (i == 0 ? -half.x : half.x) + u * (j == 0 ? -half.y : half.y) + f * (k == 0 ? -half.z : half.z);
        Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), col);
        Quad(P(1, 0, 1), P(0, 0, 1), P(0, 1, 1), P(1, 1, 1), col);
        Quad(P(0, 0, 1), P(0, 0, 0), P(0, 1, 0), P(0, 1, 1), col);
        Quad(P(1, 0, 0), P(1, 0, 1), P(1, 1, 1), P(1, 1, 0), col);
        Quad(P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), col);
        Quad(P(0, 0, 1), P(1, 0, 1), P(1, 0, 0), P(0, 0, 0), col);
    }

    /// <summary>Triangle a-b-c extruded by depth into a closed prism (gables, wedges).</summary>
    public void Prism(Vector3 a, Vector3 b, Vector3 c, Vector3 depth, Color col)
    {
        Tri(a, b, c, col);
        Tri(a + depth, b + depth, c + depth, col);
        Quad(a, b, b + depth, a + depth, col);
        Quad(b, c, c + depth, b + depth, col);
        Quad(c, a, a + depth, c + depth, col);
    }

    /// <summary>A thick plate: quad a-b-c-d extruded by thickness along its upward-facing normal.</summary>
    public void Slab(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float thickness, Color col, Color edge)
    {
        Vector3 n = FaceNormal(a, b, d);
        if (n.y < 0) n = -n;
        Vector3 o = n * thickness;
        Quad(a, b, c, d, col);
        Quad(a + o, b + o, c + o, d + o, col);
        Quad(a, b, b + o, a + o, edge);
        Quad(b, c, c + o, b + o, edge);
        Quad(c, d, d + o, c + o, edge);
        Quad(d, a, a + o, d + o, edge);
    }

    /// <summary>Vertical frustum (cylinder, or cone when topRadius = 0) standing on baseCenter.</summary>
    public void Frustum(Vector3 baseCenter, float baseRadius, float topRadius, float height, int sides, Color col, float phase = 0)
    {
        Vector3 top = baseCenter + Vector3.up * height;
        for (int i = 0; i < sides; i++)
        {
            float a0 = phase + 2 * Mathf.PI * i / sides, a1 = phase + 2 * Mathf.PI * (i + 1) / sides;
            var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            if (topRadius > 1e-4f)
            {
                Quad(baseCenter + d0 * baseRadius, baseCenter + d1 * baseRadius, top + d1 * topRadius, top + d0 * topRadius, col);
                Tri(top + d0 * topRadius, top + d1 * topRadius, top, col);
            }
            else Tri(baseCenter + d0 * baseRadius, baseCenter + d1 * baseRadius, top, col);
            Tri(baseCenter + d1 * baseRadius, baseCenter + d0 * baseRadius, baseCenter, col);
        }
    }

    /// <summary>Tapered tube between two arbitrary points (palm trunks, beams), capped at the top.</summary>
    public void Tube(Vector3 a, Vector3 b, float ra, float rb, int sides, Color col, float phase = 0)
    {
        Vector3 axis = (b - a).normalized;
        Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 side2 = Vector3.Cross(axis, side);
        for (int i = 0; i < sides; i++)
        {
            float a0 = phase + 2 * Mathf.PI * i / sides, a1 = phase + 2 * Mathf.PI * (i + 1) / sides;
            Vector3 d0 = side * Mathf.Cos(a0) + side2 * Mathf.Sin(a0);
            Vector3 d1 = side * Mathf.Cos(a1) + side2 * Mathf.Sin(a1);
            Quad(a + d0 * ra, a + d1 * ra, b + d1 * rb, b + d0 * rb, col);
            Tri(b + d0 * rb, b + d1 * rb, b, col);
        }
    }

    /// <summary>
    /// Flat arched opening (door / window) on a wall: a rectangle with a semicircular top, drawn
    /// slightly in front of the wall. bottom is the centre of the sill; right and outward span the wall.
    /// </summary>
    public void Arch(Vector3 bottom, Vector3 right, Vector3 outward, float width, float height, Color col, float offset = 0.03f)
    {
        Vector3 up = Vector3.Cross(outward, right).normalized;
        if (Vector3.Dot(up, Vector3.up) < 0) up = -up;
        float hw = width / 2, rect = Mathf.Max(height - hw, 0.01f);
        Vector3 o = bottom + outward * offset;
        Quad(o - right * hw, o + right * hw, o + right * hw + up * rect, o - right * hw + up * rect, col);
        Vector3 c = o + up * rect;
        const int seg = 8;
        for (int i = 0; i < seg; i++)
        {
            float a0 = Mathf.PI * i / seg, a1 = Mathf.PI * (i + 1) / seg;
            Tri(c, c + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * hw, c + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * hw, col);
        }
    }

    /// <summary>Low-poly ellipsoid (subdivided icosahedron) with optional radial jitter.</summary>
    public void Icosphere(Vector3 center, Vector3 radii, int subdivisions, Color col,
                          System.Random rng = null, float jitter = 0, Func<Vector3, Color> faceColor = null)
    {
        float t = (1 + Mathf.Sqrt(5)) / 2;
        var pts = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < pts.Count; i++) pts[i] = pts[i].normalized;
        var faces = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int m)) return m;
                pts.Add(((pts[a] + pts[b]) * 0.5f).normalized);
                return mid[key] = pts.Count - 1;
            }
            var next = new List<int>();
            for (int f = 0; f < faces.Count; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            faces = next;
        }
        var scaled = new Vector3[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            float k = 1 + (rng != null ? jitter * (float)(rng.NextDouble() * 2 - 1) : 0);
            scaled[i] = center + Vector3.Scale(pts[i] * k, radii);
        }
        for (int f = 0; f < faces.Count; f += 3)
        {
            Vector3 a = scaled[faces[f]], b = scaled[faces[f + 1]], c = scaled[faces[f + 2]];
            Color fc = faceColor != null ? faceColor((pts[faces[f]] + pts[faces[f + 1]] + pts[faces[f + 2]]) / 3) : col;
            Tri(a, b, c, fc);
        }
    }

    /// <summary>
    /// The whole ground great-sphere as a polar disk of radius pi*R in the tangent plane at e_w.
    /// Under the exp map the rim collapses onto the antipode, so the disk covers the sphere exactly.
    /// </summary>
    public void GroundDisk(float R, int rings, int segments, Color col)
    {
        int center = Add(Vector3.zero, Vector3.up, col);
        for (int k = 1; k <= rings; k++)
        {
            float r = Mathf.PI * R * k / rings;
            for (int i = 0; i < segments; i++)
            {
                float a = 2 * Mathf.PI * i / segments;
                Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), Vector3.up, col);
            }
        }
        int Ring(int k, int i) => k == 0 ? center : center + 1 + (k - 1) * segments + (i % segments);
        for (int i = 0; i < segments; i++) { tris.Add(center); tris.Add(Ring(1, i + 1)); tris.Add(Ring(1, i)); }
        for (int k = 1; k < rings; k++)
            for (int i = 0; i < segments; i++)
            {
                tris.Add(Ring(k, i)); tris.Add(Ring(k, i + 1)); tris.Add(Ring(k + 1, i));
                tris.Add(Ring(k, i + 1)); tris.Add(Ring(k + 1, i + 1)); tris.Add(Ring(k + 1, i));
            }
    }

    /// <summary>A small quad given directly by four points of S^3, stored in log-map coordinates (frame = identity).</summary>
    public void QuadS3(Vector4 a, Vector4 b, Vector4 c, Vector4 d, Color col, float R)
    {
        int start = verts.Count;
        foreach (var p in new[] { a, b, c, d })
        {
            S3.LogWithNormal(p, S3.UpAt(p), R, out Vector3 v, out Vector3 n);
            Add(v, n, col);
        }
        tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
        tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
    }

    // ------------------------------------------------------------------ S^3 baking

    /// <summary>
    /// Places src onto S^3 at the given frame and appends it re-encoded as log-map coordinates
    /// around e_w, so many props can share one mesh drawn with the identity frame.
    /// </summary>
    /// <param name="fadeIndex">see-through slot from S3World.RegisterFadeProp (0 = never fades)</param>
    /// <param name="sway">how much wind moves it (grass 1, foliage ~0.3, solid 0)</param>
    /// <param name="occlusion">darken vertices near the ground (cheap baked ambient occlusion)</param>
    public void AppendBaked(MeshBuilder src, Matrix4x4 frame, float R, int fadeIndex = 0, float sway = 0, float occlusion = 0.35f)
    {
        int offset = verts.Count;
        Vector4 foot = frame.GetColumn(3);
        var info = new Vector4(fadeIndex, sway, 0, 0);
        for (int i = 0; i < src.verts.Count; i++)
        {
            Vector3 lv = src.verts[i];
            S3.ExpWithNormal(lv, src.normals[i], R, out Vector4 p, out Vector4 n4);
            S3.LogWithNormal(frame * p, frame * n4, R, out Vector3 v, out Vector3 n);
            Color c = src.colors[i];
            if (occlusion > 0)
            {
                float k = 1 - occlusion * (1 - Mathf.Clamp01(lv.y / 1.4f));
                c = new Color(c.r * k, c.g * k, c.b * k, c.a);
            }
            int idx = Add(v, n, c);
            props[idx] = foot;
            propInfo[idx] = info;
        }
        foreach (int t in src.tris) tris.Add(t + offset);
    }

    public Mesh ToMesh(string name)
    {
        var m = new Mesh { name = name };
        m.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetColors(colors);
        m.SetUVs(1, props);
        m.SetUVs(2, propInfo);
        m.SetTriangles(tris, 0, false);
        // Positions are S^3 coordinates, not Unity world space: never let Unity cull these.
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return m;
    }
}
