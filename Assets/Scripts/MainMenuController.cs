using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Main menu: Play / Quit / Volume, plus song selection and management.
// SongLibrary owns the file-system/platform work; this wires UI to it.
public class MainMenuController : MonoBehaviour
{
    [Header("Scene flow")]
    public string gameSceneName = "Game";
    public Button playButton;
    public Button quitButton;
    public TMP_Text playHintText;

    [Header("Volume")]
    public Slider volumeSlider;
    private const string VolumePrefKey = "MasterVolume";

    [Header("Song panel")]
    public GameObject songPanel;
    public Button openLibraryButton;
    public Button closePanelButton;

    [Header("Song selector")]
    public Transform songListContent;
    public GameObject songRowPrefab;
    public TMP_Text selectedSongLabel;
    public TMP_Text songsFolderPathLabel;

    [Header("Song management")]
    [Tooltip("WebGL opens the browser file picker; Editor opens a file panel; Standalone/Android opens the songs folder.")]
    public Button uploadButton;
    public Button refreshButton;
    [Tooltip("Auto-hidden on WebGL, where there's no OS folder to open.")]
    public Button openFolderButton;
    public Button copyFolderPathButton;

    [Header("Difficulty")]
    [Tooltip("Options must be in order: Easy, Medium, Hard.")]
    public TMP_Dropdown difficultyDropdown;

    private readonly List<GameObject> spawnedRows = new List<GameObject>();

    private void Awake()
    {
        // SongLibrary persists across the menu -> game scene hop; create it
        // here if nothing has placed one yet.
        if (SongLibrary.Instance == null)
        {
            var go = new GameObject("SongLibrary");
            go.AddComponent<SongLibrary>();
        }
    }

    private void Start()
    {
        if (songPanel != null) songPanel.SetActive(false);
        SetupVolumeSlider();
        SetupButtons();

        if (openFolderButton != null)
            openFolderButton.gameObject.SetActive(!WebGLBridge.IsAvailable);

        if (songsFolderPathLabel != null)
        {
            songsFolderPathLabel.text = WebGLBridge.IsAvailable
                ? "Use Upload to add mp3s."
                : $"Drop .mp3 files here:\n{SongLibrary.Instance.SongsFolderPath}";
        }

        if (playHintText != null) playHintText.gameObject.SetActive(false);

        if (difficultyDropdown != null)
        {
            difficultyDropdown.value = (int)SongLibrary.Instance.SelectedDifficulty;
            difficultyDropdown.onValueChanged.AddListener(OnDifficultyChanged);
        }

        RefreshUI();
    }

    private void OnDifficultyChanged(int index)
    {
        SongLibrary.Instance.SetDifficulty((SongLibrary.Difficulty)index);
    }

    private void SetupVolumeSlider()
    {
        if (volumeSlider == null) return;

        float saved = PlayerPrefs.GetFloat(VolumePrefKey, 1f);
        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.value = saved;
        AudioListener.volume = saved;
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
    }

    private void OnVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat(VolumePrefKey, value);
    }

    private void SetupButtons()
    {
        if (playButton != null) playButton.onClick.AddListener(OnPlayPressed);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitPressed);
        if (uploadButton != null) uploadButton.onClick.AddListener(OnUploadPressed);
        if (refreshButton != null) refreshButton.onClick.AddListener(RefreshUI);
        if (openLibraryButton != null) openLibraryButton.onClick.AddListener(() => songPanel.SetActive(true));
        if (closePanelButton != null) closePanelButton.onClick.AddListener(() => songPanel.SetActive(false));
        if (openFolderButton != null) openFolderButton.onClick.AddListener(() => SongLibrary.Instance.OpenSongsFolderInExplorer());
        if (copyFolderPathButton != null)
            copyFolderPathButton.onClick.AddListener(() =>
                GUIUtility.systemCopyBuffer = SongLibrary.Instance.SongsFolderPath);
    }

    private void OnPlayPressed()
    {
        if (string.IsNullOrEmpty(SongLibrary.Instance.SelectedSongPath))
        {
            if (playHintText != null)
            {
                playHintText.text = "Pick a song first!";
                playHintText.gameObject.SetActive(true);
            }
            Debug.LogWarning("MainMenuController: no song selected, pick one before pressing Play.");
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    private void OnQuitPressed()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnUploadPressed()
    {
        SongLibrary.Instance.PromptUpload(gameObject.name, nameof(OnSongUploaded));
    }

    // WebGL: invoked by the browser plugin via SendMessage once a file is
    // picked, as "filename.mp3|<base64>".
    public void OnSongUploaded(string base64Payload)
    {
        SongLibrary.Instance.SaveUploadedSongFromBase64(base64Payload);
        RefreshUI();
    }

    public void RefreshUI()
    {
        SongLibrary.Instance.RefreshSongList();
        RebuildSongRows();
    }

    private void RebuildSongRows()
    {
        foreach (var row in spawnedRows)
            if (row != null) Destroy(row);
        spawnedRows.Clear();

        if (songListContent == null || songRowPrefab == null) return;

        var songs = SongLibrary.Instance.Songs;
        for (int i = 0; i < songs.Count; i++)
        {
            var entry = songs[i];
            GameObject row = Instantiate(songRowPrefab, songListContent);
            spawnedRows.Add(row);

            TMP_Text label = row.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = entry.displayName;

            Button button = row.GetComponent<Button>();
            if (button == null) button = row.GetComponentInChildren<Button>();
            if (button != null)
            {
                string path = entry.filePath;
                string displayName = entry.displayName;
                button.onClick.AddListener(() => OnSongRowClicked(path, displayName));
            }
        }

        UpdateSelectedSongLabel();
    }

    private void OnSongRowClicked(string path, string displayName)
    {
        SongLibrary.Instance.SelectSong(path);
        UpdateSelectedSongLabel();
        if (playHintText != null) playHintText.gameObject.SetActive(false);
    }

    private void UpdateSelectedSongLabel()
    {
        if (selectedSongLabel == null) return;

        string selectedPath = SongLibrary.Instance.SelectedSongPath;
        selectedSongLabel.text = string.IsNullOrEmpty(selectedPath)
            ? "No song selected"
            : $"Selected: {Path.GetFileNameWithoutExtension(selectedPath)}";
    }
}
