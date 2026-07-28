using System.Collections;
using TMPro;
using UnityEngine;

// Loads the song selected in the main menu (SongLibrary.Instance), hands it
// to AudioPerSecondAnalyzer, then calls Analyze() + builder.Build(). The
// analyzer only runs itself in Start() if a clip is already assigned in the
// Inspector, so this script supplies it at runtime instead.
public class GameLevelSongLoader : MonoBehaviour
{
    public AudioPerSecondAnalyzer analyzer;

    [Tooltip("Used when no song was selected, e.g. testing this scene directly in the Editor.")]
    public AudioClip fallbackClipForTesting;

    public TMP_Text loadingText;

    private IEnumerator Start()
    {
        if (analyzer == null)
        {
            Debug.LogError("GameLevelSongLoader: no analyzer assigned.");
            yield break;
        }

        if (loadingText != null)
        {
            loadingText.gameObject.SetActive(true);
            loadingText.text = "Loading song...";
        }

        string path = SongLibrary.Instance != null ? SongLibrary.Instance.SelectedSongPath : null;

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("GameLevelSongLoader: no song selected (SongLibrary missing or nothing picked in the menu); using fallbackClipForTesting.");
            analyzer.clip = fallbackClipForTesting;
        }
        else
        {
            AudioClip loaded = null;
            yield return SongLibrary.Instance.LoadClip(path, clip => loaded = clip);
            analyzer.clip = loaded;
        }

        if (analyzer.clip == null)
        {
            if (loadingText != null) loadingText.text = "Couldn't load the selected song.";
            Debug.LogError("GameLevelSongLoader: no clip available to analyze.");
            yield break;
        }

        if (loadingText != null) loadingText.gameObject.SetActive(false);

        if (SongLibrary.Instance != null)
            analyzer.segmentDuration = SongLibrary.Instance.SelectedSegmentDuration;

        analyzer.Analyze();
        if (analyzer.Readings.Count > 0 && analyzer.builder != null)
            analyzer.builder.Build();
    }
}
