using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Central score/end-game authority. WaveBallController reports hit/miss per
// second via AddScore; this decides when the song ends and shows the end screen.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    public AudioPlaybackTracer tracer;
    public AudioBarChartBuilder chartBuilder;

    [Header("Scoring")]
    [Tooltip("Awarded when the ball is already in position when a point becomes current.")]
    public int perfectScore = 10;

    [Tooltip("Awarded when the ball catches up before the tracer moves to the next point.")]
    public int greatScore = 8;

    private bool maxScoreInitialized = false;

    [Header("Live HUD")]
    public Slider scoreSlider;

    [Header("Grade markers")]
    public GradeMarker[] gradeMarkers;

    [System.Serializable]
    public class GradeMarker
    {
        public string grade;
        public GameObject markerObject;
        public GameObject achievedHighlight;
        public float thresholdPercentOverride = -1f;
    }

    [Header("End Screen")]
    public GameObject endScreenPanel;
    public TextMeshProUGUI endGradeText;
    public TextMeshProUGUI endScoreText;
    public Button retryButton;
    public Button mainMenuButton;
    public string mainMenuSceneName = "MainMenu";

    [Header("Grade thresholds (percent)")]
    public float gradeAThreshold = 90f;
    public float gradeBThreshold = 75f;
    public float gradeCThreshold = 50f;

    public int TotalScore { get; private set; }
    public int MaxPossibleScore { get; private set; }
    public string CurrentGrade { get; private set; }
    public bool GameEnded { get; private set; }

    private void Awake()
    {
        Instance = this;
        if (endScreenPanel != null) endScreenPanel.SetActive(false);

        if (scoreSlider != null)
        {
            scoreSlider.interactable = false;
            scoreSlider.minValue = 0f;
            scoreSlider.maxValue = 1f;
            scoreSlider.value = 0f;
        }

        PositionGradeMarkers();
        ResetGradeHighlights();
    }

    private void Start()
    {
        if (retryButton != null) retryButton.onClick.AddListener(Retry);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
    }

    private void Update()
    {
        if (!GameEnded)
        {
            float percent = MaxPossibleScore > 0 ? (TotalScore / (float)MaxPossibleScore) * 100f : 0f;
            if (percent >= gradeAThreshold) CurrentGrade = "A";
            else if (percent >= gradeBThreshold) CurrentGrade = "B";
            else if (percent >= gradeCThreshold) CurrentGrade = "C";
            else CurrentGrade = "F";
            HighlightAchievedGrade();
        }

        if (!maxScoreInitialized && chartBuilder != null && chartBuilder.HasDisplayedReadings)
        {
            MaxPossibleScore = chartBuilder.DisplayedReadings.Count * perfectScore;
            maxScoreInitialized = true;
        }

        if (GameEnded || tracer == null || !tracer.IsStarted) return;
        if (tracer.audioSource == null || tracer.audioSource.clip == null) return;

        double songTime = tracer.SongTime;
        if (songTime >= tracer.audioSource.clip.length)
            EndGame();
    }

    public void AddScore(int gained)
    {
        if (GameEnded) return;

        TotalScore += gained;

        if (scoreSlider != null && MaxPossibleScore > 0)
            scoreSlider.value = (float)TotalScore / MaxPossibleScore;
    }

    private void EndGame()
    {
        GameEnded = true;

        if (scoreSlider != null && MaxPossibleScore > 0)
            scoreSlider.value = (float)TotalScore / MaxPossibleScore;

        HighlightAchievedGrade();

        if (endGradeText != null) endGradeText.text = CurrentGrade;
        if (endScoreText != null) endScoreText.text = $"{TotalScore}/{MaxPossibleScore}";
        if (endScreenPanel != null) endScreenPanel.SetActive(true);
    }

    public void Retry()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void HighlightAchievedGrade()
    {
        if (gradeMarkers == null) return;

        foreach (var marker in gradeMarkers)
        {
            if (marker == null || marker.achievedHighlight == null) continue;
            if (marker.grade == CurrentGrade) marker.achievedHighlight.SetActive(true);
        }
    }

    private void ResetGradeHighlights()
    {
        if (gradeMarkers == null) return;

        foreach (var marker in gradeMarkers)
        {
            if (marker != null && marker.achievedHighlight != null)
                marker.achievedHighlight.SetActive(false);
        }
    }

    private float GetThresholdPercent(GradeMarker marker)
    {
        if (marker.thresholdPercentOverride >= 0f) return marker.thresholdPercentOverride;

        switch (marker.grade.ToUpperInvariant())
        {
            case "A": return gradeAThreshold;
            case "B": return gradeBThreshold;
            case "C": return gradeCThreshold;
            default: return 0f;
        }
    }

    private void PositionGradeMarkers()
    {
        if (gradeMarkers == null || scoreSlider == null) return;

        foreach (var marker in gradeMarkers)
        {
            if (marker == null || marker.markerObject == null) continue;

            RectTransform markerRect = marker.markerObject.GetComponent<RectTransform>();
            RectTransform achieverRect = marker.achievedHighlight.GetComponent<RectTransform>();
            if (markerRect == null || achieverRect == null) continue;

            float t = Mathf.Clamp01(GetThresholdPercent(marker) / 100f);

            markerRect.anchorMin = new Vector2(t, markerRect.anchorMin.y);
            markerRect.anchorMax = new Vector2(t, markerRect.anchorMax.y);
            markerRect.anchoredPosition = new Vector2(0f, markerRect.anchoredPosition.y);
            achieverRect.anchorMin = new Vector2(t, achieverRect.anchorMin.y);
            achieverRect.anchorMax = new Vector2(t, achieverRect.anchorMax.y);
            achieverRect.anchoredPosition = new Vector2(0f, achieverRect.anchoredPosition.y);
        }
    }
}
