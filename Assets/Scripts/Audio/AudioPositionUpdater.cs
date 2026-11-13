using UnityEngine;

public class AudioPositionUpdater : MonoBehaviour
{
    /// <summary>The AudioEmitter sibling component, cached by Init.</summary>
    [HideInInspector] public AudioEmitter Emitter;
    /// <summary>Optional name used to filter debug log output.</summary>
    public string DebugName;

    protected AudioObject Parent;

    /// <summary>Called once by AudioObject after Awake. Caches parent references and runs first-time setup.</summary>
    public virtual void Init(AudioObject parent)        { Parent = parent; }

    /// <summary>Called every frame by AudioObject with the elapsed delta time.</summary>
    public virtual void UpdatePosition(float deltaTime) { }

    /// <summary>Randomises or resets the emitter's starting position. Pass true to pick a new random position.</summary>
    public virtual void InitPosition(bool setPosition)  { }

    /// <summary>Moves the emitter to a specific normalised (0–1) position along its path. Returns false if out of range.</summary>
    public virtual bool SetPosition(float normalizedPosition) { return false; }
}
