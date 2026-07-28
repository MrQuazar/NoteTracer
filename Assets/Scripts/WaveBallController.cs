using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Player-controlled ball: X always tracks AudioPlaybackTracer's X; the
// player controls height by clicking/tapping/dragging anywhere on screen.
// Input snaps to the nearest valid block row, with a forgiveness window so
// landing adjacent to the correct row still counts. Each second the ball's
// height is compared to the tracer's using the tracer's synced clock: a
// match within matchDistance scores, anything else scores 0.
[RequireComponent(typeof(SpriteRenderer))]
public class WaveBallController : MonoBehaviour
{
    [Header("References")]
    public AudioBarChartBuilder chartBuilder;
    public AudioPlaybackTracer tracer;

    [Tooltip("Camera the bars are viewed through. Defaults to Camera.main.")]
    public Camera gameCamera;

    [Header("Screen height control")]
    public float heightMoveSpeed = 15f;

    [Header("Snapping assist")]
    public bool snapToRows = true;

    [Tooltip("If the snapped row is within this many blocks of the currently correct row, snap to the correct row instead. 0 disables forgiveness.")]
    public int forgivenessBlocks = 1;

    [Header("Scoring")]
    [Tooltip("Vertical distance to the tracer at or under this counts as a match; otherwise scores 0.")]
    public float matchDistance = 0.6f;
    public int scoreMatch = 10;

    [Header("Feedback sprites")]
    public Image gainsImage;
    public Sprite greatSprite;
    public Sprite perfectSprite;
    public float gainsSpriteDuration = 0.6f;
    public float jiggleAmount = 0.25f;
    public float jiggleSpeed = 18f;

    private Coroutine gainsRoutine;
    private float targetHeightY;
    private bool targetInitialized = false;
    private readonly HashSet<int> scoredDisplayedIndices = new HashSet<int>();
    private int lastCheckedDisplayedIndex = -1;
    private readonly HashSet<int> catchupRequiredIndices = new HashSet<int>();

    private void Update()
    {
        if (chartBuilder == null || chartBuilder.analyzer == null || chartBuilder.analyzer.Readings == null) return;
        if (tracer == null || !tracer.IsStarted) return;
        if (!chartBuilder.HasDisplayedReadings) return;

        if (gameCamera == null) gameCamera = Camera.main;
        if (gameCamera == null) return;

        if (!targetInitialized)
        {
            targetHeightY = transform.position.y;
            targetInitialized = true;
        }

        HandleScreenInput();
        FollowTracerX();
        MoveTowardTargetHeight();
        HandleScoring();
    }

    private void HandleScreenInput()
    {
        if (Input.touchCount > 0)
        {
            UpdateTargetHeightFromScreenY(Input.GetTouch(0).position.y);
        }
        else if (Input.GetMouseButton(0))
        {
            UpdateTargetHeightFromScreenY(Input.mousePosition.y);
        }
    }

    // Projects the chart's world-space min/max block heights into screen
    // space (at the ball's own X/Z) and normalizes the tap against that
    // range, so wherever the bars sit on screen maps correctly regardless
    // of how much of the screen they occupy.
    private void UpdateTargetHeightFromScreenY(float screenY)
    {
        if (chartBuilder == null || gameCamera == null) return;

        float minY = chartBuilder.groundY + chartBuilder.minBlocks * chartBuilder.blockSize;
        float maxY = chartBuilder.groundY + chartBuilder.maxBlocks * chartBuilder.blockSize;

        float screenYAtMin = gameCamera.WorldToScreenPoint(new Vector3(transform.position.x, minY, transform.position.z)).y;
        float screenYAtMax = gameCamera.WorldToScreenPoint(new Vector3(transform.position.x, maxY, transform.position.z)).y;

        float normalized = Mathf.Abs(screenYAtMax - screenYAtMin) > 0.0001f
            ? Mathf.Clamp01(Mathf.InverseLerp(screenYAtMin, screenYAtMax, screenY))
            : 0f;
        float rawY = Mathf.Lerp(minY, maxY, normalized);

        int snappedBlocks = chartBuilder.minBlocks;
        if (snapToRows)
        {
            float rowsAboveGround = (rawY - chartBuilder.groundY) / chartBuilder.blockSize;
            snappedBlocks = Mathf.Clamp(Mathf.RoundToInt(rowsAboveGround), chartBuilder.minBlocks, chartBuilder.maxBlocks);

            if (forgivenessBlocks > 0 && TryGetCorrectBlockCount(out int correctBlocks))
            {
                if (Mathf.Abs(snappedBlocks - correctBlocks) <= forgivenessBlocks)
                    snappedBlocks = correctBlocks;
            }

            rawY = chartBuilder.groundY + snappedBlocks * chartBuilder.blockSize;
        }

        float heightOffset = tracer != null ? tracer.heightOffset : 0f;
        float laneOffsetY = tracer != null ? tracer.laneOffset.y : 0f;
        targetHeightY = rawY + heightOffset + laneOffsetY;
    }

    private bool TryGetCorrectBlockCount(out int blockCount)
    {
        blockCount = 0;
        if (tracer == null || !tracer.IsStarted) return false;

        double songTime = tracer.SongTime;
        if (songTime < 0) return false;
        if (!chartBuilder.TryGetCurrentDisplayedReading((float)songTime, out var reading, out _)) return false;

        blockCount = chartBuilder.GetBlockCountForReading(reading);
        return true;
    }

    private void FollowTracerX()
    {
        Vector3 pos = transform.position;
        pos.x = tracer.transform.position.x;
        transform.position = pos;
    }

    private void MoveTowardTargetHeight()
    {
        Vector3 pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetHeightY, heightMoveSpeed * Time.deltaTime);
        transform.position = pos;
    }

    private void HandleScoring()
    {
        if (GameManager.Instance != null && GameManager.Instance.GameEnded) return;
        if (tracer.SongTime < 0) return;

        if (!chartBuilder.TryGetCurrentDisplayedReading((float)tracer.SongTime, out _, out int displayedIndex))
            return;

        if (scoredDisplayedIndices.Contains(displayedIndex)) return;

        float distance = Mathf.Abs(transform.position.y - tracer.transform.position.y);
        bool isMatched = distance <= matchDistance;

        if (displayedIndex != lastCheckedDisplayedIndex)
        {
            lastCheckedDisplayedIndex = displayedIndex;
            if (!isMatched)
            {
                catchupRequiredIndices.Add(displayedIndex);
                return;
            }
        }
        else if (!isMatched)
        {
            return;
        }

        bool neededCatchup = catchupRequiredIndices.Contains(displayedIndex);
        int gained = neededCatchup ? GameManager.Instance.greatScore : GameManager.Instance.perfectScore;
        string result = neededCatchup ? "great" : "perfect";

        scoredDisplayedIndices.Add(displayedIndex);
        catchupRequiredIndices.Remove(displayedIndex);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddScore(gained);
        }

        ShowGainSprite(result);

        Debug.Log($"[Score] displayedIndex={displayedIndex} result={result} gained={gained} distance={distance:F2}");
    }

    private void ShowGainSprite(string result)
    {
        if (gainsImage == null) return;

        Sprite sprite = result == "perfect" ? perfectSprite : greatSprite;
        if (sprite == null) return;

        gainsImage.sprite = sprite;
        gainsImage.enabled = true;

        if (gainsRoutine != null) StopCoroutine(gainsRoutine);
        gainsRoutine = StartCoroutine(JiggleAndHide());
    }

    private System.Collections.IEnumerator JiggleAndHide()
    {
        float elapsed = 0f;
        Vector3 baseScale = Vector3.one;
        gainsImage.rectTransform.localScale = baseScale;

        while (elapsed < gainsSpriteDuration)
        {
            elapsed += Time.deltaTime;
            float damp = 1f - (elapsed / gainsSpriteDuration);
            float wiggle = 1f + Mathf.Sin(elapsed * jiggleSpeed) * jiggleAmount * damp;
            gainsImage.rectTransform.localScale = baseScale * wiggle;
            yield return null;
        }

        gainsImage.enabled = false;
        gainsImage.rectTransform.localScale = baseScale;
    }
}
