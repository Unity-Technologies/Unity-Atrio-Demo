using System;
using System.Collections.Generic;
using UnityEngine.Categorization;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Experimental.Neural.Rendering
{
    /// <summary>
    /// A Graphics Settings container for Neural Texture Compression settings.
    /// Accessible in Editor > Project Settings in the Graphics tab (URP). 
    /// </summary>
    /// <seealso cref="IRenderPipelineGraphicsSettings"/>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [CategoryInfo(Name = "Neural Texture Compression", Order = 1010)]
    public class NTCSettings : IRenderPipelineGraphicsSettings
    {
        #region Version
        internal enum Version : int
        {
            Initial = 0,
        }

        [SerializeField][HideInInspector]
        Version m_Version;

        /// <summary>Current version of the settings container. Used only for upgrading a project.</summary>
        public int version => (int)m_Version;
        #endregion

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField]
        bool m_Enabled = true;

        /// <summary>
        /// The project-wide switch for Neural Texture Compression. On by default, but the feature
        /// only actually runs when <see cref="hasValidWeights"/> is also true; see <see cref="isEnabled"/>.
        /// In the Editor, NTCBaker.ntcEnabledChanged reports when that changes.
        /// </summary>
        public bool enabled
        {
            get => m_Enabled;
#if UNITY_EDITOR
            set => this.SetValueAndNotify(ref m_Enabled, value);
#endif
        }

        /// Holds a binary blob for the NTC model weights and biases.
        /// The blob is a headerless little-endian fp32 dump produced by training pipeline.
        /// It is held as a <see cref="TextAsset"/> so it reaches the player unparsed and
        /// <see cref="NTCWeightsBuffer"/> can upload the bytes verbatim.
        [SerializeField]
        TextAsset m_Weights;

        /// <summary>
        /// The MLP parameter blob: one network per material in <see cref="materials"/>, which the
        /// runtime uploads and binds each material its own window of. Null when no weights have been assigned, in which case the feature stays
        /// off even while <see cref="enabled"/> is true.
        /// </summary>
        public TextAsset weights
        {
            get => m_Weights;
            set => this.SetValueAndNotify(ref m_Weights, value);
        }

        /// <summary>
        /// True when <paramref name="asset"/> is assigned and holds one or more well-formed MLP
        /// parameter blobs back to back, one per material at a fixed stride. Uses
        /// <see cref="TextAsset.dataSize"/> rather than <see cref="TextAsset.bytes"/> so it can be
        /// called every frame without allocating.
        /// </summary>
        public static bool IsValidWeights(TextAsset asset)
            => asset != null && asset.dataSize > 0 && asset.dataSize % NTCWeightsBuffer.expectedByteCount == 0;

        /// <summary>
        /// How many materials' networks <paramref name="asset"/> holds: material i's blob starts at
        /// byte i * NTCWeightsBuffer.expectedByteCount. Zero when the asset is not valid.
        /// </summary>
        public static int GetMaterialCount(TextAsset asset)
            => IsValidWeights(asset) ? (int)(asset.dataSize / NTCWeightsBuffer.expectedByteCount) : 0;

        /// <summary>
        /// Explains why NTC is not running although <see cref="enabled"/> is true, for a blob that
        /// fails <see cref="IsValidWeights"/>. Shared by the runtime warning, the Graphics Settings
        /// drawer and the build-time variant stripper so all three report the same thing.
        /// </summary>
        public static string GetInvalidWeightsMessage(TextAsset asset)
        {
            const string k_Consequence = "NTC is disabled, so materials on an NTC shader render the error shader; put them on their conventional shader.";

            if (asset == null)
                return "Neural Texture Compression is enabled in Graphics Settings but no MLP asset is assigned. " + k_Consequence;

            return $"Neural Texture Compression is enabled in Graphics Settings but MLP asset '{asset.name}' is " +
                   $"{asset.dataSize} bytes, expected a multiple of {NTCWeightsBuffer.expectedByteCount} " +
                   "(one network per material). " + k_Consequence;
        }

        /// <summary>
        /// True when <see cref="weights"/> holds a usable MLP parameter blob.
        /// </summary>
        public bool hasValidWeights => IsValidWeights(m_Weights);

        /// <summary>How many materials' networks <see cref="weights"/> holds.</summary>
        public int materialCount => GetMaterialCount(m_Weights);

        /// The materials whose networks live in <see cref="weights"/>. Each carries its slot in
        /// _NTCMaterialIndex; NTCRuntime walks this list to bind every material its own cbuffer
        /// window, because material-level bindings are not serialized and the runtime has no other
        /// way to find the materials. Referencing them here also keeps them in builds.
        [SerializeField]
        List<Material> m_Materials = new List<Material>();

        /// <summary>
        /// The materials encoded into <see cref="weights"/>, each identified by its
        /// <c>_NTCMaterialIndex</c>. Written by the project tooling that builds the combined blob.
        /// </summary>
        public List<Material> materials
        {
            get => m_Materials;
            set => this.SetValueAndNotify(ref m_Materials, value);
        }

        /// <summary>
        /// True when the feature is switched on and the settings describe a usable network.
        /// Everything else -- the global keywords, the GPU buffers, build variant stripping -- is
        /// gated on this, so a missing or malformed blob leaves the feature switched off (and any
        /// material still on an NTC shader on the error shader) rather than decoding zeros.
        /// </summary>
        public bool isEnabled => m_Enabled && hasValidWeights;

        /// <summary>
        /// How the MLP is evaluated. Also decides the view type the parameter buffers are created
        /// with, constant or byte-address, and which global shader keyword <see cref="NTCRuntime"/>
        /// enables.
        /// </summary>
        public enum RuntimeMode
        {
            /// <summary>
            /// Evaluate the MLP with mad() over per-material constant buffers. Runs on every
            /// graphics API, and the default for that reason.
            /// </summary>
            FMA4,

            /// <summary>
            /// Evaluate the MLP with cooperative vectors over ByteAddressBuffers. Needs D3D12 and
            /// a shader compiler that can reach SM 6.10.
            /// </summary>
            CooperativeVectors,
        }

        [SerializeField]
        RuntimeMode m_Mode = RuntimeMode.FMA4;

        /// <summary>
        /// The inference path the decode is compiled and bound for. Ignored while
        /// <see cref="isEnabled"/> is false.
        /// </summary>
        public RuntimeMode mode
        {
            get => m_Mode;
            set => this.SetValueAndNotify(ref m_Mode, value);
        }

        /// <summary>
        /// True when <paramref name="api"/> can run the cooperative-vector decode. The one place
        /// that encodes the requirement: <see cref="NTCRuntime"/> passes it the device actually in
        /// use, while the editor passes it every API the target is being built for. FMA4 has no
        /// such question -- it runs on every API NTC is allowed to be enabled for at all.
        /// </summary>
        public static bool SupportsCooperativeVectors(GraphicsDeviceType api)
            // NTC-TODO: Cannot be supported yet until LinAlg is out of preview and the requirements land in Unity.
            //=> api == GraphicsDeviceType.Direct3D12;
            => false;

        /// <summary>
        /// Explains why <paramref name="mode"/> cannot run here, for the runtime warning. Shaped
        /// like <see cref="GetInvalidWeightsMessage"/>, which reports the other reason the feature
        /// can be on in the settings and off in the frame.
        /// </summary>
        internal static string GetUnsupportedModeMessage(RuntimeMode mode)
            => $"Neural Texture Compression is set to {mode}, which requires Direct3D12, LinAlg and HLSL2021 support. " +
               "NTC is disabled, so materials on an NTC shader render the error shader on this device.";
    }
}
