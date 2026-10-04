using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Two graphics modes, switched in game (G, or the pause menu) and remembered in PlayerPrefs:
///   * Quality: render scale 1.5 (supersampled) with 4x MSAA and SMAA on the camera.
///   * Performance: render scale 1.0 with 4x MSAA only, about 2x the frame rate.
/// The S3 shaders don't use Unity's shadow maps, so the render scale is what costs: 1.5 draws 2.25x the pixels.
/// The pipeline asset is shared, so the editor's original render scale is put back on exit (see Restore).
/// </summary>
public static class S3Graphics
{
    public enum Mode { Quality, Performance }

    const string Key = "s3.graphics";
    const float QualityScale = 1.5f, PerformanceScale = 1f;
    static float originalScale = -1;

    public static Mode Current
    {
        get => (Mode)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Mode.Performance), 0, 1);
        private set { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); }
    }

    public static string Name(Mode m) => m == Mode.Quality ? "Quality" : "Performance";

    static UniversalRenderPipelineAsset Pipeline =>
        (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;

    /// <summary>Set the pipeline and this camera up for the current mode.</summary>
    public static void Apply(Camera cam)
    {
        var rp = Pipeline;
        if (rp != null)
        {
            if (originalScale < 0) originalScale = rp.renderScale;
            rp.renderScale = Current == Mode.Quality ? QualityScale : PerformanceScale;
        }
        if (cam && cam.TryGetComponent(out UniversalAdditionalCameraData data))
        {
            data.antialiasing = Current == Mode.Quality ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            data.antialiasingQuality = AntialiasingQuality.High;
        }
    }

    public static Mode Toggle(Camera cam)
    {
        Current = Current == Mode.Quality ? Mode.Performance : Mode.Quality;
        Apply(cam);
        return Current;
    }

    /// <summary>Put the pipeline asset's render scale back (in the editor it would otherwise stay changed on disk).</summary>
    public static void Restore()
    {
        var rp = Pipeline;
        if (rp != null && originalScale >= 0) rp.renderScale = originalScale;
        originalScale = -1;
    }
}
