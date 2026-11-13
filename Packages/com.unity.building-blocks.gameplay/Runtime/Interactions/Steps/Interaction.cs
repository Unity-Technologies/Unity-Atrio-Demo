using System.Collections;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// The context a running <see cref="InteractionStep"/> gets: the player, the interactable, and the
    /// run's clock. One instance per <see cref="Interactable"/>, reused for every run.
    /// </summary>
    /// <remarks>
    /// Read <see cref="Elapsed"/> and <see cref="DeltaTime"/> instead of <c>Time.deltaTime</c> so the
    /// step also works when a test drives the run by hand. <see cref="Player"/> can be <c>null</c>.
    /// </remarks>
    public sealed class Interaction
    {
        /// <summary>The character this run acts on, or <c>null</c> when the interactable found none.</summary>
        public KinematicMover Player { get; private set; }

        /// <summary>The interactable running the steps. Its GameObject is where the steps live.</summary>
        public Interactable Interactable { get; }

        /// <summary>Seconds since the run started, on the interactable's clock.</summary>
        public float Elapsed { get; private set; }

        /// <summary>Seconds the run's clock advanced this frame. Use it where a component would read <c>Time.deltaTime</c>.</summary>
        public float DeltaTime { get; private set; }

        internal Interaction(Interactable interactable)
        {
            Interactable = interactable;
        }

        /// <summary>Waits <paramref name="seconds"/> of run time: <c>yield return interaction.Wait(2f)</c>.</summary>
        public IEnumerator Wait(float seconds)
        {
            float until = Elapsed + seconds;
            while (Elapsed < until)
                yield return null;
        }

        internal void Begin(KinematicMover player)
        {
            Player = player;
            Elapsed = 0f;
            DeltaTime = 0f;
        }

        internal void Advance(float deltaTime)
        {
            DeltaTime = deltaTime;
            Elapsed += deltaTime;
        }

        internal void End()
        {
            Player = null;
            DeltaTime = 0f;
        }
    }
}
