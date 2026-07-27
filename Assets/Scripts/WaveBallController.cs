using TMPro;
using UnityEngine;

/// <summary>
/// Player-controlled ball: while the mouse button is held, the ball moves
/// toward the mouse's world position. Its Y is clamped to a corridor around
/// the wave curve (chartBuilder.GetHeightAtWorldX), so it can only travel
/// along the wave's path rather than anywhere on screen. Every second, its
/// distance to AudioPlaybackTracer is measured and scored, using the
/// tracer's own synced clock (SongTime) rather than a separate timer, so
/// scoring stays aligned with what's actually audible.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class WaveBallController : MonoBehaviour
{
    [Header("References")]
    public AudioBarChartBuilder chartBuilder;
    public AudioPlaybackTracer tracer;

    [Tooltip("Camera used to convert mouse screen position to world position. Defaults to Camera.main if left empty.")]
    public Camera cam;

    [Header("Movement")]
    [Tooltip("World units per second the ball moves toward the mouse while the mouse button is held.")]
    public float moveSpeed = 10f;

    [Tooltip("How far above/below the wave curve the ball is allowed to roam, in world units. The ball's Y is clamped to [waveY - pathHalfWidth, waveY + pathHalfWidth].")]
    public float pathHalfWidth = 1.5f;

    [Header("Scoring bands")]
    [Tooltip("Distance to the tracer at or under this earns scoreBandX (the top score).")]
    public float distanceBandX = 0.5f;
    public int scoreBandX = 100;

    [Tooltip("Distance at or under this (but over Band X) earns scoreBandY.")]
    public float distanceBandY = 1.0f;
    public int scoreBandY = 50;

    [Tooltip("Distance at or under this (but over Band Y) earns scoreBandZ. Anything further scores 0.")]
    public float distanceBandZ = 2.0f;
    public int scoreBandZ = 10;

    [Header("UI Display")]
    public TextMeshProUGUI score;
    public TextMeshProUGUI gains;

    public int TotalScore { get; private set; }
    public int MaxPossibleScore { get; private set; }
    public string FinalGrade { get; private set; }
    public bool SongFinished { get; private set; }

    private int lastScoredSecond = -1;

    private void Update()
    {
        if (chartBuilder == null || chartBuilder.analyzer == null || chartBuilder.analyzer.Readings == null) return;
        if (tracer == null || !tracer.IsStarted) return;
        if (cam == null) cam = Camera.main;

        HandleMouseMovement();
        ClampToPath();
        HandleScoring();
    }

    private void HandleMouseMovement()
    {
        if (cam == null) return;

        if (Input.GetMouseButton(0))
        {
            Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = transform.position.z;
            transform.position = Vector3.MoveTowards(transform.position, mouseWorld, moveSpeed * Time.deltaTime);
        }
    }

    private void ClampToPath()
    {
        float waveY = chartBuilder.GetHeightAtWorldX(transform.position.x);
        Vector3 pos = transform.position;
        pos.y = Mathf.Clamp(pos.y, waveY - pathHalfWidth, waveY + pathHalfWidth);
        transform.position = pos;
    }

    private void HandleScoring()
    {
        double songTime = tracer.SongTime;
        int totalSeconds = chartBuilder.analyzer.Readings.Count;

        if (songTime < 0) return;

        int currentSecond = Mathf.Clamp(Mathf.FloorToInt((float)songTime), 0, totalSeconds - 1);
        if (currentSecond != lastScoredSecond)
        {
            lastScoredSecond = currentSecond;
            ScoreSecond(currentSecond);
        }

        if (!SongFinished && songTime >= totalSeconds)
        {
            SongFinished = true;
            FinalizeGrade();
        }
    }

    private void ScoreSecond(int second)
    {
        float distance = Vector3.Distance(transform.position, tracer.transform.position);

        int gained;
        if (distance <= distanceBandX) gained = scoreBandX;
        else if (distance <= distanceBandY) gained = scoreBandY;
        else if (distance <= distanceBandZ) gained = scoreBandZ;
        else gained = 0;

        TotalScore += gained;
        MaxPossibleScore += scoreBandX;
        score.text =TotalScore +"/"+ MaxPossibleScore;
        gains.text = "+"+ gained;
        Debug.Log($"[Score] second={second} distance={distance:F2} gained={gained} totalScore={TotalScore}/{MaxPossibleScore}");
    }

    private void FinalizeGrade()
    {
        float percent = MaxPossibleScore > 0 ? (TotalScore / (float)MaxPossibleScore) * 100f : 0f;

        if (percent >= 90f) FinalGrade = "A";
        else if (percent >= 80f) FinalGrade = "B";
        else if (percent >= 70f) FinalGrade = "C";
        else if (percent >= 50f) FinalGrade = "D";
        else FinalGrade = "F";

        Debug.Log($"[FinalGrade] score={TotalScore}/{MaxPossibleScore} ({percent:F1}%) grade={FinalGrade}");
    }
}
