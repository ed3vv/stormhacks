using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Owns the 3-sphere world: builds the ground and scenery meshes, defines the golf course,
/// and pushes the camera / lighting uniforms that the "S3/Object" shader reads.
///
/// Runs in edit mode too. Every camera gets its own S^3 view just before it renders: the game
/// camera sees through the player's eyes, and the Scene view camera's position and rotation are
/// carried onto S^3 by the exponential map, so flying around the Scene view moves through the world.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-100)]
public class S3World : MonoBehaviour
{
    public static S3World Instance { get; private set; }

    [Serializable]
    public struct HoleDef
    {
        public Vector2 tee;   // local ground coordinates (x, z) in metres around the origin e_w
        public Vector2 cup;
        public int par;
        public HoleDef(float tx, float tz, float cx, float cz, int par) { tee = new Vector2(tx, tz); cup = new Vector2(cx, cz); this.par = par; }
    }

    public class Hole { public Vector4 tee, cup; public int par; }

    public struct Obstacle
    {
        public Vector4 foot;   // ground point
        public float radius;   // metres
        public float height;   // metres
        public Obstacle(Vector4 foot, float radius, float height) { this.foot = foot; this.radius = radius; this.height = height; }
    }

    [Header("Geometry")]
    [Tooltip("Radius of the 3-sphere in metres. Walking 2*pi*R in a straight line brings you back.")]
    public float radius = 12f;
    public int seed = 11;

    [Header("Rendering")]
    public Material objectMaterial;
    public Material groundMaterial;
    public Vector4 sun = new Vector4(0.55f, 0.75f, 0.35f, 0.2f);
    [Range(0, 1)] public float ambient = 0.62f;
    public Color haze = new Color(0.82f, 0.90f, 0.96f, 0.22f);
    [Tooltip("Depth range parameter: geometry nearer than about nearClip*R (or that close to the antipode) is clipped.")]
    public float nearClip = 0.004f;

    [Header("Course")]
    public HoleDef[] course =
    {
        new HoleDef(0, 3, 0, 24, 3),        // straight ahead, the cup sits just past the horizon
        new HoleDef(3, 27, -22, 8, 3),
        new HoleDef(-20, 6, 16.1f, -4.8f, 4), // cup is the tee's antipode: every direction leads there
        new HoleDef(19, -6, -2, -20, 3),
        new HoleDef(-2, -24, -3, -4, 3),
    };
    public float cupRadius = 0.18f;
    public float greenRadius = 3.5f;

    [NonSerialized] public readonly List<Hole> holes = new List<Hole>();
    [NonSerialized] public readonly List<Obstacle> obstacles = new List<Obstacle>();

    static readonly int ModelID = Shader.PropertyToID("_S3Model");
    static readonly int ViewID = Shader.PropertyToID("_S3View");
    static readonly int EyeID = Shader.PropertyToID("_S3Eye");
    static MaterialPropertyBlock mpb;

    static CommandBuffer cameraCmd;
    S3Player player;

    void Awake()
    {
        if (Application.isPlaying) Build();
    }

    void OnEnable()
    {
        Instance = this;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        if (!Application.isPlaying) Build();   // edit-mode preview (children are DontSave, never written to the scene)
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (!Application.isPlaying) ClearChildren();
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
    }

    /// <summary>Sets the S^3 view for whichever camera is about to render.</summary>
    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        Matrix4x4 frame;
        if (cam.cameraType == CameraType.SceneView) frame = SceneViewFrame(cam.transform);
        else
        {
            if (!player) player = FindAnyObjectByType<S3Player>();
            if (!player) return;
            frame = player.ViewFrame();
        }
        Matrix4x4 view = ViewMatrix(frame);
        cameraCmd ??= new CommandBuffer { name = "S3 camera" };
        cameraCmd.Clear();
        cameraCmd.SetGlobalMatrix(ViewID, view);
        cameraCmd.SetGlobalVector(EyeID, frame.GetColumn(3));
        context.ExecuteCommandBuffer(cameraCmd);
        cameraCmd.Clear();
        Shader.SetGlobalMatrix(ViewID, view);
        Shader.SetGlobalVector(EyeID, frame.GetColumn(3));
    }

    /// <summary>
    /// A Euclidean camera pose read as a point in the exp-map chart around e_w: walk |p| metres
    /// along the geodesic towards p, carrying the camera's axes with it.
    /// </summary>
    public Matrix4x4 SceneViewFrame(Transform t)
    {
        Vector3 p = t.position;
        float d = p.magnitude;
        Matrix4x4 walk = d > 1e-6f ? S3.PlaneRotation(S3.EW, new Vector4(p.x / d, p.y / d, p.z / d, 0), d / radius) : Matrix4x4.identity;
        return walk * Matrix4x4.Rotate(t.rotation);
    }

    static Matrix4x4 ViewMatrix(Matrix4x4 cameraFrame) => Matrix4x4.Scale(new Vector3(1, 1, -1)) * cameraFrame.transpose;

    /// <summary>(Re)generates the ground, scenery and course. Safe to call in edit mode.</summary>
    public void Build()
    {
        Instance = this;
        ClearChildren();
        holes.Clear();
        obstacles.Clear();
        foreach (var h in course)
            holes.Add(new Hole { tee = S3.GroundPoint(h.tee.x, h.tee.y, radius), cup = S3.GroundPoint(h.cup.x, h.cup.y, radius), par = h.par });

        var ground = new MeshBuilder();
        ground.GroundDisk(radius, 72, 160, Color.white);
        CreateRenderer("Ground", ground.ToMesh("S3 Ground"), groundMaterial, Matrix4x4.identity);

        var scenery = S3Scenery.Build(this, new System.Random(seed));
        CreateRenderer("Scenery", scenery.ToMesh("S3 Scenery"), objectMaterial, Matrix4x4.identity);

        PushGlobals();
    }

    public GameObject CreateRenderer(string name, Mesh mesh, Material material, Matrix4x4 frame)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        SetFrame(r, frame);
        return go;
    }

    /// <summary>Places a renderer's local-metre mesh at an SO(4) frame.</summary>
    public static void SetFrame(Renderer r, Matrix4x4 frame)
    {
        mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetMatrix(ModelID, frame);
        r.SetPropertyBlock(mpb);
    }

    void PushGlobals()
    {
        Shader.SetGlobalFloat("_S3R", radius);
        Shader.SetGlobalFloat("_S3InvR", 1f / radius);
        Shader.SetGlobalFloat("_S3Near", nearClip);
        Shader.SetGlobalFloat("_S3Ambient", ambient);
        Shader.SetGlobalVector("_S3Sun", sun.normalized);
        Shader.SetGlobalVector("_S3Haze", haze);
        Shader.SetGlobalFloat("_S3CupR", cupRadius);
        Shader.SetGlobalFloat("_S3GreenR", greenRadius);
    }

    /// <summary>Camera frame in SO(4): columns (right, up, forward, eye position).</summary>
    public void SetCamera(Matrix4x4 cameraFrame)
    {
        Shader.SetGlobalMatrix(ViewID, ViewMatrix(cameraFrame));
        Shader.SetGlobalVector(EyeID, cameraFrame.GetColumn(3));
        PushGlobals();
    }

    public void SetActiveCup(Vector4 cup) => Shader.SetGlobalVector("_S3Hole", cup);

    /// <summary>Ground distance in metres between two points (their feet).</summary>
    public float GroundDistance(Vector4 a, Vector4 b) => S3.Dist(S3.Foot(a), S3.Foot(b)) * radius;
}
