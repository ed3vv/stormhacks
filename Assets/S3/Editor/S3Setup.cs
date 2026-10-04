using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Editor tooling: builds the S3 scene, renders preview captures, and sanity-checks the ball physics.</summary>
public static class S3Setup
{
    const string Root = "Assets/S3";
    const string ScenePath = "Assets/Scenes/S3World.unity";

    [MenuItem("S3/Create Scene")]
    public static void CreateScene()
    {
        var shader = Shader.Find("S3/Object");
        if (shader == null) throw new System.Exception("Shader S3/Object not found (did it compile?)");
        var objectMat = MaterialAsset($"{Root}/Materials/S3Object.mat", shader, false);
        var groundMat = MaterialAsset($"{Root}/Materials/S3Ground.mat", shader, true);
        ConfigureRenderers();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.80f, 0.88f, 0.95f);
        cam.fieldOfView = 70;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        camGo.AddComponent<AudioListener>();
        var player = camGo.AddComponent<S3Player>();

        var worldGo = new GameObject("S3 World");
        var world = worldGo.AddComponent<S3World>();
        world.objectMaterial = objectMat;
        world.groundMaterial = groundMat;
        var golf = worldGo.AddComponent<S3Golf>();
        golf.player = player;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        SetupGraphics();   // post volume, camera AA, pipeline settings; rebuilds and saves
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[S3] Scene created at " + ScenePath);
    }

    static Material MaterialAsset(string path, Shader shader, bool ground)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.SetFloat("_Ground", ground ? 1 : 0);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// The S^3 depth buffer isn't Euclidean, so screen-space effects that reconstruct positions
    /// from depth (SSAO) would be garbage; depth priming is also unnecessary.
    /// </summary>
    static void ConfigureRenderers()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
        {
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
            if (rd == null) continue;
            foreach (var f in rd.rendererFeatures)
                if (f != null && f.GetType().Name.Contains("AmbientOcclusion") && f.isActive)
                {
                    f.SetActive(false);
                    EditorUtility.SetDirty(f);
                }
            rd.depthPrimingMode = DepthPrimingMode.Disabled;
            EditorUtility.SetDirty(rd);
        }
    }

    /// <summary>
    /// Graphics and performance settings for the open S3 scene:
    ///   * a global post-processing volume with one profile per map (bloom, neutral tonemapping,
    ///     grading, vignette; all effects that don't read depth, which isn't Euclidean here),
    ///   * SMAA on the camera instead of MSAA,
    ///   * no camera depth / opaque texture copies (nothing reads them; they cost two passes a frame).
    /// </summary>
    [MenuItem("S3/Setup Graphics")]
    public static void SetupGraphics()
    {
        ConfigureRenderers();
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (rp == null) continue;
            rp.supportsCameraDepthTexture = false;
            rp.supportsCameraOpaqueTexture = false;
            rp.msaaSampleCount = 1;
            rp.supportsHDR = true;
            EditorUtility.SetDirty(rp);
        }

        var meadow = PostProfile($"{Root}/Post/MeadowPost.asset", false);
        var desert = PostProfile($"{Root}/Post/DesertPost.asset", true);

        var world = Object.FindAnyObjectByType<S3World>();
        var volume = Object.FindAnyObjectByType<Volume>();
        if (volume == null) volume = new GameObject("Post Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10;
        world.postVolume = volume;
        world.meadowPost = meadow;
        world.desertPost = desert;
        EditorUtility.SetDirty(world);

        var player = Object.FindAnyObjectByType<S3Player>();
        var data = player.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = player.gameObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.renderShadows = false;
        EditorUtility.SetDirty(data);

        world.Build();
        var scene = world.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[S3] Graphics set up");
    }

    static VolumeProfile PostProfile(string path, bool desert)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        T Get<T>() where T : VolumeComponent
        {
            if (!profile.TryGet(out T c))
            {
                c = profile.Add<T>(false);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            c.active = true;
            EditorUtility.SetDirty(c);
            return c;
        }
        var bloom = Get<Bloom>();
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(desert ? 0.6f : 0.35f);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(desert ? new Color(1f, 0.82f, 0.66f) : Color.white);
        Get<Tonemapping>().mode.Override(TonemappingMode.Neutral);
        var grade = Get<ColorAdjustments>();
        grade.postExposure.Override(desert ? 0.25f : 0.2f);
        grade.contrast.Override(desert ? 14f : 12f);
        grade.saturation.Override(desert ? 10f : 14f);
        var wb = Get<WhiteBalance>();
        wb.temperature.Override(desert ? 12f : 0f);
        wb.tint.Override(desert ? 4f : 0f);
        var vignette = Get<Vignette>();
        vignette.intensity.Override(desert ? 0.3f : 0.22f);
        vignette.smoothness.Override(0.45f);
        vignette.color.Override(desert ? new Color(0.32f, 0.14f, 0.1f) : new Color(0.05f, 0.1f, 0.08f));
        EditorUtility.SetDirty(profile);
        return profile;
    }

    [MenuItem("S3/Render Preview Captures")]
    public static void Capture()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var world = Object.FindAnyObjectByType<S3World>();
        world.Build();
        var golf = Object.FindAnyObjectByType<S3Golf>();
        golf.Init();
        var player = Object.FindAnyObjectByType<S3Player>();
        var cam = player.GetComponent<Camera>();
        Directory.CreateDirectory("Captures");
        player.ApplyCamera();
        Render(cam, "Captures/00_tee.png");   // the golf view as the game starts: behind the ball on hole 1

        var shots = new (string name, float x, float z, float yaw, float pitch)[]
        {
            ("01_start", 0, -1.5f, 0, -4),
            ("02_house", 2, 3, 28, 2),
            ("03_up", 0, 0, 0, 60),
            ("04_down", 0, 0, 0, -45),
            ("05_far_house", 0, 0, 225, 0),
            ("06_behind", 0, 0, 180, -4),
        };
        var ballCamera = player.cameraFilter;
        player.cameraFilter = null;   // walking poses
        foreach (var s in shots)
        {
            player.SetPose(s.x, s.z, s.yaw, s.pitch);
            player.ApplyCamera();
            Render(cam, $"Captures/{s.name}.png");
        }
        player.cameraFilter = ballCamera;
        golf.DebugAim(60);
        player.ApplyCamera();
        Render(cam, "Captures/07_aim60.png");
        golf.DebugAim(90);
        player.ApplyCamera();
        Render(cam, "Captures/08_aim90.png");
        foreach (var (name, t) in new[] { ("09_flight_rise", 0.9f), ("10_flight_pole", 1.45f), ("11_flight_fall", 2.2f), ("12_landed", 4.5f) })
        {
            golf.DebugFly(1f, 90f, t);
            player.ApplyCamera();
            Render(cam, $"Captures/{name}.png");
            Debug.Log($"[S3] {name}: {golf.DebugState}");
        }
        // ball under a tree: the tree should dither away (compare with fading switched off)
        Vector4 tee = world.holes[0].tee;
        S3World.Obstacle tree = default;
        float best = float.MaxValue;
        foreach (var o in world.obstacles)
            if (o.height > 5 && o.radius < 0.6f && S3.Dist(o.foot, tee) < best) { best = S3.Dist(o.foot, tee); tree = o; }
        Vector4 toTee = S3.DirTo(tree.foot, world.holes[0].cup);
        golf.DebugPlaceBall(S3.Walk(tree.foot, toTee == Vector4.zero ? S3.Ortho(S3.EZ, tree.foot, S3.EY) : -toTee, (tree.radius + 0.6f) / world.radius));
        player.ApplyCamera();
        Render(cam, "Captures/13_tree_fade.png");
        Shader.SetGlobalFloat("_S3Fade", 0);
        Render(cam, "Captures/13b_tree_no_fade.png");
        Shader.SetGlobalFloat("_S3Fade", 0);

        // the desert map
        var originalBiome = world.biome;
        world.SetBiome(S3World.Biome.Desert);
        golf.Init();
        player.ApplyCamera();
        Render(cam, "Captures/20_desert_tee.png");
        golf.DebugAim(60);
        player.ApplyCamera();
        Render(cam, "Captures/21_desert_aim60.png");
        player.cameraFilter = null;
        foreach (var s in new (string name, float x, float z, float yaw, float pitch)[]
                 {
                     ("22_desert_fort", -1.5f, 2.5f, -38, 8),
                     ("23_desert_fort_close", -5.5f, 5.0f, -42, 14),
                     ("24_desert_wide", 3, -6, 20, 2),
                     ("25_desert_up", 0, 0, 0, 55),
                 })
        {
            player.SetPose(s.x, s.z, s.yaw, s.pitch);
            player.ApplyCamera();
            Render(cam, $"Captures/{s.name}.png");
        }
        player.cameraFilter = ballCamera;
        world.ClearFades();
        world.biome = originalBiome;

        world.Build();   // drop the golf props again; the edit-mode preview is just the world
        player.SetPose(0, 0, 0, -5);
        Debug.Log("[S3] Captures written to " + Path.GetFullPath("Captures"));
    }

    static void Render(Camera cam, string path)
    {
        const int W = 1280, H = 720;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) ;
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
    }

    [MenuItem("S3/Test Ball Physics")]
    public static void TestPhysics()
    {
        var log = new StringBuilder("[S3] physics checks\n");
        float R = 12f;
        Vector4 start = S3.GroundPoint(0, 0, R);

        // 1. a putt rolls along a great circle with constant deceleration
        var b = new S3Ball { R = R };
        b.Place(start);
        b.Launch(S3.EZ, 6f, 0);
        float t = 0, travelled = 0;
        Vector4 prev = b.Foot;
        while (t < 30 && !(b.grounded && b.Speed < 0.05f))
        {
            b.Step(1 / 240f, 1.7f, null);
            travelled += S3.Dist(prev, b.Foot) * R;
            prev = b.Foot;
            t += 1 / 240f;
        }
        log.AppendLine($"putt 6 m/s, friction 1.7: rolled {travelled:0.00} m (flat-world prediction {36 / (2 * 1.7f):0.00} m), stopped after {t:0.00}s, ended {S3.Dist(start, b.Foot) * R:0.00} m from start");

        // 2. drive: flight distance and apex vs. the flat formula
        b.Place(start);
        b.Launch(S3.EZ, 20f, 30 * Mathf.Deg2Rad);
        t = 0;
        float apex = 0;
        while (t < 10)
        {
            b.Step(1 / 240f, 1.7f, null);
            t += 1 / 240f;
            apex = Mathf.Max(apex, b.HeightM - b.radius);
            if (t > 0.1f && b.HeightM <= b.radius + 1e-3f) break;
        }
        float carry = S3.Dist(start, b.Foot) * R;
        log.AppendLine($"drive 20 m/s @30°: carry {carry:0.00} m (flat ~{400 * Mathf.Sin(60 * Mathf.Deg2Rad) / 9.81f:0.0} m with no drag), apex {apex:0.00} m (flat {100 / (2 * 9.81f):0.00} m), flight {t:0.00}s");

        // 3. a hard frictionless putt laps the world and comes back to where it started
        b.Place(start);
        b.Launch(S3.EX, 10f, 0);
        b.airDrag = 0;
        float lap = 2 * Mathf.PI * R / 10f;
        for (t = 0; t < lap; t += 1 / 240f) b.Step(1 / 240f, 0, null);
        log.AppendLine($"lap: after {lap:0.00}s at 10 m/s the ball is {S3.Dist(start, b.Foot) * R:0.000} m from its start (one lap = {2 * Mathf.PI * R:0.0} m)");

        // 4. game physics (ed3vv constants): soft and medium shots, then vertical drives that
        //    either fall back or cross the pole to the antipode
        foreach (var (pw, deg) in new[] { (0f, 2f), (0.3f, 2f), (0.5f, 24f), (1f, 24f) })
        {
            b = S3Golf.NewBall(R);
            b.Place(start);
            b.Launch(S3.EZ, S3Golf.LaunchSpeed(pw, R), deg * Mathf.Deg2Rad);
            for (t = 0; t < 30 && !(t > 0.1f && b.grounded && b.Speed < 0.05f); t += 1 / 240f) b.Step(1 / 240f, 0.54f * R, null);
            log.AppendLine($"shot at {pw:P0} power, {deg}°: {S3Golf.LaunchSpeed(pw, R):0.0} m/s, came to rest {S3.Dist(start, b.Foot) * R:0.00} m away after {t:0.00}s");
        }
        foreach (float speed in new[] { 0.90f, 0.93f, 0.95f, 1f })
        {
            float v0 = S3Golf.LaunchSpeed(speed, R);
            b = S3Golf.NewBall(R);
            b.Place(start);
            b.Launch(S3.EZ, v0, 90 * Mathf.Deg2Rad);
            float top = 0;
            for (t = 0; t < 12; t += 1 / 240f)
            {
                b.Step(1 / 240f, 0.54f * R, null);
                top = Mathf.Max(top, b.HeightM);
                if (t > 0.2f && b.grounded) break;
            }
            log.AppendLine($"vertical drive at {speed:P0} power ({v0:0.0} m/s): apex {top:0.00} m (pole at {0.5f * Mathf.PI * R:0.00} m), " +
                           $"landed {S3.Dist(start, b.Foot) * R:0.00} m away (antipode = {Mathf.PI * R:0.00} m) after {t:0.00}s");
        }

        // 5. a ball at rest exactly on the pole still falls
        b = new S3Ball { R = R };
        b.Place(start);
        b.pos = S3.EY;
        b.vel = Vector4.zero;
        for (t = 0; t < 1; t += 1 / 240f) b.Step(1 / 240f, 1.7f, null);
        log.AppendLine($"ball released at rest on the pole: after 1s it has dropped {0.5f * Mathf.PI * R - b.HeightM:0.00} m (free fall 4.9 m), NaN: {float.IsNaN(b.pos.x)}");

        // 6. exp/log round trip
        var v = new Vector3(3, 1, -20);
        S3.ExpWithNormal(v, Vector3.up, R, out var p, out var n4);
        S3.LogWithNormal(p, n4, R, out var v2, out var n2);
        log.AppendLine($"exp/log round trip error {(v - v2).magnitude:0.000000}, normal error {(Vector3.up - n2).magnitude:0.000000}");

        // 7. course geometry
        var world = Object.FindAnyObjectByType<S3World>();
        if (world != null)
            for (int i = 0; i < world.holes.Count; i++)
                log.AppendLine($"hole {i + 1}: tee→cup {world.GroundDistance(world.holes[i].tee, world.holes[i].cup):0.0} m");
        Debug.Log(log.ToString());
        File.WriteAllText("Captures/physics.txt", log.ToString());
    }

    /// <summary>Batch entry point: unity run ... -- -executeMethod S3Setup.BatchAll</summary>
    public static void BatchAll()
    {
        CreateScene();
        Capture();
        TestPhysics();
    }

    public static void BatchCapture()
    {
        Capture();
        TestPhysics();
    }
}
