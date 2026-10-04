using UnityEngine;

/// <summary>
/// Math on the unit 3-sphere S^3 in R^4. The world radius R (metres) converts
/// metres to radians: a full lap of the world is 2*pi*R.
///
/// Conventions
///   * The ground is the great 2-sphere y = 0. "Up" off the ground is the +e_y side.
///   * A frame is an SO(4) matrix whose columns are (right, up, forward, position).
///     The identity frame sits at e_w looking along e_z with e_y up.
///   * Local (Euclidean, metre) coordinates around a frame are mapped onto S^3 with
///     the exponential map, so a prop authored in metres is placed by its frame alone.
/// </summary>
public static class S3
{
    public static readonly Vector4 EX = new Vector4(1, 0, 0, 0);
    public static readonly Vector4 EY = new Vector4(0, 1, 0, 0);
    public static readonly Vector4 EZ = new Vector4(0, 0, 1, 0);
    public static readonly Vector4 EW = new Vector4(0, 0, 0, 1);

    /// <summary>
    /// Rotation in the plane of orthonormal a, b by angle t:
    /// a -> a cos t + b sin t, b -> b cos t - a sin t, identity on the complement.
    /// </summary>
    public static Matrix4x4 PlaneRotation(Vector4 a, Vector4 b, float t)
    {
        float c = Mathf.Cos(t) - 1f, s = Mathf.Sin(t);
        var m = Matrix4x4.identity;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                m[i, j] += c * (a[i] * a[j] + b[i] * b[j]) + s * (b[i] * a[j] - a[i] * b[j]);
        return m;
    }

    /// <summary>Exponential map at e_w: local tangent vector in metres -> point of S^3.</summary>
    public static Vector4 Exp(Vector3 v, float R)
    {
        Vector3 u = v / R;
        float r = u.magnitude;
        if (r < 1e-7f) return EW;
        Vector3 d = u / r;
        float s = Mathf.Sin(r);
        return new Vector4(d.x * s, d.y * s, d.z * s, Mathf.Cos(r));
    }

    /// <summary>
    /// Exp map of a vertex plus parallel transport of its normal along the same geodesic.
    /// Mirrors the vertex shader exactly.
    /// </summary>
    public static void ExpWithNormal(Vector3 v, Vector3 n, float R, out Vector4 p, out Vector4 n4)
    {
        Vector3 u = v / R;
        float r = u.magnitude;
        if (r < 1e-7f) { p = EW; n4 = new Vector4(n.x, n.y, n.z, 0); return; }
        Vector3 d = u / r;
        float s = Mathf.Sin(r), c = Mathf.Cos(r);
        p = new Vector4(d.x * s, d.y * s, d.z * s, c);
        float along = Vector3.Dot(n, d);
        Vector3 perp = n - along * d;
        n4 = new Vector4(perp.x + along * d.x * c, perp.y + along * d.y * c, perp.z + along * d.z * c, -along * s);
    }

    /// <summary>
    /// Inverse of <see cref="ExpWithNormal"/>: encodes any point of S^3 (and a tangent normal there)
    /// as local metre coordinates around e_w. Lets baked props share one mesh with frame = identity.
    /// </summary>
    public static void LogWithNormal(Vector4 p, Vector4 n4, float R, out Vector3 v, out Vector3 n)
    {
        var xyz = new Vector3(p.x, p.y, p.z);
        float m = xyz.magnitude;
        if (m < 1e-7f)
        {
            v = p.w > 0 ? Vector3.zero : new Vector3(0, 0, Mathf.PI * R);
            n = new Vector3(n4.x, n4.y, n4.z);
            return;
        }
        float r = Mathf.Atan2(m, p.w);
        Vector3 d = xyz / m;
        v = d * (r * R);
        var tau = new Vector4(d.x * Mathf.Cos(r), d.y * Mathf.Cos(r), d.z * Mathf.Cos(r), -Mathf.Sin(r));
        float a = Vector4.Dot(n4, tau);
        Vector4 n0 = n4 - a * tau + a * new Vector4(d.x, d.y, d.z, 0);
        n = new Vector3(n0.x, n0.y, n0.z).normalized;
    }

    /// <summary>Geodesic distance (radians) between two points of S^3.</summary>
    public static float Dist(Vector4 a, Vector4 b) =>
        2f * Mathf.Asin(Mathf.Clamp((a - b).magnitude * 0.5f, 0f, 1f));

    /// <summary>Component of v tangent to S^3 at p.</summary>
    public static Vector4 Tangent(Vector4 p, Vector4 v) => v - Vector4.Dot(p, v) * p;

    /// <summary>Unit tangent at p pointing along the shorter geodesic to q (zero if degenerate).</summary>
    public static Vector4 DirTo(Vector4 p, Vector4 q)
    {
        Vector4 d = q - Vector4.Dot(p, q) * p;
        float m = d.magnitude;
        return m < 1e-7f ? Vector4.zero : d / m;
    }

    /// <summary>Point at distance t (radians) along the geodesic from p in unit tangent direction d.</summary>
    public static Vector4 Walk(Vector4 p, Vector4 d, float t) => Mathf.Cos(t) * p + Mathf.Sin(t) * d;

    /// <summary>Closest ground point (the foot) below p.</summary>
    public static Vector4 Foot(Vector4 p) => new Vector4(p.x, 0, p.z, p.w).normalized;

    /// <summary>The point at height h (radians) straight above ground point foot.</summary>
    public static Vector4 Lift(Vector4 foot, float h) => Mathf.Cos(h) * foot + Mathf.Sin(h) * EY;

    /// <summary>Unit tangent at p pointing away from the ground.</summary>
    public static Vector4 UpAt(Vector4 p) => (EY - p.y * p).normalized;

    /// <summary>Gram-Schmidt v against an orthonormal set; zero if v lies in their span.</summary>
    public static Vector4 Ortho(Vector4 v, params Vector4[] basis)
    {
        foreach (var b in basis) v -= Vector4.Dot(v, b) * b;
        float m = v.magnitude;
        return m > 1e-6f ? v / m : Vector4.zero;
    }

    static Vector4 AnyOrtho(params Vector4[] basis)
    {
        Vector4 best = Vector4.zero;
        float bestLen = -1;
        foreach (var e in new[] { EX, EY, EZ, EW })
        {
            Vector4 v = e;
            foreach (var b in basis) v -= Vector4.Dot(v, b) * b;
            if (v.magnitude > bestLen) { bestLen = v.magnitude; best = v; }
        }
        return best.normalized;
    }

    /// <summary>Right-handed frame at pos with the given up and (approximate) forward.</summary>
    public static Matrix4x4 FrameAt(Vector4 pos, Vector4 up, Vector4 forwardHint)
    {
        pos = pos.normalized;
        up = Ortho(up, pos);
        if (up == Vector4.zero) up = AnyOrtho(pos);
        Vector4 f = Ortho(forwardHint, pos, up);
        if (f == Vector4.zero) f = AnyOrtho(pos, up);
        Vector4 r = AnyOrtho(pos, up, f);
        var m = new Matrix4x4(r, up, f, pos);
        if (m.determinant < 0) m.SetColumn(0, -r);
        return m;
    }

    /// <summary>
    /// Frame standing on the ground at local ground coordinates (x, z) metres around e_w,
    /// heading yaw radians from +z towards +x (like Unity's Y rotation).
    /// </summary>
    public static Matrix4x4 GroundFrame(float x, float z, float yaw, float R)
    {
        float d = Mathf.Sqrt(x * x + z * z);
        Matrix4x4 t = d > 1e-6f ? PlaneRotation(EW, new Vector4(x / d, 0, z / d, 0), d / R) : Matrix4x4.identity;
        return t * PlaneRotation(EZ, EX, yaw);
    }

    public static Vector4 GroundPoint(float x, float z, float R) => GroundFrame(x, z, 0, R).GetColumn(3);
}
