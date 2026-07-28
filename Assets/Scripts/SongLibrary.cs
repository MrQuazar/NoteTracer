using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// Source of truth for which mp3s exist and which is selected, shared between
// the main menu and gameplay scene via DontDestroyOnLoad. Songs live under
// Application.persistentDataPath/Songs: Standalone/Android use it as a real
// folder; WebGL has no OS folder, so songs come in via the Upload button
// (WebGLBridge) instead; the Editor uses EditorUtility's file panel.
public class SongLibrary : MonoBehaviour
{
    public static SongLibrary Instance { get; private set; }

    [Serializable]
    public class SongEntry
    {
        public string filePath;
        public string displayName;
    }

    private const string SelectedSongPrefKey = "SongLibrary_SelectedSongFileName";

    public List<SongEntry> Songs { get; private set; } = new List<SongEntry>();
    public string SelectedSongPath { get; private set; }

    public string SongsFolderPath => Path.Combine(Application.persistentDataPath, "Songs");

    public enum Difficulty { Easy, Medium, Hard }

    private const string DifficultyPrefKey = "SongLibrary_Difficulty";

    public Difficulty SelectedDifficulty { get; private set; } = Difficulty.Medium;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureFolderExists();
        RefreshSongList();
        RestoreLastSelectedSong();
    }

    private void EnsureFolderExists()
    {
        try
        {
            if (!Directory.Exists(SongsFolderPath))
                Directory.CreateDirectory(SongsFolderPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"SongLibrary: could not create songs folder '{SongsFolderPath}': {e.Message}");
        }
    }

    public void RefreshSongList()
    {
        Songs.Clear();
        EnsureFolderExists();

        string[] files;
        try
        {
            files = Directory.GetFiles(SongsFolderPath, "*.mp3", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e)
        {
            Debug.LogError($"SongLibrary: could not scan '{SongsFolderPath}': {e.Message}");
            return;
        }

        foreach (string path in files)
        {
            Songs.Add(new SongEntry
            {
                filePath = path,
                displayName = Path.GetFileNameWithoutExtension(path)
            });
        }

        Songs.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(SelectedSongPath) && !File.Exists(SelectedSongPath))
            SelectedSongPath = null;
    }

    public void SelectSong(string filePath)
    {
        SelectedSongPath = filePath;
        PlayerPrefs.SetString(SelectedSongPrefKey, Path.GetFileName(filePath));
        PlayerPrefs.Save();
    }

    private void RestoreLastSelectedSong()
    {
        string lastFileName = PlayerPrefs.GetString(SelectedSongPrefKey, string.Empty);
        if (string.IsNullOrEmpty(lastFileName)) return;

        foreach (var entry in Songs)
        {
            if (string.Equals(Path.GetFileName(entry.filePath), lastFileName, StringComparison.OrdinalIgnoreCase))
            {
                SelectedSongPath = entry.filePath;
                return;
            }
        }
        SelectedDifficulty = (Difficulty)PlayerPrefs.GetInt(DifficultyPrefKey, (int)Difficulty.Medium);
    }

    public void PromptUpload(string callbackTargetName, string callbackMethodName)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLBridge.OpenMp3FileDialog(callbackTargetName, callbackMethodName);
#elif UNITY_EDITOR
        string picked = UnityEditor.EditorUtility.OpenFilePanel("Select an mp3", SongsFolderPath, "mp3");
        if (!string.IsNullOrEmpty(picked))
            CopyFileIntoLibrary(picked);
#else
        Debug.Log("SongLibrary: no in-app file picker on this platform yet — opening the songs folder instead.");
        OpenSongsFolderInExplorer();
#endif
    }

    private void CopyFileIntoLibrary(string sourcePath)
    {
        if (!IsMp3FileName(sourcePath))
        {
            Debug.LogWarning($"SongLibrary: '{sourcePath}' isn't an .mp3, ignoring.");
            return;
        }

        string destPath = Path.Combine(SongsFolderPath, SanitizeFileName(Path.GetFileName(sourcePath)));

        try
        {
            File.Copy(sourcePath, destPath, overwrite: true);
        }
        catch (Exception e)
        {
            Debug.LogError($"SongLibrary: failed copying '{sourcePath}' into library: {e.Message}");
            return;
        }

        RefreshSongList();
        SelectSong(destPath);
    }

    // Entry point for the WebGL upload flow, called via SendMessage with
    // "filename.mp3|<base64>" once the browser file picker resolves.
    public void SaveUploadedSongFromBase64(string payload)
    {
        int separator = payload.IndexOf('|');
        if (separator < 0)
        {
            Debug.LogWarning("SongLibrary: malformed upload payload, ignoring.");
            return;
        }

        string fileName = payload.Substring(0, separator);
        string base64Data = payload.Substring(separator + 1);

        if (!IsMp3FileName(fileName))
        {
            Debug.LogWarning($"SongLibrary: rejected non-mp3 upload '{fileName}'.");
            return;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Data);
        }
        catch (Exception e)
        {
            Debug.LogError($"SongLibrary: bad upload payload for '{fileName}': {e.Message}");
            return;
        }

        string safeName = SanitizeFileName(fileName);
        string destPath = Path.Combine(SongsFolderPath, safeName);

        try
        {
            File.WriteAllBytes(destPath, bytes);
            WebGLBridge.SyncFileSystem();
        }
        catch (Exception e)
        {
            Debug.LogError($"SongLibrary: failed saving '{safeName}': {e.Message}");
            return;
        }

        RefreshSongList();
        SelectSong(destPath);
    }

    public void OpenSongsFolderInExplorer()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("SongLibrary: there's no OS folder to open in a browser build; use Upload instead.");
#else
        EnsureFolderExists();
        Application.OpenURL("file://" + SongsFolderPath);
#endif
    }

    // On Standalone/Android this fetches from "file://" directly; on WebGL
    // that scheme isn't fetchable from the sandbox, so bytes are read
    // locally and wrapped into a Blob URL instead.
    public IEnumerator LoadClip(string filePath, Action<AudioClip> onLoaded)
    {
        string url;

        if (WebGLBridge.IsAvailable)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"SongLibrary: failed reading '{filePath}': {e.Message}");
                onLoaded?.Invoke(null);
                yield break;
            }

            url = WebGLBridge.CreateBlobUrl(bytes, "audio/mpeg");
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError("SongLibrary: WebGLBridge.CreateBlobUrl returned nothing.");
                onLoaded?.Invoke(null);
                yield break;
            }
        }
        else
        {
            url = "file://" + filePath;
        }

        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool failed = request.result != UnityWebRequest.Result.Success;
#else
            bool failed = request.isNetworkError || request.isHttpError;
#endif
            if (failed)
            {
                Debug.LogError($"SongLibrary: failed to load clip '{filePath}': {request.error}");
                onLoaded?.Invoke(null);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            onLoaded?.Invoke(clip);
        }
    }

    private static bool IsMp3FileName(string fileName)
    {
        return !string.IsNullOrEmpty(fileName) &&
               fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return fileName;
    }

    public float SelectedSegmentDuration
    {
        get
        {
            switch (SelectedDifficulty)
            {
                case Difficulty.Easy: return 1f;
                case Difficulty.Medium: return 0.5f;
                case Difficulty.Hard: return 0.1f;
                default: return 0.5f;
            }
        }
    }

    public void SetDifficulty(Difficulty difficulty)
    {
        SelectedDifficulty = difficulty;
        PlayerPrefs.SetInt(DifficultyPrefKey, (int)difficulty);
        PlayerPrefs.Save();
    }
}
