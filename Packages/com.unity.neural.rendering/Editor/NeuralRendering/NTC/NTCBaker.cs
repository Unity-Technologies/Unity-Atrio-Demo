using System;
using UnityEngine.Experimental.Neural.Rendering;
using UnityEngine.Rendering;

namespace UnityEditor.Experimental.Neural.Rendering
{
    /// <summary>
    /// Editor-side NTC state: which targets support it, whether a build ships it, and callbacks for
    /// when it turns on or off.
    /// </summary>
    [InitializeOnLoad]
    public static class NTCBaker
    {
        /// <summary>
        /// Raised when <see cref="NTCSettings.isEnabled"/> changes, with its new value.
        /// </summary>
        /// <remarks>
        /// True when Enable NTC is switched on with a valid MLP asset, or a valid asset is assigned
        /// while it is on. False when Enable NTC is switched off, or the MLP asset is cleared or made
        /// invalid: a material left on an NTC shader renders the error shader from then on. A project
        /// that keeps its materials on conventional shaders while NTC is off swaps them here.
        /// </remarks>
        public static event Action<bool> ntcEnabledChanged;

        // The state the callbacks last reported. Compared rather than raised on every notification,
        // because several sources can report the same change: the setters, an inspector edit, an
        // undo, a reimport of the MLP asset.
        static bool s_NTCEnabled;

        static NTCBaker()
        {
            // Taken as the starting point, not reported: subscribers that rely on the callbacks
            // keep their own state across domain reloads.
            s_NTCEnabled = IsNTCEnabled();

            // NTCSettings' setters (SetValueAndNotify). Graphics Settings has no notification for
            // edits made through a SerializedObject, which the next two cover.
            GraphicsSettings.Subscribe<NTCSettings>(OnSettingsChanged);

            // Inspector edits, undo and redo of the settings asset.
            ObjectChangeEvents.changesPublished += OnChangesPublished;

            // The MLP asset reimported, which can change whether it is valid.
            EditorApplication.projectChanged += Refresh;
        }

        static bool IsNTCEnabled()
            => GraphicsSettings.TryGetRenderPipelineSettings(out NTCSettings settings) && settings.isEnabled;

        static void OnSettingsChanged(NTCSettings settings, string property) => Refresh();

        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; ++i)
            {
                if (stream.GetEventType(i) == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    Refresh();
                    return;
                }
            }
        }

        static void Refresh()
        {
            bool enabled = IsNTCEnabled();
            if (enabled == s_NTCEnabled)
                return;

            // Updated first, so a subscriber that changes the settings again gets its own callback.
            s_NTCEnabled = enabled;

            ntcEnabledChanged?.Invoke(enabled);

            // Subscribers typically change the materials' shaders, which drops the cbuffer windows
            // the runtime bound them.
            NTCRuntime.InvalidateMaterialBindings();
        }

        /// <summary>
        /// True when a build for <paramref name="target"/> ships NTC: it is enabled with a valid MLP
        /// asset (<see cref="NTCSettings.isEnabled"/>) and the target supports it. The single answer
        /// every build step takes -- NTCShaderVariantStripper keeps or drops the decode variants on
        /// it, NTCSettingsStripper the weights and material list -- so a player never decodes with
        /// no network, or ships NTC materials with no decode.
        /// </summary>
        public static bool ShipsNTC(NTCSettings settings, BuildTarget target)
            => settings != null && settings.isEnabled && IsSupported(target);

        /// <summary>
        /// True when <paramref name="target"/> supports NTC.
        /// </summary>
        public static bool IsSupported(BuildTarget target)
        {
            // Windows (D3D11, D3D12, Vulkan) and macOS (Metal). Metal runs the FMA4 decode only;
            // cooperative vectors stay D3D12 (SupportsCooperativeVectors).
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneOSX)
                return false;

            // NTC-TODO: This will change as we increase platform support, we should also add #pragma only_renderers
            // in shaders
            foreach (var api in PlayerSettings.GetGraphicsAPIs(target))
            {
                // Every API the player could come up on has to be one we support.
                if (!IsSupportedGraphicsAPI(api))
                    return false;
            }
            return true;
        }

        static bool IsSupportedGraphicsAPI(GraphicsDeviceType api)
            => api == GraphicsDeviceType.Direct3D12 ||
               api == GraphicsDeviceType.Direct3D11 ||
               api == GraphicsDeviceType.Vulkan ||
               api == GraphicsDeviceType.Metal;

        /// <summary>
        /// True when at least one of the graphics APIs <paramref name="target"/> is built for can
        /// run the cooperative-vector decode.
        /// A target built for D3D12 and other graphics APIs can run on other graphics APIs,
        /// which is why <see cref="NTCSettings.SupportsCooperativeVectors"/> is asked again at
        /// runtime.
        /// </summary>
        public static bool SupportsCooperativeVectors(BuildTarget target)
        {
            foreach (var api in PlayerSettings.GetGraphicsAPIs(target))
            {
                if (NTCSettings.SupportsCooperativeVectors(api))
                    return true;
            }

            return false;
        }
    }
}
