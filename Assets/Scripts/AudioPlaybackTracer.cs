using UnityEngine;

/// <summary>
/// Moves a sprite across the AudioBarChartBuilder's buildings in sync with
/// music playback, using Unity's audio-thread clock (AudioSettings.dspTime)
/// as the single source of truth for both starting playback and driving the
/// tracer's position. Logs a sync check every second so drift can be caught.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class AudioPlaybackTracer : MonoBehaviour
{
    [Header("Playback")]
    [Tooltip("The AudioSource that will play the same clip the bar chart was built from.")]
    public AudioSource audioSource;

    [Tooltip("Seconds to wait (from when tracing starts) before playback and tracing begin together.")]
    public double startDelay = 0.0;

    [Tooltip("If true, scheduling begins automatically in Start(). If false, call StartTracing() yourself (e.g. from AudioBarChartBuilder once the bars exist).")]
    public bool autoPlayOnStart = false;

    [Header("Bar chart reference")]
    [Tooltip("The builder that generated the buildings this sprite will trace over.")]
    public AudioBarChartBuilder chartBuilder;

    [Header("Motion")]
    [Tooltip("Extra height above the current bar's top, in world units.")]
    public float heightOffset = 0.5f;
    public float segmentDuration = 1f;

    [Tooltip("If true, the sprite jumps discretely to each bar's exact top on the second. If false, it smoothly interpolates position and height between consecutive seconds.")]
    public bool snapToWholeSeconds = false;

    [Header("Lane alignment")]
    [Tooltip("Manual X/Y nudge applied after computing the wave-based position. Useful for correcting a visual mismatch between this tracer's per-second height sampling and a smoothed wave line (e.g. AudioWaveBuilder's smoothCurve), so the tracer sits exactly on the lane instead of slightly off it.")]
    public Vector2 laneOffset = Vector2.zero;

    [Header("Blink effect")]
    [Tooltip("If true, the sprite only appears briefly each time it reaches a new second, then hides until the next one — a blinking pulse instead of staying continuously visible while it moves/teleports.")]
    public bool blinkMode = false;

    [Tooltip("How long the sprite stays visible after appearing at a new second, in milliseconds.")]
    public float blinkVisibleMs = 150f;

    [Header("Sync verification")]
    [Tooltip("Log a sync check line every second, comparing the dsp-clock song time against the AudioSource's actual sample position.")]
    public bool logSyncChecks = true;

    [Tooltip("Drift beyond this many seconds logs a warning instead of a normal log.")]
    public float syncToleranceSeconds = 0.05f;

    private SpriteRenderer spriteRenderer;
    private int totalSeconds;
    private double scheduledDspStartTime;
    private bool started = false;
    private int lastLoggedSecond = -1;
    private int lastBlinkSecond = -1;
    private float blinkElapsed = 0f;
    private bool blinkVisible = false;

    /// <summary>True once BeginSyncedPlayback/StartTracing has scheduled playback.</summary>
    public bool IsStarted => started;

    /// <summary>
    /// The same dsp-clock-derived song time driving this tracer's position.
    /// Other scripts (e.g. a scoring system) should read this instead of
    /// keeping their own timer, so everything stays on one clock. Returns
    /// -1 before playback has actually begun (still in the start delay).
    /// </summary>
    public double SongTime => started ? AudioSettings.dspTime - scheduledDspStartTime : -1;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        if (chartBuilder == null || chartBuilder.analyzer == null || chartBuilder.analyzer.Readings == null)
        {
            Debug.LogWarning("AudioPlaybackTracer: missing chartBuilder or analyzer readings.");
            enabled = false;
            return;
        }

        totalSeconds = chartBuilder.analyzer.Readings.Count;
        segmentDuration = chartBuilder.analyzer.segmentDuration;

        if (spriteRenderer != null) spriteRenderer.enabled = !blinkMode;

        if (autoPlayOnStart)
            StartTracing();
    }

    /// <summary>
    /// Convenience entry point for other scripts (e.g. AudioBarChartBuilder,
    /// once it has finished building the bars) to kick off synced playback
    /// using the configured startDelay.
    /// </summary>
    public void StartTracing()
    {
        if (chartBuilder != null && chartBuilder.analyzer != null && chartBuilder.analyzer.Readings != null)
        {
            totalSeconds = chartBuilder.analyzer.Readings.Count;
            segmentDuration = chartBuilder.analyzer.segmentDuration;
        }

        BeginSyncedPlayback(startDelay);
    }

    /// <summary>
    /// Schedules audio playback to start "delaySeconds" from now, using the
    /// dsp clock as the reference point for both playback and tracer motion.
    /// </summary>
    public void BeginSyncedPlayback(double delaySeconds)
    {
        if (audioSource == null)
        {
            Debug.LogWarning("AudioPlaybackTracer: no AudioSource assigned.");
            return;
        }

        double dspNow = AudioSettings.dspTime;
        scheduledDspStartTime = dspNow + delaySeconds;
        audioSource.PlayScheduled(scheduledDspStartTime);
        started = true;
        lastLoggedSecond = -1;
        lastBlinkSecond = -1;
        blinkVisible = false;
        if (spriteRenderer != null) spriteRenderer.enabled = !blinkMode;

        Debug.Log($"[Sync] Scheduled playback: dspTime now={dspNow:F3}s, will start at dspTime={scheduledDspStartTime:F3}s (delay={delaySeconds:F3}s).");
    }

    private void Update()
    {
        if (!started || totalSeconds == 0) return;

        double songTime = AudioSettings.dspTime - scheduledDspStartTime;
        if (songTime < 0) return;

        UpdatePosition(songTime);

        int currentSegment = Mathf.Clamp(Mathf.FloorToInt((float)(songTime / segmentDuration)), 0, totalSeconds - 1);

        if (blinkMode)
            HandleBlink(currentSegment);

        if (logSyncChecks && currentSegment != lastLoggedSecond)
        {
            lastLoggedSecond = currentSegment;
            LogSyncCheck(currentSegment, songTime);
        }
    }

    /// <summary>
    /// Shows the sprite the moment a new second is reached, then hides it
    /// again after blinkVisibleMs — a pulse rather than a continuously
    /// visible sprite gliding/teleporting between positions.
    /// </summary>
    private void HandleBlink(int currentSecond)
    {
        if (spriteRenderer == null) return;

        if (currentSecond != lastBlinkSecond)
        {
            lastBlinkSecond = currentSecond;
            blinkVisible = true;
            blinkElapsed = 0f;
            spriteRenderer.enabled = true;
            return;
        }

        if (blinkVisible)
        {
            blinkElapsed += Time.deltaTime;
            if (blinkElapsed * 1000f >= blinkVisibleMs)
            {
                blinkVisible = false;
                spriteRenderer.enabled = false;
            }
        }
    }

    private void UpdatePosition(double songTime)
    {
        int index = Mathf.Clamp(Mathf.FloorToInt((float)(songTime / segmentDuration)), 0, totalSeconds - 1);
        int nextIndex = Mathf.Clamp(index + 1, 0, totalSeconds - 1);
        float frac = Mathf.Clamp01((float)((songTime - index * segmentDuration) / segmentDuration));

        float xCurrent = index * chartBuilder.buildingSpacing;
        float xNext = nextIndex * chartBuilder.buildingSpacing;

        float heightCurrent = chartBuilder.groundY + chartBuilder.GetBlockCount(index) * chartBuilder.blockSize;
        float heightNext = chartBuilder.groundY + chartBuilder.GetBlockCount(nextIndex) * chartBuilder.blockSize;

        float x, y;
        if (snapToWholeSeconds)
        {
            x = xCurrent;
            y = heightCurrent;
        }
        else
        {
            x = Mathf.Lerp(xCurrent, xNext, frac);
            y = Mathf.Lerp(heightCurrent, heightNext, frac);
        }

        Vector3 pos = transform.position;
        pos.x = x + laneOffset.x;
        pos.y = y + heightOffset + laneOffset.y;
        transform.position = pos;
    }

    /// <summary>
    /// Compares the dsp-clock-derived song time (used to drive the tracer)
    /// against the AudioSource's own sample-accurate playback position, and
    /// logs the reading the tracer is currently pointing at. If both clocks
    /// agree within syncToleranceSeconds, this confirms the tracer is
    /// pointing at the correct second's bar.
    /// </summary>
    private void LogSyncCheck(int second, double songTime)
    {
        if (audioSource == null || audioSource.clip == null) return;

        double audioSampleTime = (double)audioSource.timeSamples / audioSource.clip.frequency;
        double drift = songTime - audioSampleTime;

        var reading = chartBuilder.analyzer.Readings[second];
        int blockCount = chartBuilder.GetBlockCount(second);
        string status = Mathf.Abs((float)drift) > syncToleranceSeconds ? "DESYNC" : "OK";

        string line = $"[SyncCheck] second={second} | dspSongTime={songTime:F3}s audioSampleTime={audioSampleTime:F3}s drift={drift * 1000:F1}ms | " +
                      $"reading: vol={reading.volumeDb}dB pitch={reading.pitchHz}Hz note={reading.noteName} blocks={blockCount} | tracerPos={transform.position} | {status}";

        if (status == "DESYNC")
            Debug.LogWarning(line);
        else
            Debug.Log(line);
    }
}