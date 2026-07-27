using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Follow Settings")]
    public float smoothTime = 0.15f;
    public Vector3 offset = new Vector3(0f, 1f, -10f);
    private Vector3 velocity = Vector3.zero;
    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;

        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position, desiredPosition, ref velocity, smoothTime
        );

        transform.position = smoothedPosition;
    }

}