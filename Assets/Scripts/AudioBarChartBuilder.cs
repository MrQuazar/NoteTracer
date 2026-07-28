using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Builds a bar-chart from AudioPerSecondAnalyzer readings: one stacked-block
// "building" per surviving reading, positioned by real timeSeconds so dropped
// readings leave a gap instead of bunching the remaining bars together.
public class AudioBarChartBuilder : MonoBehaviour
{
    public enum Metric { Volume, Pitch, Note }

    [Header("Data source")]
    public AudioPerSecondAnalyzer analyzer;
    public AudioPlaybackTracer playback;
    public AudioWaveBuilder waveBuilder;

    [Tooltip("If set, Build() hands off to this instead of calling playback.StartTracing() directly.")]
    public CountdownController countdownController;

    [Header("Prefab & layout")]
    [Tooltip("Square 2D prefab with a BoxCollider2D. One or more are stacked per building.")]
    public GameObject blockPrefab;
    public Transform container;
    public float blockSize = 1f;
    public float buildingSpacing = 1.2f;
    public float groundY = 0f;

    [Header("Metric mapping")]
    public Metric metric = Metric.Volume;
    public int minBlocks = 1;
    public int maxBlocks = 20;
    public float volumeDbFloor = -60f;
    public float volumeDbCeiling = 0f;
    public float pitchHzFloor = 55f;
    public float pitchHzCeiling = 1500f;

    [Header("Note metric (MIDI-based, song-independent)")]
    public int midiNoteFloor = 33;
    public int midiNoteCeiling = 91;

    [Header("Peak markers")]
    [Tooltip("Placed at each displayed reading's exact tracer-landing spot.")]
    public GameObject peakPrefab;
    public Transform peakContainer;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<GameObject> spawnedPeaks = new List<GameObject>();

    // Subset of analyzer.Readings actually turned into bars (duplicates and
    // no-note readings excluded), ordered by timeSeconds.
    public List<AudioPerSecondAnalyzer.SecondReading> DisplayedReadings { get; private set; }
        = new List<AudioPerSecondAnalyzer.SecondReading>();

    public bool HasDisplayedReadings => DisplayedReadings != null && DisplayedReadings.Count > 0;

    [ContextMenu("Build Bar Chart")]
    public void Build()
    {
        if (analyzer == null || analyzer.Readings == null || analyzer.Readings.Count == 0)
        {
            Debug.LogWarning("AudioBarChartBuilder: analyzer has no readings. Call AudioPerSecondAnalyzer.Analyze() first.");
            return;
        }

        if (blockPrefab == null)
        {
            Debug.LogWarning("AudioBarChartBuilder: no blockPrefab assigned.");
            return;
        }

        Clear();

        if (container == null)
        {
            GameObject containerGo = new GameObject("BarChartContainer");
            containerGo.transform.SetParent(transform, false);
            container = containerGo.transform;
        }

        DisplayedReadings = FilterReadings(analyzer.Readings);

        for (int i = 0; i < DisplayedReadings.Count; i++)
        {
            var reading = DisplayedReadings[i];
            int height = ValueToBlockCount(reading);
            float x = GetWorldXAtTime(reading.timeSeconds);

            for (int b = 0; b < height; b++)
            {
                float y = groundY + b * blockSize + blockSize * 0.5f;
                GameObject block = Instantiate(blockPrefab, new Vector3(x, y, 0f), Quaternion.identity, container);
                block.name = $"Block_s{reading.second}_{b}";
                spawned.Add(block);

                if (b == height - 1)
                {
                    var tmp = block.GetComponentInChildren<TMP_Text>();
                    if (tmp != null) tmp.text = reading.noteName;
                }
            }
        }

        Debug.Log($"AudioBarChartBuilder: {analyzer.Readings.Count} readings -> {DisplayedReadings.Count} displayed buildings ({spawned.Count} blocks total) after dropping duplicates/no-note segments.");

        BuildPeakMarkers();

        if (DisplayedReadings.Count > 0 && playback != null)
        {
            if (waveBuilder != null) waveBuilder.Build();

            if (countdownController != null) countdownController.BeginCountdown();
            else playback.StartTracing();
        }
    }

    // Keeps a reading if it has a note and isn't an exact repeat of the last
    // *kept* reading, so a whole run of duplicates/no-note readings collapses
    // to zero bars.
    private static List<AudioPerSecondAnalyzer.SecondReading> FilterReadings(
        List<AudioPerSecondAnalyzer.SecondReading> readings)
    {
        var result = new List<AudioPerSecondAnalyzer.SecondReading>(readings.Count);

        for (int i = 0; i < readings.Count; i++)
        {
            var r = readings[i];

            bool hasNote = r.midiNote >= 0 && !string.IsNullOrEmpty(r.noteName);
            if (!hasNote) continue;

            if (result.Count > 0)
            {
                var prevKept = result[result.Count - 1];
                bool isDuplicate = prevKept.midiNote == r.midiNote
                    && Mathf.Approximately(prevKept.volumeDb, r.volumeDb)
                    && Mathf.Approximately(prevKept.pitchHz, r.pitchHz);
                if (isDuplicate) continue;
            }

            result.Add(r);
        }

        return result;
    }

    // Reads the tracer's offsets directly rather than duplicating them, so
    // markers can't drift out of alignment with where the tracer lands.
    private void BuildPeakMarkers()
    {
        if (peakPrefab == null) return;

        ClearPeaks();

        if (peakContainer == null)
        {
            GameObject peakContainerGo = new GameObject("PeakContainer");
            peakContainerGo.transform.SetParent(transform, false);
            peakContainer = peakContainerGo.transform;
        }

        Vector2 laneOffset = playback != null ? playback.laneOffset : Vector2.zero;
        float heightOffset = playback != null ? playback.heightOffset : 0f;

        for (int i = 0; i < DisplayedReadings.Count; i++)
        {
            var reading = DisplayedReadings[i];
            float x = GetWorldXAtTime(reading.timeSeconds) + laneOffset.x;
            float y = groundY + ValueToBlockCount(reading) * blockSize + heightOffset + laneOffset.y;

            GameObject peak = Instantiate(peakPrefab, new Vector3(x, y, 0f), Quaternion.identity, peakContainer);
            peak.name = $"Peak_s{reading.second}";

            var tmp = peak.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.text = reading.noteName;

            spawnedPeaks.Add(peak);
        }

        Debug.Log($"AudioBarChartBuilder: placed {spawnedPeaks.Count} peak markers.");
    }

    [ContextMenu("Clear Peak Markers")]
    public void ClearPeaks()
    {
        for (int i = spawnedPeaks.Count - 1; i >= 0; i--)
        {
            if (spawnedPeaks[i] != null)
            {
                if (Application.isPlaying) Destroy(spawnedPeaks[i]);
                else DestroyImmediate(spawnedPeaks[i]);
            }
        }
        spawnedPeaks.Clear();
    }

    [ContextMenu("Clear Bar Chart")]
    public void Clear()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] != null)
            {
                if (Application.isPlaying) Destroy(spawned[i]);
                else DestroyImmediate(spawned[i]);
            }
        }
        spawned.Clear();
        ClearPeaks();
    }

    public float GetWorldXAtTime(float timeSeconds)
    {
        float segDuration = analyzer != null ? Mathf.Max(0.0001f, analyzer.segmentDuration) : 1f;
        return timeSeconds * (buildingSpacing / segDuration);
    }

    public int GetBlockCountForReading(AudioPerSecondAnalyzer.SecondReading reading) => ValueToBlockCount(reading);

    public int GetBlockCount(int secondIndex)
    {
        if (analyzer == null || analyzer.Readings == null || analyzer.Readings.Count == 0) return minBlocks;
        secondIndex = Mathf.Clamp(secondIndex, 0, analyzer.Readings.Count - 1);
        return ValueToBlockCount(analyzer.Readings[secondIndex]);
    }

    // Last displayed reading at or before timeSeconds (step-hold across gaps).
    public bool TryGetCurrentDisplayedReading(float timeSeconds, out AudioPerSecondAnalyzer.SecondReading reading, out int displayedIndex)
    {
        if (DisplayedReadings == null || DisplayedReadings.Count == 0)
        {
            reading = default;
            displayedIndex = -1;
            return false;
        }

        int idx = -1;
        for (int i = 0; i < DisplayedReadings.Count; i++)
        {
            if (DisplayedReadings[i].timeSeconds <= timeSeconds) idx = i;
            else break;
        }
        if (idx < 0) idx = 0;

        reading = DisplayedReadings[idx];
        displayedIndex = idx;
        return true;
    }

    public float GetHeightAtWorldX(float x)
    {
        float segDuration = analyzer != null ? Mathf.Max(0.0001f, analyzer.segmentDuration) : 1f;
        float spacing = Mathf.Max(0.0001f, buildingSpacing);
        float timeSeconds = x * (segDuration / spacing);
        return GetHeightAtTime(timeSeconds);
    }

    // Lerps between the two nearest *raw* readings (not DisplayedReadings) so
    // the height dips during silence instead of holding flat across a gap.
    public float GetHeightAtTime(float timeSeconds)
    {
        if (analyzer == null || analyzer.Readings == null || analyzer.Readings.Count == 0)
            return groundY;

        var readings = analyzer.Readings;

        int idx = -1;
        for (int i = 0; i < readings.Count; i++)
        {
            if (readings[i].timeSeconds <= timeSeconds) idx = i;
            else break;
        }
        if (idx < 0) idx = 0;

        int nextIdx = Mathf.Min(idx + 1, readings.Count - 1);
        var current = readings[idx];
        var next = readings[nextIdx];

        float heightCurrent = groundY + ValueToBlockCount(current) * blockSize;
        if (nextIdx == idx || next.timeSeconds <= current.timeSeconds)
            return heightCurrent;

        float heightNext = groundY + ValueToBlockCount(next) * blockSize;
        float frac = Mathf.Clamp01((timeSeconds - current.timeSeconds) / (next.timeSeconds - current.timeSeconds));
        return Mathf.Lerp(heightCurrent, heightNext, frac);
    }

    private int ValueToBlockCount(AudioPerSecondAnalyzer.SecondReading reading)
    {
        float t;
        switch (metric)
        {
            case Metric.Volume:
                t = Mathf.InverseLerp(volumeDbFloor, volumeDbCeiling, reading.volumeDb);
                break;

            case Metric.Note:
                // MIDI is linear in semitones (unlike raw Hz, which is logarithmic).
                int midi = reading.midiNote >= 0 ? reading.midiNote : midiNoteFloor;
                t = Mathf.InverseLerp(midiNoteFloor, midiNoteCeiling, midi);
                break;

            case Metric.Pitch:
            default:
                float hz = reading.pitchHz > 0f ? reading.pitchHz : pitchHzFloor;
                t = Mathf.InverseLerp(pitchHzFloor, pitchHzCeiling, hz);
                break;
        }

        t = Mathf.Clamp01(t);
        int blocks = Mathf.RoundToInt(Mathf.Lerp(minBlocks, maxBlocks, t));
        return Mathf.Clamp(blocks, minBlocks, maxBlocks);
    }
}
