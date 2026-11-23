using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Neural.Rendering
{
    /// <summary>
    /// Owns the GPU buffers the neural-texture-compression decode reads. The FMA4 path binds each
    /// material its own window of them as the <c>NTCWeightConstants</c> and <c>NTCBiasConstants</c>
    /// cbuffers; the cooperative-vector path binds them whole as the globals
    /// <c>_NTCWeightBuffer</c> and <c>_NTCBiasBuffer</c>.
    /// </summary>
    internal sealed class NTCWeightsBuffer : IDisposable
    {
        // The shape the asset is laid out in: in -> hidden -> out, one hidden layer. It is the
        // compiled shape by definition: switching network configuration rewrites NTCNetworkConfig
        // and the weights asset together, and a weights asset of another shape (another hidden
        // width, or more hidden layers) fails the byte-count check in Populate.
        const int k_AssetHiddenDim = NTCNetworkConfig.untcHiddenDim;
        const int k_AssetHiddenLayerCount = NTCNetworkConfig.untcHiddenLayerCount;

        /// <summary>
        /// Floats in the weights buffer: W0, a hidden-to-hidden block per extra hidden layer (none
        /// while k_AssetHiddenLayerCount is 1, which Populate asserts), then the output layer's WOUT.
        /// Also the per-material stride, matching UNTC_WEIGHT_STRIDE in Runtime/NTC/NTCDecode.hlsl.
        /// </summary>
        internal const int weightFloatCount =
            NTCNetworkConfig.untcInputDim * k_AssetHiddenDim +
            (k_AssetHiddenLayerCount - 1) * k_AssetHiddenDim * k_AssetHiddenDim +
            k_AssetHiddenDim * NTCNetworkConfig.untcOutputDim;

        /// <summary>
        /// Floats in the bias buffer: one hidden-width block per hidden layer, then the output
        /// layer's BOUT. Also the per-material stride, matching UNTC_BIAS_STRIDE in
        /// Runtime/NTC/NTCDecode.hlsl.
        /// </summary>
        internal const int biasFloatCount =
            k_AssetHiddenLayerCount * k_AssetHiddenDim + NTCNetworkConfig.untcOutputDim;

        /// <summary>Floats in a well-formed blob: every layer's weights and biases.</summary>
        internal const int expectedFloatCount = weightFloatCount + biasFloatCount;

        /// <summary>Bytes in a well-formed blob. The asset is fp32 whatever the GPU format is.</summary>
        internal const int expectedByteCount = expectedFloatCount * 4;

        /// <summary>Bytes one network parameter occupies on the GPU in a given runtime mode.</summary>
        /// <remarks>
        /// Mirrors INF_DATA_TYPE_BYTES in NTCDecode.hlsl, which keys off _UNITY_NTC_COOP_VECTORS.
        /// The asset is always fp32; only the cooperative-vector path converts, because FLOAT32 is
        /// not a matrix type that hardware implements. The software path keeps fp32 -- reading fp16
        /// there costs a per-element unpack worth +44% DXIL instructions for no benefit.
        /// </remarks>
        internal static int ParameterByteSize(NTCSettings.RuntimeMode mode) =>
            mode == NTCSettings.RuntimeMode.CooperativeVectors ? 2 : 4;

        /// <summary>
        /// Elements to allocate for a buffer holding <paramref name="dataBytes"/> bytes of data,
        /// rounded up to a whole granule with one more granule of slack past it.
        /// </summary>
        /// <remarks>
        /// The granule is UNTC_MATRIX_ALIGN: a cooperative-vector matrix load may widen a fetch past
        /// the last parameter, so an allocation ending exactly there would be read off the end.
        /// </remarks>
        static int PaddedElementCount(int dataBytes, int elementSize)
        {
            const int align = NTCNetworkConfig.untcMatrixAlign;
            int paddedBytes = ((dataBytes + align - 1) / align) * align + align;
            return (paddedBytes + elementSize - 1) / elementSize;
        }

        /// <summary>Bytes in a float4, the FMA path's constant-buffer element.</summary>
        const int k_Float4Size = 16;

        /// <summary>Bytes the <c>untc_param4_t[N / 4]</c> cbuffer arrays occupy for N fp32 parameters.</summary>
        static int ConstantByteSize(int floatCount) => floatCount / 4 * k_Float4Size;

        /// <summary>
        /// fp32 parameters grouped four per element, matching the float4 cbuffer arrays in
        /// inference_fma.hlsl. The byte layout is unchanged from the flat float array; only the
        /// managed element type differs, so it agrees with the buffer's 16-byte stride.
        /// </summary>
        static Vector4[] PackFloat4(float[] source, int elementCount)
        {
            var packed = new Vector4[elementCount];
            for (int i = 0; i < source.Length; ++i)
                packed[i >> 2][i & 3] = source[i];
            return packed;
        }

        /// <summary>
        /// fp32 parameters to the fp16 values the cooperative-vector path reads, two packed per
        /// uint because a Raw GraphicsBuffer still has a 4-byte element size.
        /// </summary>
        static uint[] PackHalf(float[] source, int elementCount)
        {
            var packed = new uint[elementCount];
            for (int i = 0; i < source.Length; ++i)
            {
                uint value = Mathf.FloatToHalf(source[i]);
                packed[i >> 1] |= (i & 1) == 0 ? value : value << 16;
            }
            return packed;
        }

        // The constant buffers bind by CBUFFER_START block name, not by the array name inside.
        static readonly int s_NTCWeightBuffer = Shader.PropertyToID("_NTCWeightBuffer");
        static readonly int s_NTCBiasBuffer = Shader.PropertyToID("_NTCBiasBuffer");
        static readonly int s_NTCWeightConstants = Shader.PropertyToID("NTCWeightConstants");
        static readonly int s_NTCBiasConstants = Shader.PropertyToID("NTCBiasConstants");

        /// <summary>The material property holding which network in the combined blob a material uses.</summary>
        internal static readonly int s_NTCMaterialIndex = Shader.PropertyToID("_NTCMaterialIndex");

        // Bytes of one material's window as the cbuffer arrays declare it: UNTC_WEIGHT_STRIDE and
        // UNTC_BIAS_STRIDE, the same counts the asset is read with.
        static int WeightWindowBytes => ConstantByteSize(weightFloatCount);
        static int BiasWindowBytes => ConstantByteSize(biasFloatCount);

        // A cbuffer window has to start on the device's constant-buffer offset alignment (256 bytes
        // on D3D12, Vulkan and Metal on macOS). The weights already are a multiple of it; the biases are padded.
        static int AlignToConstantOffset(int bytes)
        {
            int align = Mathf.Max(SystemInfo.constantBufferOffsetAlignment, k_Float4Size);
            return (bytes + align - 1) / align * align;
        }

        GraphicsBuffer m_WeightBuffer;
        GraphicsBuffer m_BiasBuffer;

        // Networks held, and the byte distance between consecutive materials' parameters in each
        // buffer. Constant buffers pad each material to the offset alignment so every window can
        // start on it; raw buffers pack materials back to back.
        int m_MaterialCount;
        int m_WeightStrideBytes;
        int m_BiasStrideBytes;

        // Set whenever the buffers are (re)created or refilled, so the materials' windows, which
        // point into the old buffers, are bound again.
        bool m_MaterialBindingsDirty;

        // Materials currently holding a window, so a release can unbind them rather than leave them
        // pointing at a disposed buffer.
        readonly HashSet<Material> m_BoundMaterials = new HashSet<Material>();

        // A copy of the registry last bound from, to notice any entry being added, removed or
        // replaced, not only the list being reassigned.
        readonly List<Material> m_BoundRegistry = new List<Material>();
        readonly HashSet<Material> m_WarnedMaterials = new HashSet<Material>();
        bool m_WarnedRawMultiMaterial;

        // The state this object has already made a decision about, so a valid blob is not
        // re-uploaded and a malformed one is not re-read and re-diagnosed every frame.
        TextAsset m_EvaluatedAsset;
        NTCSettings.RuntimeMode m_EvaluatedMode;
        GraphicsBuffer.Target m_ParameterTarget;

        /// <summary>
        /// Uploads whatever has changed, and binds the cooperative-vector path's buffers globally.
        /// Called once per
        /// rendered context from <see cref="NTCRuntime"/>'s
        /// <c>RenderPipelineManager.beginContextRendering</c> handler, which runs before any camera
        /// renders and therefore before the GBuffer pass.
        /// </summary>
        public void UploadAndBind()
        {
            var settings = GraphicsSettings.GetRenderPipelineSettings<NTCSettings>();
            var asset = settings?.weights;
            var mode = settings?.mode ?? NTCSettings.RuntimeMode.FMA4;

            bool parametersLost = m_WeightBuffer == null || !m_WeightBuffer.IsValid() ||
                                  m_BiasBuffer == null || !m_BiasBuffer.IsValid();
            if (!ReferenceEquals(m_EvaluatedAsset, asset) || m_EvaluatedMode != mode || parametersLost)
            {
                m_EvaluatedAsset = asset;
                m_EvaluatedMode = mode;
                Populate(asset, mode);
            }

            // The constant path has no global binding: a global constant buffer wins over a
            // material-level one of the same name, which would put every material on material 0's
            // network. ReleaseParameterBuffers clears the globals before any buffer is created, and
            // each material gets its own window in BindMaterialWindows instead; a material missing
            // from the registry reads an unbound cbuffer (zeros).
            if (m_WeightBuffer != null && m_BiasBuffer != null && m_ParameterTarget != GraphicsBuffer.Target.Constant)
            {
                Shader.SetGlobalBuffer(s_NTCWeightBuffer, m_WeightBuffer);
                Shader.SetGlobalBuffer(s_NTCBiasBuffer, m_BiasBuffer);
            }
        }

        bool RegistryChanged(IReadOnlyList<Material> materials)
        {
            int count = materials?.Count ?? 0;
            if (count != m_BoundRegistry.Count)
                return true;
            for (int i = 0; i < count; ++i)
            {
                if (!ReferenceEquals(materials[i], m_BoundRegistry[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Binds each of <paramref name="materials"/> its own window of the combined parameter
        /// buffers, chosen by its <c>_NTCMaterialIndex</c>. Material-level bindings are not
        /// serialized, so this has to run again after the buffers change; it does nothing otherwise
        /// unless <paramref name="force"/> is set.
        /// </summary>
        /// <remarks>
        /// Only the FMA path windows: a cbuffer holds one network (64 KB is the cbuffer limit), so
        /// each material sees its network at offset 0. The cooperative-vector path binds the whole
        /// raw buffer and its decode still reads material 0 only.
        /// </remarks>
        public void BindMaterialWindows(IReadOnlyList<Material> materials, bool force)
        {
            if (m_WeightBuffer == null || m_BiasBuffer == null ||
                (!force && !m_MaterialBindingsDirty && !RegistryChanged(materials)))
                return;
            m_MaterialBindingsDirty = false;
            m_BoundRegistry.Clear();
            if (materials != null)
                m_BoundRegistry.AddRange(materials);

            if (materials == null || materials.Count == 0)
                return;

            if (m_ParameterTarget != GraphicsBuffer.Target.Constant)
            {
                if (materials.Count > 1 && !m_WarnedRawMultiMaterial)
                {
                    m_WarnedRawMultiMaterial = true;
                    Debug.LogWarning("NTC: the cooperative-vector path decodes material 0's network only; " +
                                     "use FMA4 to decode several materials' networks.");
                }
                return;
            }

            int weightWindow = WeightWindowBytes;
            int biasWindow = BiasWindowBytes;

            foreach (var material in materials)
            {
                if (material == null)
                    continue;

                int index = material.HasProperty(s_NTCMaterialIndex)
                    ? Mathf.RoundToInt(material.GetFloat(s_NTCMaterialIndex))
                    : 0;

                if (index < 0 || index >= m_MaterialCount)
                {
                    if (m_WarnedMaterials.Add(material))
                        Debug.LogWarning($"NTC: material '{material.name}' has _NTCMaterialIndex {index}, but the " +
                                         $"weights asset holds {m_MaterialCount} network(s). It is left unbound and decodes nothing.", material);
                    continue;
                }

                material.SetConstantBuffer(s_NTCWeightConstants, m_WeightBuffer, index * m_WeightStrideBytes, weightWindow);
                material.SetConstantBuffer(s_NTCBiasConstants, m_BiasBuffer, index * m_BiasStrideBytes, biasWindow);
                m_BoundMaterials.Add(material);
            }
        }

        /// <summary>
        /// Fills the parameter buffers from the asset, or with zeros when there is nothing usable to
        /// fill them from. Always leaves correctly sized buffers behind, because the binding above
        /// has to succeed either way.
        /// </summary>
        void Populate(TextAsset asset, NTCSettings.RuntimeMode mode)
        {
            // We currently only have network implemented for 1 hidden layer.
            Debug.Assert(k_AssetHiddenLayerCount == 1,
                $"NTC: NTCNetworkConfig.untcHiddenLayerCount is {k_AssetHiddenLayerCount}, but NTCEvaluateMLP decodes only 1 hidden layer.");
            byte[] bytes = null;
            int materialCount = 1;

            if (asset != null)
            {
                // Allocates a copy of the whole asset, which is why this is reached only on an
                // actual change rather than every frame.
                bytes = asset.bytes;
                int byteCount = bytes?.Length ?? 0;

                if (byteCount > 0 && byteCount % expectedByteCount == 0)
                {
                    materialCount = byteCount / expectedByteCount;
                }
                else
                {
                    Debug.LogError(
                        $"NTC: weights asset {asset.name} is {byteCount} bytes, but is read as " +
                        $"{NTCNetworkConfig.untcInputDim} -> {k_AssetHiddenDim} (x{k_AssetHiddenLayerCount}) -> " +
                        $"{NTCNetworkConfig.untcOutputDim} networks of " +
                        $"{expectedByteCount} bytes ({expectedFloatCount} fp32 values) each, one per " +
                        $"material. Decoding zeros instead.", asset);
                    bytes = null;
                }
            }

            EnsureParameterBuffers(mode, materialCount);

            int weightStrideFloats = m_WeightStrideBytes / 4;
            int biasStrideFloats = m_BiasStrideBytes / 4;
            var weights = new float[materialCount * weightStrideFloats];
            var biases = new float[materialCount * biasStrideFloats];

            if (bytes != null)
            {
                for (int m = 0; m < materialCount; ++m)
                    Deinterleave(bytes, m * expectedByteCount, weights, m * weightStrideFloats, biases, m * biasStrideFloats);
            }

            if (mode == NTCSettings.RuntimeMode.CooperativeVectors)
            {
                m_WeightBuffer.SetData(PackHalf(weights, m_WeightBuffer.count));
                m_BiasBuffer.SetData(PackHalf(biases, m_BiasBuffer.count));
            }
            else
            {
                // Uploaded as Vector4 so the managed element size matches the buffer's 16-byte
                // stride. The bytes are identical to the float[] -- four consecutive weights per
                // element is exactly how the shader indexes them.
                m_WeightBuffer.SetData(PackFloat4(weights, m_WeightBuffer.count));
                m_BiasBuffer.SetData(PackFloat4(biases, m_BiasBuffer.count));
            }

            m_MaterialBindingsDirty = true;
        }

        /// <summary>
        /// Walks one material's blob, starting at <paramref name="srcOffset"/>, layer by layer,
        /// moving each layer's weight block to <paramref name="weights"/> and its bias block to
        /// <paramref name="biases"/> at the given float offsets, so the two come out as W0|...|WOUT and
        /// B0|...|BOUT. Element order inside a block is untouched: the asset already stores weights as
        /// <c>outDim * l + x</c>, which is how the decode indexes them.
        /// </summary>
        /// NTC-TODO: This can be removed once the trainer provides in the expected format.
        static void Deinterleave(byte[] blob, int srcOffset, float[] weights, int weightOffset, float[] biases, int biasOffset)
        {
            int srcByte = srcOffset;
            int weightFloat = weightOffset;
            int biasFloat = biasOffset;

            void CopyLayer(int inDim, int outDim)
            {
                int weightCount = inDim * outDim;
                Buffer.BlockCopy(blob, srcByte, weights, weightFloat * 4, weightCount * 4);
                srcByte += weightCount * 4;
                weightFloat += weightCount;

                Buffer.BlockCopy(blob, srcByte, biases, biasFloat * 4, outDim * 4);
                srcByte += outDim * 4;
                biasFloat += outDim;
            }

            CopyLayer(NTCNetworkConfig.untcInputDim, k_AssetHiddenDim);

            int hiddenLayerCount = k_AssetHiddenLayerCount;
            for (int layer = 1; layer < hiddenLayerCount; ++layer)
                CopyLayer(k_AssetHiddenDim, k_AssetHiddenDim);
            CopyLayer(k_AssetHiddenDim, NTCNetworkConfig.untcOutputDim);
        }

        void EnsureParameterBuffers(NTCSettings.RuntimeMode mode, int materialCount)
        {
            // Raw is a byte-address view for the cooperative-vector path's ByteAddressBuffers;
            // Constant is what the FMA path's CBUFFER_START blocks need.
            var target = mode == NTCSettings.RuntimeMode.CooperativeVectors
                ? GraphicsBuffer.Target.Raw
                : GraphicsBuffer.Target.Constant;

            // Whether the device can bind that target at all is settled before NTCRuntime ever
            // reaches this class.
            if (m_WeightBuffer != null && m_WeightBuffer.IsValid() &&
                m_BiasBuffer != null && m_BiasBuffer.IsValid() &&
                m_ParameterTarget == target && m_MaterialCount == materialCount)
                return;

            // One of the two can be valid on its own after a device reset; both are recreated so
            // the pair always comes from the same upload.
            ReleaseParameterBuffers();

            // The element size has to match what the shader declares, not just the data size.
            // inference_fma.hlsl reads float4 cbuffer arrays, so that path needs a 16-byte stride;
            // a Raw buffer is byte-addressed and takes 4. Slack past the data is deliberate; see
            // PaddedElementCount. SetData uploads only the parameters themselves, so the tail is
            // never written and never read as data.
            int elementSize = target == GraphicsBuffer.Target.Raw ? sizeof(uint) : k_Float4Size;

            if (target == GraphicsBuffer.Target.Constant)
            {
                // Every material's window has to start on the constant-buffer offset alignment.
                m_WeightStrideBytes = AlignToConstantOffset(ConstantByteSize(weightFloatCount));
                m_BiasStrideBytes = AlignToConstantOffset(ConstantByteSize(biasFloatCount));
            }
            else
            {
                // Packed back to back. Strides stay fp32-sized: Populate lays the floats out and
                // PackHalf halves them on upload, matching UNTC_*_STRIDE in elements.
                m_WeightStrideBytes = weightFloatCount * 4;
                m_BiasStrideBytes = biasFloatCount * 4;
            }

            int parameterBytes = ParameterByteSize(mode);

            // The constant target gets an exact count: each window's bound size must match the
            // shader's array, and the strides are already whole float4s.
            int ElementCount(int strideBytes) => target == GraphicsBuffer.Target.Constant
                ? materialCount * strideBytes / k_Float4Size
                : PaddedElementCount(materialCount * strideBytes / 4 * parameterBytes, elementSize);

            m_WeightBuffer = new GraphicsBuffer(target, ElementCount(m_WeightStrideBytes), elementSize)
            {
                name = "NTCWeights"
            };
            m_BiasBuffer = new GraphicsBuffer(target, ElementCount(m_BiasStrideBytes), elementSize)
            {
                name = "NTCBiases"
            };
            m_ParameterTarget = target;
            m_MaterialCount = materialCount;
        }

        void ReleaseParameterBuffers()
        {
            // Unbind before disposing, so the global properties cannot be left pointing at a
            // destroyed buffer after a pipeline switch. Passing null is the supported unbind. Both
            // kinds are cleared, because the target may have changed since the last bind.
            Shader.SetGlobalBuffer(s_NTCWeightBuffer, (GraphicsBuffer)null);
            Shader.SetGlobalBuffer(s_NTCBiasBuffer, (GraphicsBuffer)null);
            Shader.SetGlobalConstantBuffer(s_NTCWeightConstants, (GraphicsBuffer)null, 0, 0);
            Shader.SetGlobalConstantBuffer(s_NTCBiasConstants, (GraphicsBuffer)null, 0, 0);

            foreach (var material in m_BoundMaterials)
            {
                if (material == null)
                    continue;
                material.SetConstantBuffer(s_NTCWeightConstants, (GraphicsBuffer)null, 0, 0);
                material.SetConstantBuffer(s_NTCBiasConstants, (GraphicsBuffer)null, 0, 0);
            }
            m_BoundMaterials.Clear();
            m_BoundRegistry.Clear();
            m_MaterialCount = 0;

            m_WeightBuffer?.Dispose();
            m_WeightBuffer = null;
            m_BiasBuffer?.Dispose();
            m_BiasBuffer = null;
        }

        public void Dispose()
        {
            ReleaseParameterBuffers();
            m_EvaluatedAsset = null;
        }
    }
}
