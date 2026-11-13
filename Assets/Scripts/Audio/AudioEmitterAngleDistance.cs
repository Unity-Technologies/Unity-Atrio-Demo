using UnityEngine;

public class AudioEmitterAngleDistance : AudioPositionUpdater
{
    /// <summary>World-space centre point from which the polar position is calculated.</summary>
    [SerializeField] public Vector3 Origin;
    /// <summary>Random range for the emitter's angle around the origin (degrees).</summary>
    [SerializeField] public Vector2 MinMaxAngle;
    /// <summary>Random range for the emitter's distance from the origin.</summary>
    [SerializeField] public Vector2 MinMaxDistance;
    /// <summary>Scales the distance along the X axis to form an ellipse.</summary>
    [SerializeField] public float DistanceScaleX;
    /// <summary>Scales the distance along the Z axis to form an ellipse.</summary>
    [SerializeField] public float DistanceScaleZ;
    /// <summary>When true, picks a new random distance each time a sequence completes.</summary>
    [SerializeField] public bool SetRandomSubsequentDistance;
    /// <summary>When true, picks a new random angle each time a sequence completes.</summary>
    [SerializeField] public bool SetRandomSubsequentAngle;

    private float m_EmitterDistance;
    private float m_AngleInDegrees;

    /// <summary>Caches the parent emitter and registers the complete callback, then positions the emitter for the first time.</summary>
    public override void Init(AudioObject parent)
    {
        Parent = parent;
        Emitter = Parent.Emitter;
        Emitter.SetCompetedCallback(CompleteCallback);
        InitPosition();
    }

    /// <summary>Called by the emitter when it finishes a bark sequence; re-randomises angle and distance.</summary>
    private void CompleteCallback()
    {
        InitPosition();
    }

    /// <summary>Optionally re-randomises angle and distance, then moves the transform to the new polar position.</summary>
    private void InitPosition()
    {
        if (SetRandomSubsequentAngle)
            m_AngleInDegrees = Random.Range(MinMaxAngle.x, MinMaxAngle.y);
        if (SetRandomSubsequentDistance)
            m_EmitterDistance = Random.Range(MinMaxDistance.x, MinMaxDistance.y);

        transform.localPosition = CreateEmitterPosition(m_AngleInDegrees, m_EmitterDistance, DistanceScaleX, DistanceScaleZ);
    }

    /// <summary>Converts a polar angle and distance to a world-space XZ position around Origin.</summary>
    private Vector3 CreateEmitterPosition(float angleInDegrees, float distance, float scaleX, float scaleZ)
    {
        float rad = angleInDegrees * Mathf.Deg2Rad;
        float x   = Mathf.Cos(rad) * scaleX * distance;
        float z   = Mathf.Sin(rad) * scaleZ * distance;
        return new Vector3(x, 0, z);
    }
}
