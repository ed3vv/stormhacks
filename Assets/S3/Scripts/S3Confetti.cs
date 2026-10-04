using UnityEngine;

/// <summary>
/// A burst of confetti out of the cup when the ball drops in: paper flakes in local metres around the cup's
/// ground frame, thrown up, tumbling and fluttering back down to settle flat on the green, then shrinking away.
/// One dynamic mesh, rewritten each frame (it's local metres, so the S3/Object shader places it like any prop).
/// </summary>
public class S3Confetti
{
    const int Count = 120;
    const float Gravity = 6f, Drag = 1.7f, Life = 4.5f, Shrink = 0.9f;
    static readonly Color[] Palette =
    {
        new Color(1f, 0.80f, 0.25f, 0.8f), new Color(0.95f, 0.25f, 0.22f, 0.8f), new Color(0.30f, 0.70f, 1f, 0.8f),
        new Color(0.98f, 0.98f, 0.95f, 0.8f), new Color(0.55f, 0.90f, 0.40f, 0.8f), new Color(0.85f, 0.45f, 0.95f, 0.8f),
    };

    readonly Mesh mesh;
    readonly Renderer renderer;
    readonly Vector3[] pos = new Vector3[Count], vel = new Vector3[Count], axis = new Vector3[Count];
    readonly float[] angle = new float[Count], spin = new float[Count], size = new float[Count];
    readonly bool[] landed = new bool[Count];
    readonly Vector3[] verts = new Vector3[Count * 4], normals = new Vector3[Count * 4];
    float age = -1;

    public S3Confetti(S3World world)
    {
        mesh = new Mesh { name = "S3 Confetti" };
        mesh.MarkDynamic();
        var colors = new Color[Count * 4];
        var tris = new int[Count * 6];
        for (int i = 0; i < Count; i++)
        {
            for (int k = 0; k < 4; k++) colors[4 * i + k] = Palette[i % Palette.Length];
            int v = 4 * i;
            tris[6 * i + 0] = v; tris[6 * i + 1] = v + 2; tris[6 * i + 2] = v + 1;
            tris[6 * i + 3] = v; tris[6 * i + 4] = v + 3; tris[6 * i + 5] = v + 2;
        }
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.colors = colors;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // S^3 coordinates: never cull
        renderer = world.CreateRenderer("Confetti", mesh, world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        renderer.enabled = false;
    }

    /// <summary>Throw a fresh burst up from the origin of a ground frame (the cup).</summary>
    public void Burst(Matrix4x4 frame)
    {
        S3World.SetFrame(renderer, frame);
        for (int i = 0; i < Count; i++)
        {
            float a = Random.value * 2 * Mathf.PI, outward = Random.Range(0.5f, 2.8f), up = Random.Range(3.8f, 7f);
            pos[i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Random.Range(0, 0.25f) + Vector3.up * 0.05f;
            vel[i] = new Vector3(Mathf.Cos(a) * outward, up, Mathf.Sin(a) * outward);
            axis[i] = Random.onUnitSphere;
            angle[i] = Random.value * 360;
            spin[i] = Random.Range(360f, 900f);
            size[i] = Random.Range(0.06f, 0.11f);
            landed[i] = false;
        }
        age = 0;
        renderer.enabled = true;
        Write();
    }

    public void Hide()
    {
        age = -1;
        if (renderer) renderer.enabled = false;
    }

    public void Update(float dt)
    {
        if (age < 0) return;
        age += dt;
        if (age > Life) { Hide(); return; }
        float keep = Mathf.Exp(-Drag * dt);
        for (int i = 0; i < Count; i++)
        {
            if (landed[i]) continue;
            vel[i].y -= Gravity * dt;
            vel[i] *= keep;   // terminal fall of Gravity / Drag: paper drifts down
            // flutter: a sideways wobble that grows as a flake slows
            vel[i].x += Mathf.Sin(age * 7f + i * 1.7f) * 2.2f * dt;
            vel[i].z += Mathf.Cos(age * 6f + i * 2.3f) * 2.2f * dt;
            pos[i] += vel[i] * dt;
            angle[i] += spin[i] * dt;
            if (pos[i].y <= 0.02f && vel[i].y < 0) { pos[i].y = 0.02f; landed[i] = true; }
        }
        Write();
    }

    void Write()
    {
        float scale = 1 - Mathf.Clamp01((age - (Life - Shrink)) / Shrink);
        for (int i = 0; i < Count; i++)
        {
            // flakes in the air tumble about their own axis; landed ones lie flat on the grass
            var q = landed[i] ? Quaternion.AngleAxis(angle[i], Vector3.up) : Quaternion.AngleAxis(angle[i], axis[i]);
            Vector3 u = q * Vector3.right * (size[i] * scale), w = q * Vector3.forward * (size[i] * 0.6f * scale), n = q * Vector3.up;
            int v = 4 * i;
            verts[v] = pos[i] - u - w;
            verts[v + 1] = pos[i] + u - w;
            verts[v + 2] = pos[i] + u + w;
            verts[v + 3] = pos[i] - u + w;
            normals[v] = normals[v + 1] = normals[v + 2] = normals[v + 3] = n;
        }
        mesh.vertices = verts;
        mesh.normals = normals;
    }
}
