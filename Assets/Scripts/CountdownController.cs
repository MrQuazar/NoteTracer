using TMPro;
using UnityEngine;

// Shows a pulsating "3, 2, 1" before playback starts. Kicks off
// AudioPlaybackTracer's scheduled playback itself (tracer.autoPlayOnStart
// should stay unchecked) and reads time straight off tracer.SongTime, which
// is already negative during the countdown.
public class CountdownController : MonoBehaviour
{
    [Header("References")]
    public AudioPlaybackTracer tracer;
    public TextMeshProUGUI countdownText;

    [Header("Countdown")]
    [Tooltip("tracer.startDelay is forced to at least this so the two stay in sync.")]
    public int countFrom = 3;

    [Header("Pulsate")]
    public float punchScale = 1.6f;
    public float punchEaseDuration = 0.35f;
    public float idlePulseAmplitude = 0.08f;
    public float idlePulseSpeed = 6f;

    private int lastShownNumber = int.MinValue;
    private float punchElapsed = 0f;

    private void Start()
    {
        if (tracer == null)
        {
            Debug.LogWarning("CountdownController: no tracer assigned.");
            enabled = false;
        }

        // Not auto-started here: BeginCountdown() is called by
        // AudioBarChartBuilder.Build() once the song has actually loaded and
        // the bars/wave exist.
    }

    // Called by AudioBarChartBuilder.Build() once bars/wave exist.
    public void BeginCountdown()
    {
        if (tracer == null) return;

        tracer.startDelay = Mathf.Max((float)tracer.startDelay, countFrom);

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.transform.localScale = Vector3.one;
        }

        tracer.StartTracing();
    }

    private void Update()
    {
        if (tracer == null || !tracer.IsStarted || countdownText == null) return;

        double songTime = tracer.SongTime;

        if (songTime >= 0)
        {
            if (countdownText.gameObject.activeSelf)
                countdownText.gameObject.SetActive(false);
            return;
        }

        int secondsLeft = Mathf.Clamp(Mathf.CeilToInt((float)(-songTime)), 1, countFrom);

        if (secondsLeft != lastShownNumber)
        {
            lastShownNumber = secondsLeft;
            countdownText.text = secondsLeft.ToString();
            punchElapsed = 0f;
        }

        countdownText.transform.localScale = Vector3.one * ComputePulseScale();
    }

    private float ComputePulseScale()
    {
        punchElapsed += Time.deltaTime;

        float punchT = Mathf.Clamp01(punchElapsed / punchEaseDuration);
        float eased = 1f - (1f - punchT) * (1f - punchT);
        float punch = Mathf.Lerp(punchScale, 1f, eased);

        float idle = 1f + Mathf.Sin(punchElapsed * idlePulseSpeed) * idlePulseAmplitude * punchT;

        return punchT >= 1f ? idle : punch;
    }
}
