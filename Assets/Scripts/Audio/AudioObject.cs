using UnityEngine;

public class AudioObject : MonoBehaviour
{
    /// <summary>Sibling component responsible for moving the GameObject along the generated path.</summary>
    [HideInInspector] public AudioPositionUpdater PositionUpdater;
    /// <summary>Sibling component responsible for triggering and controlling audio playback.</summary>
    [HideInInspector] public AudioEmitter Emitter;

    /// <summary>Fetches sibling components so subclasses and external code can reference them via Parent.</summary>
    private void Awake()
    {
        PositionUpdater = GetComponent<AudioPositionUpdater>();
        Emitter = GetComponent<AudioEmitter>();
    }

    /// <summary>Initialises and positions the updater.</summary>
    private void Start()
    {
        if (PositionUpdater != null)
        {
            PositionUpdater.Init(this);
            PositionUpdater.InitPosition(true);
        }
    }

    /// <summary>Drives the position updater each frame.</summary>
    private void Update()
    {
        PositionUpdater?.UpdatePosition(Time.deltaTime);
    }
}
