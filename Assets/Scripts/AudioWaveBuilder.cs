using System.Collections.Generic;
using UnityEngine;

// Draws a continuous wave through the "top" point of each second, using the
// same AudioBarChartBuilder metric mapping/layout instead of building towers.
[RequireComponent(typeof(LineRenderer))]
public class AudioWaveBuilder : MonoBehaviour
{
    public AudioBarChartBuilder chartBuilder;

    [Header("Line")]
    public float lineWidth = 0.15f;
    public Material lineMaterial;

    [Tooltip("Catmull-Rom smoothing through the per-second points instead of straight segments.")]
    public bool smoothCurve = true;

    [Range(2, 30)]
    public int pointsPerSegment = 10;

    [Header("Collision")]
    [Tooltip("Adds an EdgeCollider2D along the same points so the wave can be walked on.")]
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

    // Catmull-Rom spline: passes exactly through every original point,
    // unlike Bezier which only passes through endpoints.
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
