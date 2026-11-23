using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Experimental.Neural.Rendering
{
    /// <summary>
    /// Owns the process-wide lifetime of the NTC buffers and binds them once per rendered
    /// context on beginContextRendering.
    /// </summary>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    // partial because it's required by [OnCodeUnloading]
    public static partial class NTCRuntime
    {
        // Not auto-cleaned: resetting this to null on a code reload would drop the reference to
        // the GraphicsBuffers without disposing them, which leaks silently. ReleaseGpuResources
        // below does both, and OnCodeUnloading calls it.
        [NoAutoStaticsCleanup]
        static NTCWeightsBuffer s_Weights;

        // The global keywords of the NTC shader's multi_compile_fragment _UNITY_NTC_FMA4
        // _UNITY_NTC_COOP_VECTORS _UNITY_NTC_OFF: one inference path while NTC runs, _UNITY_NTC_OFF
        // while it is disabled, whose variant is a deliberate #error so a material still on an NTC
        // shader shows the error shader
        static readonly GlobalKeyword s_FMA4Keyword = GlobalKeyword.Create("_UNITY_NTC_FMA4");
        static readonly GlobalKeyword s_CoopVectorsKeyword = GlobalKeyword.Create("_UNITY_NTC_COOP_VECTORS");
        static readonly GlobalKeyword s_OffKeyword = GlobalKeyword.Create("_UNITY_NTC_OFF");

        // Set once the "enabled but no usable MLP" warning has been logged, so it is reported once
        // per occurrence rather than once per rendered context. Cleared whenever the state stops
        // being invalid, so a later regression warns again.
        static bool s_WarnedInvalidWeights;

        // The same, for a cooperative-vector setting this device cannot evaluate.
        static bool s_WarnedUnsupportedMode;

        // Set by a scene load, so the next context rebinds every material's cbuffer window.
        static bool s_MaterialWindowsStale;

        // Set once the "not URP" warning has been logged, so it is not repeated every context.
        static bool s_WarnedUnsupportedPipeline;

#if UNITY_EDITOR
        // Runs after every editor domain reload and at editor startup. Scene view rendering in
        // edit mode needs this; [RuntimeInitializeOnLoadMethod] never runs outside play mode.
        static NTCRuntime() => Install();
#endif

        // Runs in the player at startup and on entering play mode, including when Enter Play Mode
        // Options has domain reload disabled, which the editor static constructor does not cover.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.activeRenderPipelineDisposed -= ReleaseGpuResources;
            RenderPipelineManager.activeRenderPipelineDisposed += ReleaseGpuResources;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

#if UNITY_EDITOR
            EditorApplication.projectChanged -= InvalidateMaterialBindings;
            EditorApplication.projectChanged += InvalidateMaterialBindings;
            Undo.undoRedoPerformed -= InvalidateMaterialBindings;
            Undo.undoRedoPerformed += InvalidateMaterialBindings;
#endif

#if !UNITY_EDITOR
            Application.quitting -= ReleaseGpuResources;
            Application.quitting += ReleaseGpuResources;
#endif
        }

        static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Raised for whatever SRP is active, HDRP and custom pipelines included.
            if (RenderPipelineManager.currentPipeline is not UniversalRenderPipeline)
            {
                if (!s_WarnedUnsupportedPipeline)
                {
                    s_WarnedUnsupportedPipeline = true;
                    Debug.LogWarning("Neural Texture Compression only runs under URP; it is off under the active render pipeline.");
                }
                return;
            }

            s_WarnedUnsupportedPipeline = false;

            var settings = GraphicsSettings.GetRenderPipelineSettings<NTCSettings>();

            WarnIfWeightsInvalid(settings);

            // Switched off, or no usable weights, means no feature: _UNITY_NTC_OFF goes up, so any
            // material still on an NTC shader renders the error shader, and the buffers go rather
            // than sitting on the GPU holding zeros. Fixing either re-enables both on the next
            // context.
            if (settings == null || !settings.isEnabled)
            {
                ReleaseGpuResources();
                ApplyDisabledKeywords();
                return;
            }

            // Cooperative vectors are the only path with a device requirement; FMA4 runs anywhere
            // NTC can be enabled at all, which the Graphics Settings drawer and the variant
            // stripper gate on the target's graphics APIs. A build selecting cooperative vectors
            // can still come up on a non-D3D12 API, and the feature goes off entirely when it does
            // rather than quietly switching backends: the weights were packed for the selected
            // mode's buffer layout.
            if (settings.mode == NTCSettings.RuntimeMode.CooperativeVectors &&
                !NTCSettings.SupportsCooperativeVectors(SystemInfo.graphicsDeviceType))
            {
                if (!s_WarnedUnsupportedMode)
                {
                    s_WarnedUnsupportedMode = true;
                    Debug.LogWarning(NTCSettings.GetUnsupportedModeMessage(settings.mode));
                }

                ReleaseGpuResources();
                ApplyDisabledKeywords();
                return;
            }

            s_WarnedUnsupportedMode = false;

            ApplyKeywords(settings.mode);

            s_Weights ??= new NTCWeightsBuffer();
            s_Weights.UploadAndBind();

            // Material-level cbuffer windows are not serialized, so they are rebound whenever the
            // buffers or the registry change, and after events that can bring in material objects
            // that were never bound: a scene load, and in the editor an asset reimport or an undo.
            // Not every context: SetConstantBuffer marks the material dirty in the editor.
            s_Weights.BindMaterialWindows(settings.materials, s_MaterialWindowsStale);
            s_MaterialWindowsStale = false;
        }

        /// <summary>
        /// Rebinds every material in <see cref="NTCSettings.materials"/> its window of the MLP
        /// buffers on the next rendered context.
        /// </summary>
        /// <remarks>
        /// Material-level cbuffer bindings are not serialized, and anything that rebuilds a
        /// material's properties drops them: assigning its shader, or overwriting it from a
        /// snapshot. Call this after doing either to a listed material. Not needed after the NTC
        /// enabled and disabled callbacks, which call it themselves, nor after a scene load, an
        /// asset reimport or an undo.
        /// </remarks>
        public static void InvalidateMaterialBindings() => s_MaterialWindowsStale = true;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InvalidateMaterialBindings();

        /// <summary>
        /// Logs once when NTC is switched on in Graphics Settings but the MLP parameter blob is
        /// missing or malformed, since the feature then silently stays off.
        /// </summary>
        static void WarnIfWeightsInvalid(NTCSettings settings)
        {
            if (settings == null || !settings.enabled || settings.hasValidWeights)
            {
                s_WarnedInvalidWeights = false;
                return;
            }

            if (s_WarnedInvalidWeights)
                return;

            s_WarnedInvalidWeights = true;
            Debug.LogWarning(NTCSettings.GetInvalidWeightsMessage(settings.weights));
        }

        /// <summary>
        /// Enables the one inference keyword for <paramref name="mode"/>, or none at all when it is
        /// null, and clears _UNITY_NTC_OFF. Setting two would leave the decode's #elif chain picking
        /// whichever comes first, so the disables run unconditionally rather than only on a change.
        /// </summary>
        static void ApplyKeywords(NTCSettings.RuntimeMode? mode)
        {
            Shader.SetKeyword(in s_FMA4Keyword, mode == NTCSettings.RuntimeMode.FMA4);
            Shader.SetKeyword(in s_CoopVectorsKeyword, mode == NTCSettings.RuntimeMode.CooperativeVectors);
            Shader.SetKeyword(in s_OffKeyword, false);
        }

        /// <summary>
        /// NTC is disabled: only _UNITY_NTC_OFF, whose variant is the explicit #error.
        /// </summary>
        static void ApplyDisabledKeywords()
        {
            ApplyKeywords(null);
            Shader.SetKeyword(in s_OffKeyword, true);
        }

        static void ReleaseGpuResources()
        {
            // Keeps the keywords from outliving the buffers they describe: a pipeline switch or a
            // code reload would otherwise leave shaders selecting a decode with nothing bound to
            // decode from. Clears _UNITY_NTC_OFF too: a release is not "disabled", and the next
            // frame sets the right state.
            ApplyKeywords(null);

            s_Weights?.Dispose();
            s_Weights = null;
        }

#if UNITY_EDITOR
        [OnCodeUnloading]
        static void OnCodeUnloading()
        {
            ReleaseGpuResources();
        }
#endif
    }
}
