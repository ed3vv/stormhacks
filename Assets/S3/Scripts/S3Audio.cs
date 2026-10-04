using UnityEngine;

/// <summary>
/// Sound effects and music (clips in Assets/Audio). Everything plays in 2D: places in this world
/// are points on S^3, not Unity transforms, so there is nothing for 3D audio to pan by.
///
///   * hit: the club strikes the ball (louder with more power)
///   * land: the ball hits the ground, a wall or a piece (louder the harder it hits)
///   * click: placing or picking up a laser piece, addressing the ball, starting the power meter
///   * win + the crowd: a hole sunk (cheering under par, clapping at par, slow clapping over par),
///     a Desert puzzle solved or an Eternal Poles cup's ice gate opened by the laser (clapping)
///   * lose + boos: the ball went over or past the cup and carried on
///   * music: loops quietly, ducking under the jingles and the crowd
///
/// The static calls do nothing outside Play mode (the capture tool drives the game in edit mode).
/// </summary>
public class S3Audio : MonoBehaviour
{
    public static S3Audio Instance { get; private set; }

    [Header("Clips")]
    public AudioClip hit;
    public AudioClip land;
    public AudioClip click;
    public AudioClip win;
    public AudioClip lose;
    public AudioClip music;

    [Header("Crowd")]
    public AudioClip cheer;       // under par
    public AudioClip clapping;    // par, puzzles and gates
    public AudioClip slowClap;    // over par
    public AudioClip boo;         // a miss

    [Header("Volume")]
    [Range(0, 1)] public float sfxVolume = 0.8f;
    [Tooltip("Extra level for the club hit, on top of sfxVolume.")]
    [Range(0, 1)] public float hitVolume = 0.65f;
    [Range(0, 1)] public float jingleVolume = 0.9f;
    [Tooltip("Extra level for the win jingle, on top of jingleVolume.")]
    [Range(0, 1)] public float winVolume = 0.65f;
    [Range(0, 1)] public float crowdVolume = 0.8f;
    [Range(0, 1)] public float musicVolume = 0.3f;
    [Tooltip("Music level while a jingle plays, as a fraction of musicVolume.")]
    [Range(0, 1)] public float musicDuck = 0.3f;

    [Header("Ball impacts")]
    [Tooltip("Impacts slower than this (m/s) are silent.")]
    public float minImpact = 1f;
    [Tooltip("Impacts this fast (m/s) or faster play at full volume.")]
    public float loudImpact = 10f;

    AudioSource sfx, jingle, crowd, tune;
    float lastLand = -1;

    static S3Audio Active => Instance && Application.isPlaying ? Instance : null;

    void Awake()
    {
        Instance = this;
        sfx = NewSource(false);
        jingle = NewSource(false);
        crowd = NewSource(false);
        tune = NewSource(true);
        if (music)
        {
            tune.clip = music;
            tune.volume = musicVolume;
            tune.Play();
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    AudioSource NewSource(bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0;
        return s;
    }

    void Update()
    {
        // Unity stops every source when the audio device changes or the audio system restarts
        if (music && !tune.isPlaying)
        {
            tune.clip = music;
            tune.Play();
        }
        float target = musicVolume * (jingle.isPlaying || crowd.isPlaying ? musicDuck : 1f);
        tune.volume = Mathf.MoveTowards(tune.volume, target, Time.unscaledDeltaTime * 1.5f);
    }

    void OneShot(AudioClip clip, float volume)
    {
        if (clip) sfx.PlayOneShot(clip, sfxVolume * volume);
    }

    /// <summary>Win and lose (and their crowds) cut each other off, so a lucky rebound into the cup only cheers.</summary>
    void Jingle(AudioClip clip, float volume, AudioClip crowdClip)
    {
        Restart(jingle, clip, jingleVolume * volume);
        Restart(crowd, crowdClip, crowdVolume);
    }

    static void Restart(AudioSource source, AudioClip clip, float volume)
    {
        source.Stop();
        if (!clip) return;
        source.clip = clip;
        source.volume = volume;
        source.Play();
    }

    /// <summary>The club strikes the ball; power is the meter reading (0..1).</summary>
    public static void Hit(float power)
    {
        var a = Active;
        if (a) a.OneShot(a.hit, a.hitVolume * (0.45f + 0.55f * Mathf.Clamp01(power)));
    }

    /// <summary>The ball hits the ground or something solid at this speed (m/s).</summary>
    public static void Land(float speed)
    {
        var a = Active;
        if (!a || speed < a.minImpact || Time.time - a.lastLand < 0.08f) return;
        a.lastLand = Time.time;
        a.OneShot(a.land, Mathf.Lerp(0.25f, 1f, Mathf.InverseLerp(a.minImpact, a.loudImpact, speed)));
    }

    public static void Click()
    {
        var a = Active;
        if (a) a.OneShot(a.click, 0.7f);
    }

    /// <summary>A Desert puzzle solved or an Eternal Poles gate opened.</summary>
    public static void Win()
    {
        var a = Active;
        if (a) a.Jingle(a.win, a.winVolume, a.clapping);
    }

    /// <summary>The ball dropped: the crowd cheers under par, claps at par and slow-claps over it.</summary>
    public static void Sunk(int strokes, int par)
    {
        var a = Active;
        if (a) a.Jingle(a.win, a.winVolume, strokes < par || strokes == 1 ? a.cheer : strokes == par ? a.clapping : a.slowClap);
    }

    public static void Lose()
    {
        var a = Active;
        if (a) a.Jingle(a.lose, 1f, a.boo);
    }

#if UNITY_EDITOR
    void Reset() => AssignDefaultClips();

    /// <summary>Fills any empty clip slot from Assets/Audio (also used by S3Setup).</summary>
    public void AssignDefaultClips()
    {
        static AudioClip Load(string file) => UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/" + file);
        if (!hit) hit = Load("hitting_ball.mp3");
        if (!land) land = Load("ball_hit_ground.mp3");
        if (!click) click = Load("click.mp3");
        if (!win) win = Load("win_jingle.wav");
        if (!lose) lose = Load("lose_jingle.wav");
        if (!music) music = Load("tune.wav");
        if (!cheer) cheer = Load("yayyy.mp3");
        if (!clapping) clapping = Load("clapping.mp3");
        if (!slowClap) slowClap = Load("slow_clapping.mp3");
        if (!boo) boo = Load("booo.mp3");
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
