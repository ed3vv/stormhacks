using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        worldGo.AddComponent<S3Audio>().AssignDefaultClips();

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
    ///   * 4x MSAA on PC (no SMAA: it only blurs on top), render scale 1.5,
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
            // 4x MSAA on PC for the low-poly silhouettes and grass (SMAA on the camera only catches some of it)
            rp.msaaSampleCount = rp.name.Contains("Mobile") ? 1 : 4;
            if (!rp.name.Contains("Mobile")) rp.renderScale = 1.5f;   // supersampled, on top of MSAA: smoother grass and thin edges
            rp.supportsHDR = true;
            EditorUtility.SetDirty(rp);
        }

        var meadow = PostProfile($"{Root}/Post/MeadowPost.asset", 0);
        var desert = PostProfile($"{Root}/Post/DesertPost.asset", 1);
        var basin = PostProfile($"{Root}/Post/BasinPost.asset", 2);

        var world = Object.FindAnyObjectByType<S3World>();
        world.beamMaterial = ShaderMaterial($"{Root}/Materials/S3Beam.mat", "S3/Beam");
        world.glowMaterial = ShaderMaterial($"{Root}/Materials/S3Glow.mat", "S3/Glow");
        world.glowMaterial.SetFloat("_Intensity", 1.5f);
        world.glowMaterial.SetFloat("_Shimmer", 0.8f);
        EditorUtility.SetDirty(world.glowMaterial);
        world.basinPost = basin;
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
        data.antialiasing = AntialiasingMode.None;   // MSAA (the pipeline asset) handles edges; SMAA on top only blurs
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

    static Material ShaderMaterial(string path, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) throw new System.Exception($"Shader {shaderName} not found (did it compile?)");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>look: 0 meadow, 1 desert, 2 eternal poles.</summary>
    static VolumeProfile PostProfile(string path, int look)
    {
        bool desert = look == 1, basin = look == 2;
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
        bloom.intensity.Override(desert ? 0.6f : 0.45f);
        bloom.highQualityFiltering.Override(!desert);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(desert ? new Color(1f, 0.82f, 0.66f) : Color.white);
        Get<Tonemapping>().mode.Override(TonemappingMode.Neutral);
        var grade = Get<ColorAdjustments>();
        grade.postExposure.Override(0.25f);
        grade.contrast.Override(desert ? 14f : 16f);
        grade.saturation.Override(desert ? 10f : 2f);   // the meadow's greens are toned down
        var wb = Get<WhiteBalance>();
        wb.temperature.Override(desert ? 12f : 6f);
        wb.tint.Override(desert ? 4f : 0f);
        var vignette = Get<Vignette>();
        vignette.intensity.Override(desert ? 0.3f : 0.22f);
        vignette.smoothness.Override(0.45f);
        vignette.color.Override(desert ? new Color(0.32f, 0.14f, 0.1f) : new Color(0.05f, 0.1f, 0.08f));
        if (basin)
        {
            // polar day: cool, bright snow, bloom for the ice and the lasers
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.7f);
            bloom.highQualityFiltering.Override(false);
            bloom.tint.Override(new Color(0.85f, 0.95f, 1f));
            grade.postExposure.Override(0.05f);
            grade.contrast.Override(10f);
            grade.saturation.Override(8f);
            wb.temperature.Override(-10f);
            wb.tint.Override(0f);
            vignette.intensity.Override(0.24f);
            vignette.color.Override(new Color(0.06f, 0.09f, 0.22f));
        }
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
        Render(cam, "Captures/20_desert_puzzle1.png");
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

        // the eternal poles
        world.SetBiome(S3World.Biome.Basin);
        golf.Init();
        player.ApplyCamera();
        Render(cam, "Captures/30_basin_tee.png");
        player.cameraFilter = null;
        foreach (var s in new (string name, float x, float z, float yaw, float pitch)[]
                 {
                     ("31_basin_lake", 0, 2, 0, 2),
                     ("32_basin_up", 0, 0, 0, 55),
                     ("33_basin_castle", -4, 12, -40, 6),
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

    // ------------------------------------------------------------------ laser puzzles

    static List<S3Laser.Block> SceneryBlocks(S3World world) =>
        world.obstacles.Select(o => S3Laser.Block.Scenery(o, world.radius)).ToList();

    /// <summary>
    /// Every Desert puzzle and Basin lock: the intended solution lights the crystal with the real
    /// scenery in place, nothing is lit before you start, and (Basin) the solved light keeps clear of
    /// the tee and the sealed green.
    /// </summary>
    [MenuItem("S3/Test Laser Puzzles")]
    public static string TestPuzzles()
    {
        var world = Object.FindAnyObjectByType<S3World>();
        var log = new StringBuilder("[S3] laser puzzles\n");
        var original = world.biome;
        float R = world.radius;
        foreach (var biome in new[] { S3World.Biome.Desert, S3World.Biome.Basin })
        {
            world.SetBiome(biome);
            var scenery = SceneryBlocks(world);
            for (int i = 0; i < world.levels.Count; i++)
            {
                var level = world.levels[i];
                var test = new S3Levels.Level { blocks = new List<S3Laser.Block>(level.blocks), solution = level.solution, start = level.start };
                if (biome == S3World.Biome.Basin)
                    test.blocks.AddRange(S3Puzzle.GateWalls(world.holes[i].cup, world.barrierRadius - 0.45f, 12, R));
                bool works = S3Levels.SolutionWorks(test, scenery), clean = S3Levels.SolutionWorks(test, null), unsolved = S3Levels.StartsUnsolved(test, scenery);
                log.Append($"{biome} {i + 1} {level.title}: solution {(works ? "lights the crystal" : clean ? "BLOCKED BY SCENERY" : "FAILS")}, start {(unsolved ? "unsolved" : "ALREADY SOLVED")}");
                if (biome == S3World.Biome.Basin)
                {
                    var h = world.holes[i];
                    log.Append($", hole {world.GroundDistance(h.tee, h.cup):0.0} m, {LockReport(world, level, h)}");
                }
                log.AppendLine();
            }
        }
        world.SetBiome(original);
        Directory.CreateDirectory("Captures");
        File.WriteAllText("Captures/puzzles.txt", log.ToString());
        Debug.Log(log.ToString());
        return log.ToString();
    }

    static string LockReport(S3World world, S3Levels.Level level, S3World.Hole h)
    {
        float R = world.radius;
        float light = float.MaxValue, lightTee = float.MaxValue, pieces = float.MaxValue, piecesTee = float.MaxValue;
        foreach (var p in S3Levels.SolutionFootprint(level, R, 0.3f))
        {
            light = Mathf.Min(light, world.GroundDistance(p, h.cup));
            lightTee = Mathf.Min(lightTee, world.GroundDistance(p, h.tee));
        }
        foreach (var b in level.blocks.Concat(level.solution))
        {
            pieces = Mathf.Min(pieces, world.GroundDistance(b.position, h.cup));
            piecesTee = Mathf.Min(piecesTee, world.GroundDistance(b.position, h.tee));
        }
        float goal = world.GroundDistance(level.Goal.position, h.cup);
        float walk = level.solution.Max(b => world.GroundDistance(b.position, h.tee));
        float startTee = S3Levels.LightFootprint(level.blocks, R, 0.4f).Select(p => world.GroundDistance(p, h.tee)).DefaultIfEmpty(99).Min();
        return $"crystal {goal:0.0} m from cup, light ≥{light:0.0} m from cup / ≥{lightTee:0.0} m from tee (unsolved ≥{startTee:0.0}), pieces ≥{pieces:0.0} / ≥{piecesTee:0.0} m, furthest piece {walk:0.0} m from tee";
    }

    /// <summary>Places a piece may not go on hole h of the Eternal Poles while its cup is sealed (as S3Golf.Forbidden, with the ball on the tee).</summary>
    static System.Func<Vector4, bool> Forbidden(S3World world, S3World.Hole h) => p =>
        world.GroundDistance(p, h.cup) < Mathf.Max(world.cupRadius + 1.2f, world.barrierRadius + 0.4f) ||
        world.GroundDistance(p, h.tee) < 0.31f + 0.7f;

    /// <summary>
    /// Searches placements of every Basin hole's lock template (and its variants) around the cup: the blocks and the
    /// intended pieces clear of the green and the tee and placeable, the solved light at least 3.6 m from the cup (the
    /// gate absorbs anything nearer), nothing lit before you start. Scores prefer a crystal about 6 m from the cup and
    /// the lock near the tee. Prints the best LockDef per hole; check the winner with TestPuzzles and TestShortcuts.
    /// </summary>
    public static string SearchLocks(int only = -1, float step = 1.5f, float span = 12f, int yawStep = 15)
    {
        var world = Object.FindAnyObjectByType<S3World>();
        var original = world.biome;
        world.SetBiome(S3World.Biome.Basin);
        float R = world.radius;
        var log = new StringBuilder();
        for (int i = 0; i < world.holes.Count && i < world.basinLocks.Length; i++)
        {
            if (only >= 0 && i != only) continue;
            var h = world.holes[i];
            var def = world.basinCourse[i];
            var template = world.basinLocks[i].template;
            var gate = S3Puzzle.GateWalls(h.cup, world.barrierRadius - 0.45f, 12, R);
            int variants = template == S3Levels.Template.Starburst ? 12 : template == S3Levels.Template.ParallelLines ? 1 : 3;
            float best = float.MaxValue;
            S3World.LockDef bestDef = default;
            string bestReport = "none found";
            for (float dx = -span; dx <= span + 1e-3f; dx += step)
                for (float dz = -span; dz <= span + 1e-3f; dz += step)
                    for (int yaw = 0; yaw < 360; yaw += yawStep)
                        foreach (bool mirror in new[] { false, true })
                            for (int variant = 0; variant < variants; variant++)
                            {
                                var d = new S3World.LockDef(template, def.cup.x + dx, def.cup.y + dz, yaw, mirror, variant);
                                var level = world.BuildLock(d);
                                // the blocks are the same for every variant
                                if (level.blocks.Exists(b => world.GroundDistance(b.position, h.cup) < world.barrierRadius + 1.2f ||
                                                             world.GroundDistance(b.position, h.tee) < 2.5f)) break;
                                if (level.solution.Count == 0) break;
                                if (level.solution.Exists(b => world.GroundDistance(b.position, h.cup) < world.barrierRadius + 1.2f ||
                                                               world.GroundDistance(b.position, h.tee) < 2.5f)) continue;
                                var blocks = new List<S3Laser.Block>(level.blocks);
                                blocks.AddRange(gate);
                                var placed = new List<S3Laser.Block>(blocks);
                                bool placeable = true;
                                foreach (var b in level.solution)
                                {
                                    if (!S3Solver.CanPlace(b, placed, null, null, R)) { placeable = false; break; }
                                    placed.Add(b);
                                }
                                if (!placeable || !S3Solver.Solves(blocks, level.solution, null) || S3Solver.Solves(blocks, new S3Laser.Block[0], null)) continue;
                                var scene = new List<S3Laser.Block>(blocks);
                                scene.AddRange(level.solution);
                                float light = S3Levels.LightFootprint(scene, R, 0.5f, true).Select(p => world.GroundDistance(p, h.cup)).DefaultIfEmpty(99).Min();
                                if (light < 3.6f) continue;
                                float crystal = level.blocks.Where(b => b.kind == S3Laser.Kind.Goal).Min(b => world.GroundDistance(b.position, h.cup));
                                float score = Mathf.Abs(crystal - 6f) + 0.15f * world.GroundDistance(level.start, h.tee) - 0.1f * Mathf.Min(light, 6f);
                                if (score < best)
                                {
                                    best = score;
                                    bestDef = d;
                                    bestReport = LockReport(world, level, h);
                                }
                            }
            log.AppendLine($"hole {i + 1} {template}: new LockDef(S3Levels.Template.{template}, {bestDef.x}f, {bestDef.z}f, {bestDef.yaw}, {(bestDef.mirror ? "true" : "false")}, {bestDef.variant}), // {bestReport}");
        }
        world.SetBiome(original);
        Debug.Log(log.ToString());
        return log.ToString();
    }

    /// <summary>
    /// Shortcut checks for the Eternal Poles' locks, with the gate and the real scenery in place: every way the pieces you
    /// are given can light the crystals has to use the lock's trick.
    ///   * Parallel Lines: a single mirror only works near where the two "parallel" lines meet (a quarter lap north of the
    ///     laser), at that point's antipode, or at the laser's own antipode (via a lap off the frozen mirror).
    ///   * Crossfire: one mirror only works at the far crossing of the two beams.
    ///   * Back Door: one mirror never works; with two, the second is always near the first one's antipode.
    ///   * Starburst: the mirror or the diffractor alone never works; with the diffractor at the sealed crystal's antipode
    ///     the mirror works in short windows round the laser's line (listed).
    ///   * The Long Night: one mirror and the diffractor never work; with two mirrors, the second is always near the
    ///     first one's antipode.
    /// </summary>
    public static string TestShortcuts(int only = -1)
    {
        var world = Object.FindAnyObjectByType<S3World>();
        var original = world.biome;
        world.SetBiome(S3World.Biome.Basin);
        float R = world.radius;
        var scenery = SceneryBlocks(world);
        var log = new StringBuilder("[S3] lock shortcuts\n");
        string Metres(float m) => $"{m:0.0} m";
        for (int i = 0; i < world.levels.Count; i++)
        {
            if (only >= 0 && i != only) continue;
            var level = world.levels[i];
            var h = world.holes[i];
            var blocks = new List<S3Laser.Block>(level.blocks);
            blocks.AddRange(S3Puzzle.GateWalls(h.cup, world.barrierRadius - 0.45f, 12, R));
            var forbidden = Forbidden(world, h);
            Vector4 laser = level.blocks.Find(b => b.kind == S3Laser.Kind.Emitter).position;
            var diffractor = level.solution.Find(b => b.kind == S3Laser.Kind.Diffractor);
            string verdict;
            switch (world.basinLocks[i].template)
            {
                case S3Levels.Template.ParallelLines:
                {
                    Vector4 meet = level.solution[0].position;
                    var spots = S3Solver.OneMirrorSampled(blocks, scenery, forbidden, R, 0.2f, 0.5f);
                    int near = spots.Count(m => S3.Dist(m.position, meet) * R < 4f), far = spots.Count(m => S3.Dist(m.position, -meet) * R < 4f);
                    int back = spots.Count(m => S3.Dist(m.position, -laser) * R < 3f);
                    int other = spots.Count - near - far - back;
                    verdict = near == 0 ? "NO SOLUTION NEAR THE MEETING POINT" :
                              $"one mirror works near the meeting point ({near} spots), its antipode ({far}), the laser's antipode ({back})" +
                              (other == 0 ? ", nowhere else" : $"; SHORTCUT at {other} other spots");
                    break;
                }
                case S3Levels.Template.Crossfire:
                {
                    Vector4 crossing = level.solution[0].position;
                    var spots = S3Solver.OneMirror(blocks, scenery, forbidden, R);
                    int other = spots.Count(m => S3.Dist(m.position, crossing) * R > 1.5f);
                    verdict = spots.Count == 0 ? "NO ONE-MIRROR SOLUTION FOUND"
                            : other == 0 ? $"one mirror works only at the far crossing ({spots.Count} spots, all within {Metres(spots.Max(m => S3.Dist(m.position, crossing) * R))})"
                            : $"SHORTCUT: one mirror works {other} times away from the far crossing";
                    break;
                }
                case S3Levels.Template.BackDoor:
                {
                    int one = S3Solver.OneMirror(blocks, scenery, forbidden, R).Count + S3Solver.OneMirrorSampled(blocks, scenery, forbidden, R, 0.25f, 1f).Count;
                    var pairs = S3Solver.TwoMirrors(blocks, scenery, forbidden, R);
                    var off = pairs.Where(p => p.second == null || S3.Dist(p.second.position, -p.first.position) * R > 3f).ToList();
                    verdict = (one > 0 ? $"SHORTCUT with one mirror ({one}); " : "one mirror never works; ") +
                              (pairs.Count == 0 ? "NO TWO-MIRROR SOLUTION FOUND"
                              : off.Count == 0 ? $"{pairs.Count} two-mirror solutions, the second mirror always within {Metres(pairs.Max(p => S3.Dist(p.second.position, -p.first.position) * R))} of the first one's antipode"
                              : $"SHORTCUT: {off.Count} of {pairs.Count} two-mirror solutions don't hop half a world");
                    break;
                }
                case S3Levels.Template.Starburst:
                {
                    int mirrorAlone = S3Solver.OneMirror(blocks, scenery, forbidden, R).Count;
                    bool diffractorAlone = S3Solver.Solves(blocks, new[] { diffractor }, scenery);
                    var withDiffractor = new List<S3Laser.Block>(blocks) { diffractor };
                    var spots = S3Solver.OneMirrorSampled(withDiffractor, scenery, forbidden, R, 0.12f, 0.25f);
                    verdict = (mirrorAlone > 0 ? $"SHORTCUT: a mirror alone works ({mirrorAlone}); " : "") +
                              (diffractorAlone ? "SHORTCUT: the diffractor alone works; " : "") +
                              (spots.Count == 0 ? "NO MIRROR SPOT WORKS WITH THE DIFFRACTOR"
                              : $"with the diffractor at the sealed crystal's antipode, a mirror works {S3Solver.Runs(spots.Select(m => S3.Dist(m.position, laser) * R))} m from the laser");
                    break;
                }
                case S3Levels.Template.LongNight:
                {
                    var withDiffractor = new List<S3Laser.Block>(blocks) { diffractor };
                    int one = S3Solver.OneMirrorSampled(withDiffractor, scenery, forbidden, R, 0.25f, 1f).Count;
                    var pairs = S3Solver.TwoMirrorsInto(withDiffractor, scenery, forbidden, R, diffractor.position, diffractor.facing);
                    var off = pairs.Where(p => S3.Dist(p.second.position, -p.first.position) * R > 3f).ToList();
                    verdict = (one > 0 ? $"SHORTCUT with one mirror ({one}); " : "one mirror and the diffractor never work; ") +
                              (pairs.Count == 0 ? "NO TWO-MIRROR SOLUTION FOUND"
                              : off.Count == 0 ? $"{pairs.Count} two-mirror solutions, the second mirror always within {Metres(pairs.Max(p => S3.Dist(p.second.position, -p.first.position) * R))} of the first one's antipode"
                              : $"SHORTCUT: {off.Count} of {pairs.Count} two-mirror solutions don't hop half a world");
                    break;
                }
                default: verdict = "no shortcut check"; break;
            }
            log.AppendLine($"hole {i + 1} {level.title}: {verdict}");
        }
        world.SetBiome(original);
        Directory.CreateDirectory("Captures");
        File.WriteAllText("Captures/shortcuts.txt", log.ToString());
        Debug.Log(log.ToString());
        return log.ToString();
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
