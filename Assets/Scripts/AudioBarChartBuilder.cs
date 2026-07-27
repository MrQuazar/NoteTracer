using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Reads the per-second readings from AudioPerSecondAnalyzer and builds a
/// bar-chart-style structure in a 2D scene: one "building" per second,
/// made of stacked instances of a square block prefab. Building height
/// is driven by volume (dB), pitch (Hz), or musical note (MIDI number).
/// </summary>
public class AudioBarChartBuilder : MonoBehaviour
{
    public enum Metric { Volume, Pitch, Note }

    [Header("Data source")]
    [Tooltip("The analyzer holding the per-second Readings to build from.")]
    public AudioPerSecondAnalyzer analyzer;
    public AudioPlaybackTracer playback;
    public AudioWaveBuilder waveBuilder;

    [Header("Prefab & layout")]
    [Tooltip("A square 2D prefab with a BoxCollider2D. One or more are stacked per building.")]
    public GameObject blockPrefab;

    [Tooltip("Parent transform new blocks are instantiated under. Auto-created if left empty.")]
    public Transform container;

    [Tooltip("World size of one block (assumes square blocks, matches the prefab's scale).")]
    public float blockSize = 1f;

    [Tooltip("Horizontal gap between the centers of consecutive buildings, in world units.")]
    public float buildingSpacing = 1.2f;

    [Tooltip("World Y position of the ground row (bottom of every building).")]
    public float groundY = 0f;

    [Header("Metric mapping")]
    public Metric metric = Metric.Volume;

    [Tooltip("Fewest blocks a building can have, even for silence/very low readings.")]
    public int minBlocks = 1;

    [Tooltip("Tallest a building is allowed to get, in blocks.")]
    public int maxBlocks = 20;

    [Tooltip("dB value that maps to minBlocks. Typical noise floor used by the analyzer is -60.")]
    public float volumeDbFloor = -60f;

    [Tooltip("dB value that maps to maxBlocks.")]
    public float volumeDbCeiling = 0f;

    [Tooltip("Hz value that maps to minBlocks (silence/unvoiced seconds also map here).")]
    public float pitchHzFloor = 55f;

    [Tooltip("Hz value that maps to maxBlocks.")]
    public float pitchHzCeiling = 1500f;

    [Header("Note metric (MIDI-based, song-independent)")]
    [Tooltip("MIDI note number that maps to minBlocks. Default 33 = A1, matching the analyzer's default minPitchHz (55Hz). Silence/unvoiced seconds also map here.")]
    public int midiNoteFloor = 33;

    [Tooltip("MIDI note number that maps to maxBlocks. Default 91 = ~G6, matching the analyzer's default maxPitchHz (1500Hz).")]
    public int midiNoteCeiling = 91;

    [Header("Peak markers")]
    [Tooltip("A 'unitPeak' prefab (e.g. a circle) placed at the exact spot the tracer will visit each second, so the player can see upcoming targets before the tracer reaches them.")]
    public GameObject peakPrefab;

    [Tooltip("Parent transform new peak markers are instantiated under. Auto-created if left empty.")]
    public Transform peakContainer;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<GameObject> spawnedPeaks = new List<GameObject>();

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

        var readings = analyzer.Readings;
        for (int i = 0; i < readings.Count; i++)
        {
            int height = ValueToBlockCount(readings[i]);
            float x = i * buildingSpacing;

            for (int b = 0; b < height; b++)
            {
                float y = groundY + b * blockSize + blockSize * 0.5f;
                GameObject block = Instantiate(blockPrefab, new Vector3(x, y, 0f), Quaternion.identity, container);
                block.name = $"Block_s{readings[i].second}_{b}";
                spawned.Add(block);

                if (b == height - 1)
                {
                    var tmp = block.GetComponentInChildren<TMP_Text>();
                    if (tmp != null) tmp.text = readings[i].noteName;
                }
            }
        }

        Debug.Log($"AudioBarChartBuilder: built {readings.Count} buildings, {spawned.Count} blocks total.");

        BuildPeakMarkers(readings);

        if (readings.Count > 0 && playback != null) 
        {
            waveBuilder.Build();
            playback.StartTracing();
        }
    }

    /// <summary>
    /// Places one peakPrefab instance at every second's exact tracer-landing
    /// spot: the same X/Y the tracer computes for that second, including its
    /// heightOffset and laneOffset. Reading those directly off "playback"
    /// (rather than duplicating the offset values here) means markers can
    /// never drift out of alignment with where the tracer actually lands —
    /// there's one source of truth for the offset, not two copies to keep
    /// in sync by hand.
    /// </summary>
    private void BuildPeakMarkers(List<AudioPerSecondAnalyzer.SecondReading> readings)
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

        for (int i = 0; i < readings.Count; i++)
        {
            float x = i * buildingSpacing + laneOffset.x;
            float y = groundY + GetBlockCount(i) * blockSize + heightOffset + laneOffset.y;

            GameObject peak = Instantiate(peakPrefab, new Vector3(x, y, 0f), Quaternion.identity, peakContainer);
            peak.name = $"Peak_s{readings[i].second}";

            var tmp = peak.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.text = readings[i].noteName;

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

    /// <summary>
    /// Returns the block count (building height) for the reading at the given
    /// second index, using the same mapping Build() uses. Lets other scripts
    /// (e.g. a playback tracer) query bar height without duplicating logic.
    /// </summary>
    public int GetBlockCount(int secondIndex)
    {
        if (analyzer == null || analyzer.Readings == null) return minBlocks;
        secondIndex = Mathf.Clamp(secondIndex, 0, analyzer.Readings.Count - 1);
        return ValueToBlockCount(analyzer.Readings[secondIndex]);
    }

    /// <summary>
    /// Continuous version of GetBlockCount's height — samples the wave's
    /// world-space Y at any X (not just at whole-second points) by lerping
    /// between the two nearest seconds. Used by anything that needs to
    /// treat the wave as a path to constrain movement against.
    /// </summary>
    public float GetHeightAtWorldX(float x)
    {
        if (analyzer == null || analyzer.Readings == null || analyzer.Readings.Count == 0) return groundY;

        int totalSeconds = analyzer.Readings.Count;
        float secF = x / buildingSpacing;
        int idx = Mathf.Clamp(Mathf.FloorToInt(secF), 0, totalSeconds - 1);
        int nextIdx = Mathf.Clamp(idx + 1, 0, totalSeconds - 1);
        float frac = Mathf.Clamp01(secF - idx);

        float heightCurrent = groundY + GetBlockCount(idx) * blockSize;
        float heightNext = groundY + GetBlockCount(nextIdx) * blockSize;
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
                // MIDI numbers are linear in semitones, so this mapping is
                // song-independent: the same note always produces the same
                // height, unlike raw Hz which is logarithmic in pitch.
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