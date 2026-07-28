using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Follow Settings")]
    public float smoothTime = 0.15f;
    public Vector3 offset = new Vector3(0f, 1f, -10f);
    private Vector3 velocity = Vector3.zero;
    private float targetY = 0f;

    void Awake()
    {
        if (target != null) targetY = target.position.y;
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;

        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position, desiredPosition, ref velocity, smoothTime
        );

        transform.position = new Vector3(smoothedPosition.x, targetY + offset.y, smoothedPosition.z);
    }

}