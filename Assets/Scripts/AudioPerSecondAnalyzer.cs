using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Produces one volume (dB) and pitch (Hz) reading per second from an
// AudioClip, mirroring the browser-based per-second analysis.
public class AudioPerSecondAnalyzer : MonoBehaviour
{
    [System.Serializable]
    public struct SecondReading
    {
        public int second;        // sequential segment index, not necessarily whole seconds
        public float timeSeconds; // = second * segmentDuration
        public float volumeDb;
        public float pitchHz;
        public int midiNote;      // -1 when no pitch was detected
        public string noteName;
    }

    public AudioClip clip;
    public AudioBarChartBuilder builder;

    [Header("Segment settings")]
    [Tooltip("Length of each analyzed segment, in seconds.")]
    public float segmentDuration = 1f;

    [Tooltip("Sample window used for pitch detection, taken from the middle of each segment.")]
    public int pitchWindowSize = 1024;
    public float minPitchHz = 55f;
    public float maxPitchHz = 1500f;

    [Header("Export")]
    [Tooltip("Writes a CSV of every reading to disk when Analyze() runs.")]
    public bool exportToFile = true;

    [Tooltip("Under Assets/ in the editor, persistentDataPath in a build.")]
    public string exportFolderName = "AudioAnalysisExports";

    public List<SecondReading> Readings { get; private set; } = new List<SecondReading>();
    public string LastExportPath { get; private set; }

    [ContextMenu("Analyze Clip")]
    public void Analyze()
    {
        if (clip == null)
        {
            Debug.LogWarning("AudioPerSecondAnalyzer: no clip assigned.");
            return;
        }

        Readings.Clear();

        int channels = clip.channels;
        int sampleRate = clip.frequency;
        int totalSamples = clip.samples;

        float[] raw = new float[totalSamples * channels];
        clip.GetData(raw, 0);

        float[] mono = new float[totalSamples];
        for (int i = 0; i < totalSamples; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
                sum += raw[i * channels + c];
            mono[i] = sum / channels;
        }

        int samplesPerSegment = Mathf.Max(1, Mathf.RoundToInt(segmentDuration * sampleRate));
        int totalSegments = totalSamples / samplesPerSegment;

        for (int idx = 0; idx < totalSegments; idx++)
        {
            int segStart = idx * samplesPerSegment;
            int segEnd = Mathf.Min(segStart + samplesPerSegment, totalSamples);

            double sumSquares = 0;
            for (int s = segStart; s < segEnd; s++)
                sumSquares += mono[s] * mono[s];
            float rms = Mathf.Sqrt((float)(sumSquares / (segEnd - segStart)));
            float db = rms > 0f ? Mathf.Max(-60f, 20f * Mathf.Log10(rms)) : -60f;

            int midStart = Mathf.Clamp(
                (segStart + segEnd) / 2 - pitchWindowSize / 2,
                segStart,
                Mathf.Max(segStart, segEnd - pitchWindowSize));
            int windowLen = Mathf.Min(pitchWindowSize, segEnd - midStart);

            float freq = AutoCorrelatePitch(mono, midStart, windowLen, sampleRate, minPitchHz, maxPitchHz);
            int midi = freq > 0f ? FreqToMidi(freq) : -1;

            Readings.Add(new SecondReading
            {
                second = idx,
                timeSeconds = idx * segmentDuration,
                volumeDb = Mathf.Round(db * 10f) / 10f,
                pitchHz = freq > 0f ? Mathf.Round(freq) : 0f,
                midiNote = midi,
                noteName = midi >= 0 ? MidiToNoteName(midi) : ""
            });
        }

        Debug.Log($"AudioPerSecondAnalyzer: analyzed {Readings.Count} segments of '{clip.name}' at {segmentDuration}s each.");

        if (exportToFile)
            ExportReadingsToFile();
    }

    void Start()
    {
        // A clip already assigned in the Inspector is analyzed immediately;
        // otherwise GameLevelSongLoader assigns it once the chosen song has
        // loaded and calls Analyze()/builder.Build() itself.
        if (clip == null) return;

        Analyze();
        if (Readings.Count > 0)
        {
            builder.Build();
        }
    }

    [ContextMenu("Export Readings To File")]
    public void ExportReadingsToFile()
    {
        if (Readings.Count == 0)
        {
            Debug.LogWarning("AudioPerSecondAnalyzer: no readings to export. Run Analyze() first.");
            return;
        }

        string folderPath;
#if UNITY_EDITOR
        folderPath = Path.Combine(Application.dataPath, exportFolderName);
#else
        folderPath = Path.Combine(Application.persistentDataPath, exportFolderName);
#endif
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string safeClipName = clip != null ? clip.name : "clip";
        string filePath = Path.Combine(folderPath, $"{safeClipName}_analysis.csv");

        var sb = new StringBuilder();
        sb.AppendLine("Second,VolumeDb,PitchHz,MidiNote,Note");
        foreach (var r in Readings)
            sb.AppendLine($"{r.second},{r.volumeDb.ToString(System.Globalization.CultureInfo.InvariantCulture)},{r.pitchHz.ToString(System.Globalization.CultureInfo.InvariantCulture)},{r.midiNote},{r.noteName}");

        File.WriteAllText(filePath, sb.ToString());
        LastExportPath = filePath;

        Debug.Log($"AudioPerSecondAnalyzer: exported {Readings.Count} readings to {filePath}");

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    // Time-domain autocorrelation, restricted to a plausible lag range.
    // Returns -1 if the window is too quiet or has no clear periodicity.
    private static float AutoCorrelatePitch(float[] data, int offset, int size, int sampleRate, float minHz, float maxHz)
    {
        if (size < 8) return -1f;

        double mean = 0;
        for (int i = 0; i < size; i++) mean += data[offset + i];
        mean /= size;

        double rms = 0;
        for (int i = 0; i < size; i++)
        {
            double v = data[offset + i] - mean;
            rms += v * v;
        }
        rms = System.Math.Sqrt(rms / size);
        if (rms < 0.005) return -1f;

        int minLag = Mathf.Max(1, Mathf.FloorToInt(sampleRate / maxHz));
        int maxLag = Mathf.Min(size - 1, Mathf.FloorToInt(sampleRate / minHz));

        int bestLag = -1;
        double bestCorr = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double corr = 0;
            for (int i = 0; i < size - lag; i++)
                corr += (data[offset + i] - mean) * (data[offset + i + lag] - mean);
            if (corr > bestCorr)
            {
                bestCorr = corr;
                bestLag = lag;
            }
        }

        if (bestLag <= 0) return -1f;
        return sampleRate / (float)bestLag;
    }

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private static int FreqToMidi(float freq)
    {
        return Mathf.RoundToInt(69f + 12f * Mathf.Log(freq / 440f, 2f));
    }

    private static string MidiToNoteName(int midi)
    {
        int noteIndex = ((midi % 12) + 12) % 12;
        int octave = midi / 12 - 1;
        return NoteNames[noteIndex] + octave;
    }
}
