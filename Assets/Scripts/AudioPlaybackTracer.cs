using UnityEngine;

// Moves a sprite across AudioBarChartBuilder's buildings in sync with
// playback, using AudioSettings.dspTime as the shared clock for both
// starting playback and driving position. Follows chartBuilder's
// DisplayedReadings, so motion holds/interpolates smoothly across any gap
// left by dropped readings.
[RequireComponent(typeof(SpriteRenderer))]
public class AudioPlaybackTracer : MonoBehaviour
{
    [Header("Playback")]
    public AudioSource audioSource;

    [Tooltip("Seconds to wait after StartTracing() before playback and tracing begin together.")]
    public double startDelay = 0.0;

    [Tooltip("If false, call StartTracing() yourself instead of starting in Start().")]
    public bool autoPlayOnStart = false;

    [Header("Bar chart reference")]
    public AudioBarChartBuilder chartBuilder;

    [Header("Motion")]
    public float heightOffset = 0.5f;

    [Tooltip("If true, the sprite jumps to each displayed bar and holds. If false, it interpolates position/height between bars.")]
    public bool snapToWholeSeconds = false;

    [Header("Lane alignment")]
    [Tooltip("Manual X/Y nudge to correct mismatch against a smoothed wave line (e.g. AudioWaveBuilder).")]
    public Vector2 laneOffset = Vector2.zero;

    [Header("Blink effect")]
    [Tooltip("If true, the sprite only appears briefly at each new displayed bar instead of staying visible.")]
    public bool blinkMode = false;
    public float blinkVisibleMs = 150f;

    [Header("Sync verification")]
    [Tooltip("Logs the dsp-clock song time against the AudioSource's sample position at each new displayed bar.")]
    public bool logSyncChecks = true;
    public float syncToleranceSeconds = 0.05f;

    private SpriteRenderer spriteRenderer;
    private double scheduledDspStartTime;
    private bool started = false;
    private int lastLoggedDisplayedIndex = -1;
    private int lastBlinkDisplayedIndex = -1;
    private float blinkElapsed = 0f;
    private bool blinkVisible = false;

    public bool IsStarted => started;

    // -1 before playback has begun (still in the start delay).
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

        if (spriteRenderer != null) spriteRenderer.enabled = !blinkMode;

        if (autoPlayOnStart)
            StartTracing();
    }

    public void StartTracing()
    {
        BeginSyncedPlayback(startDelay);
    }

    public void BeginSyncedPlayback(double delaySeconds)
    {
        if (audioSource == null)
        {
            Debug.LogWarning("AudioPlaybackTracer: no AudioSource assigned.");
            return;
        }

        if (chartBuilder != null && chartBuilder.analyzer != null && chartBuilder.analyzer.clip != null)
            audioSource.clip = chartBuilder.analyzer.clip;

        double dspNow = AudioSettings.dspTime;
        scheduledDspStartTime = dspNow + delaySeconds;
        audioSource.PlayScheduled(scheduledDspStartTime);
        started = true;
        lastLoggedDisplayedIndex = -1;
        lastBlinkDisplayedIndex = -1;
        blinkVisible = false;
        if (spriteRenderer != null) spriteRenderer.enabled = !blinkMode;

        Debug.Log($"[Sync] Scheduled playback: dspTime now={dspNow:F3}s, will start at dspTime={scheduledDspStartTime:F3}s (delay={delaySeconds:F3}s).");
    }

    private void Update()
    {
        if (!started || chartBuilder == null || !chartBuilder.HasDisplayedReadings) return;

        double songTime = AudioSettings.dspTime - scheduledDspStartTime;
        if (songTime < 0) return;

        UpdatePosition(songTime);

        if (!chartBuilder.TryGetCurrentDisplayedReading((float)songTime, out var reading, out int displayedIndex))
            return;

        if (blinkMode)
            HandleBlink(displayedIndex);

        if (logSyncChecks && displayedIndex != lastLoggedDisplayedIndex)
        {
            lastLoggedDisplayedIndex = displayedIndex;
            LogSyncCheck(displayedIndex, reading, songTime);
        }
    }

    private void HandleBlink(int displayedIndex)
    {
        if (spriteRenderer == null) return;

        if (displayedIndex != lastBlinkDisplayedIndex)
        {
            lastBlinkDisplayedIndex = displayedIndex;
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
        float x, y;

        if (snapToWholeSeconds)
        {
            if (chartBuilder.TryGetCurrentDisplayedReading((float)songTime, out var reading, out _))
            {
                x = chartBuilder.GetWorldXAtTime(reading.timeSeconds);
                y = chartBuilder.groundY + chartBuilder.GetBlockCountForReading(reading) * chartBuilder.blockSize;
            }
            else
            {
                x = chartBuilder.GetWorldXAtTime((float)songTime);
                y = chartBuilder.groundY;
            }
        }
        else
        {
            x = chartBuilder.GetWorldXAtTime((float)songTime);
            y = chartBuilder.GetHeightAtTime((float)songTime);
        }

        Vector3 pos = transform.position;
        pos.x = x + laneOffset.x;
        pos.y = y + heightOffset + laneOffset.y;
        transform.position = pos;
    }

    private void LogSyncCheck(int displayedIndex, AudioPerSecondAnalyzer.SecondReading reading, double songTime)
    {
        if (audioSource == null || audioSource.clip == null) return;

        double audioSampleTime = (double)audioSource.timeSamples / audioSource.clip.frequency;
        double drift = songTime - audioSampleTime;

        int blockCount = chartBuilder.GetBlockCountForReading(reading);
        string status = Mathf.Abs((float)drift) > syncToleranceSeconds ? "DESYNC" : "OK";

        string line = $"[SyncCheck] displayedIndex={displayedIndex} rawSecond={reading.second} readingTime={reading.timeSeconds:F2}s | dspSongTime={songTime:F3}s audioSampleTime={audioSampleTime:F3}s drift={drift * 1000:F1}ms | " +
                      $"reading: vol={reading.volumeDb}dB pitch={reading.pitchHz}Hz note={reading.noteName} blocks={blockCount} | tracerPos={transform.position} | {status}";

        if (status == "DESYNC")
            Debug.LogWarning(line);
        else
            Debug.Log(line);
    }
}
