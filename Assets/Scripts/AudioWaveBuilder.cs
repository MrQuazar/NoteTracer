using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a continuous 2D wave through the "top" point of each second,
/// instead of AudioBarChartBuilder's stacked-block towers. Reuses the same
/// AudioBarChartBuilder for its metric mapping (Volume/Pitch/Note, floor,
/// ceiling, spacing) so both approaches stay driven by the same settings —
/// this script just connects the tops with a line instead of building
/// towers underneath them.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class AudioWaveBuilder : MonoBehaviour
{
    [Tooltip("The builder whose metric mapping (Volume/Pitch/Note) and layout (spacing, groundY, blockSize) define each point's height.")]
    public AudioBarChartBuilder chartBuilder;

    [Header("Line")]
    public float lineWidth = 0.15f;

    [Tooltip("Assign a material for the LineRenderer (e.g. a URP/Unlit or Sprites/Default material). LineRenderer won't render without one.")]
    public Material lineMaterial;

    [Tooltip("If true, interpolates a smooth curve through the per-second points (Catmull-Rom spline) instead of straight segments — an ECG-monitor look rather than a sharp zigzag.")]
    public bool smoothCurve = true;

    [Tooltip("How many interpolated points to generate between each pair of consecutive seconds. Higher = smoother curve, more line/collider points.")]
    [Range(2, 30)]
    public int pointsPerSegment = 10;

    [Header("Collision")]
    [Tooltip("If true, an EdgeCollider2D is generated along the same points, so the wave can be walked/collided on like a surface.")]
    public bool addCollider = true;

    public PhysicsMaterial2D physicsMaterial;

    private LineRenderer lineRenderer;
    private EdgeCollider2D edgeCollider;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    [ContextMenu("Build Wave")]
    public void Build()
    {
        if (chartBuilder == null || chartBuilder.analyzer == null ||
            chartBuilder.analyzer.Readings == null || chartBuilder.analyzer.Readings.Count == 0)
        {
            Debug.LogWarning("AudioWaveBuilder: no readings available. Analyze the clip and assign a chartBuilder first.");
            return;
        }

        var readings = chartBuilder.analyzer.Readings;
        int count = readings.Count;

        Vector3[] rawPoints = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float x = i * chartBuilder.buildingSpacing;
            float y = chartBuilder.groundY + chartBuilder.GetBlockCount(i) * chartBuilder.blockSize;
            rawPoints[i] = new Vector3(x, y, 0f);
        }

        Vector3[] finalPoints = smoothCurve ? GenerateSmoothPoints(rawPoints, pointsPerSegment) : rawPoints;

        if (lineMaterial != null) lineRenderer.material = lineMaterial;
        lineRenderer.useWorldSpace = true;

        // Uniform thickness along the whole line, regardless of any curve
        // that might otherwise be set on widthCurve, plus rounded joins so
        // corners don't show mitered/sharp artifacts.
        lineRenderer.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        lineRenderer.widthMultiplier = lineWidth;
        lineRenderer.numCapVertices = 8;
        lineRenderer.numCornerVertices = 8;

        lineRenderer.positionCount = finalPoints.Length;
        lineRenderer.SetPositions(finalPoints);

        if (addCollider)
        {
            edgeCollider = GetComponent<EdgeCollider2D>();
            if (edgeCollider == null) edgeCollider = gameObject.AddComponent<EdgeCollider2D>();
            Vector2[] colliderPoints = new Vector2[finalPoints.Length];
            for (int i = 0; i < finalPoints.Length; i++)
                colliderPoints[i] = finalPoints[i];
            edgeCollider.points = colliderPoints;
            if (physicsMaterial != null) edgeCollider.sharedMaterial = physicsMaterial;
        }

        Debug.Log($"AudioWaveBuilder: drew wave through {finalPoints.Length} points ({count} seconds, smoothed: {smoothCurve}).");

        if (chartBuilder.playback != null) chartBuilder.playback.StartTracing();
    }

    /// <summary>
    /// Catmull-Rom spline interpolation through the given points, producing
    /// a smooth curve that still passes exactly through every original
    /// per-second point (unlike Bezier, which would only pass through
    /// endpoints).
    /// </summary>
    private static Vector3[] GenerateSmoothPoints(Vector3[] pts, int subdivisions)
    {
        if (pts.Length < 3) return pts;

        var result = new List<Vector3>();
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 p0 = pts[Mathf.Max(i - 1, 0)];
            Vector3 p1 = pts[i];
            Vector3 p2 = pts[i + 1];
            Vector3 p3 = pts[Mathf.Min(i + 2, pts.Length - 1)];

            for (int s = 0; s < subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                result.Add(CatmullRomPoint(p0, p1, p2, p3, t));
            }
        }
        result.Add(pts[pts.Length - 1]);
        return result.ToArray();
    }

    private static Vector3 CatmullRomPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}