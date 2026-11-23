#ifndef UNITY_NTC_INFERENCE_COOP_VECTORS_INCLUDED
#define UNITY_NTC_INFERENCE_COOP_VECTORS_INCLUDED

#ifndef UNITY_NTC_DECODE_INCLUDED
#error "inference_coop_vectors.hlsl is an implementation detail of NTCDecode.hlsl; include that instead."
#endif

#if !defined(__HLSL_ENABLE_16_BIT)
#error "The cooperative vector path needs 16-bit types. Add 'native16bit' to the pass requirement, e.g. '#pragma require linalg native16bit' (Shader Graph: Graph Inspector -> Preprocessor Directives -> Pragmas), or switch the NTC runtime mode in Graphics Settings to FMA4."
#endif

#if !((__SHADER_TARGET_MAJOR > 6) || (__SHADER_TARGET_MAJOR == 6 && __SHADER_TARGET_MINOR >= 10))
#error "NTC cooperative vectors need shader model 6.10 or newer. Raise the shader's #pragma target, or switch the NTC runtime mode in Graphics Settings to FMA4."
#endif
#if __HLSL_VERSION < 2021
#error "NTC cooperative vectors need HLSL 2021 or newer, which is what dx/linalg.h's templates are written against."
#endif

// Not available in Unity 6.7 yet. 
//#include <dx/linalg.h>

ByteAddressBuffer _NTCWeightBuffer;
ByteAddressBuffer _NTCBiasBuffer;

#define UNTC_W0_BYTE_OFFSET      (UNTC_W0_OFFSET * INF_DATA_TYPE_BYTES)
#define UNTC_WOUT_BYTE_OFFSET    (UNTC_WOUT_OFFSET * INF_DATA_TYPE_BYTES)
#define UNTC_B0_BYTE_OFFSET      (UNTC_B0_OFFSET * INF_DATA_TYPE_BYTES)
#define UNTC_BOUT_BYTE_OFFSET    (UNTC_BOUT_OFFSET * INF_DATA_TYPE_BYTES)
#define UNTC_WEIGHT_BYTE_STRIDE  (UNTC_WEIGHT_STRIDE * INF_DATA_TYPE_BYTES)
#define UNTC_BIAS_BYTE_STRIDE    (UNTC_BIAS_STRIDE   * INF_DATA_TYPE_BYTES)

// Must describe INF_DATA_TYPE, which NTCDecode.hlsl fixes at float16_t for this backend. FP32 is
// not a cooperative-vector matrix type: NVAPI's physical device properties list FLOAT16, FP8 and
// the 8-bit integer formats, and nothing rejects an fp32 matrix before the GPU faults on it.
#define UNTC_COMPONENT_TYPE      dx::linalg::ComponentType::F16

// The weights arrive in the layout the encoder wrote, indexed outDim * l + x, which makes each
// column of the outDim x inDim matrix contiguous with a column stride of outDim elements, so
// ColMajor reads those bytes as they are.
// NTC-TODO: We should MulOptimal, but we lack API to upload/convert that in Unity now.
#define UNTC_MATRIX_LAYOUT       dx::linalg::MatrixLayoutEnum::ColMajor
#define UNTC_MATRIX_STRIDE(outDim) ((outDim) * INF_DATA_TYPE_BYTES)

#define UNTC_WEIGHT_MATRIX(outDim, inDim) dx::linalg::Matrix<UNTC_COMPONENT_TYPE, outDim, inDim, \
                                             dx::linalg::MatrixUse::A, dx::linalg::MatrixScope::Thread>
#define UNTC_BIAS_REF(outDim)             dx::linalg::VectorRef<UNTC_COMPONENT_TYPE, outDim>

typedef UNTC_WEIGHT_MATRIX(UNTC_HIDDEN_DIM, UNTC_INPUT_DIM) W0_t;
typedef UNTC_WEIGHT_MATRIX(UNTC_OUTPUT_DIM, UNTC_HIDDEN_DIM) WOUT_t;

void NTCEvaluateMLP(inout untc_vector_t x, uint materialIdx)
{
    // Start of this material's block in each buffer. Both are zero while a single texture set is
    // bound; see the stride note in NTCDecode.hlsl.
    const uint weightBase = UNTC_WEIGHT_BYTE_STRIDE * materialIdx;
    const uint biasBase = UNTC_BIAS_BYTE_STRIDE * materialIdx;

    W0_t W0 = W0_t::Load<UNTC_MATRIX_LAYOUT, UNTC_MATRIX_ALIGN>(
        _NTCWeightBuffer, weightBase + UNTC_W0_BYTE_OFFSET, UNTC_MATRIX_STRIDE(UNTC_HIDDEN_DIM));
    WOUT_t WOUT = WOUT_t::Load<UNTC_MATRIX_LAYOUT, UNTC_MATRIX_ALIGN>(
        _NTCWeightBuffer, weightBase + UNTC_WOUT_BYTE_OFFSET, UNTC_MATRIX_STRIDE(UNTC_OUTPUT_DIM));

    UNTC_BIAS_REF(UNTC_HIDDEN_DIM) B0 = { _NTCBiasBuffer, biasBase + UNTC_B0_BYTE_OFFSET };
    UNTC_BIAS_REF(UNTC_OUTPUT_DIM) BOUT = { _NTCBiasBuffer, biasBase + UNTC_BOUT_BYTE_OFFSET };

    // Hidden layer, ReLU
    vector<INF_DATA_TYPE, UNTC_HIDDEN_DIM> h = dx::linalg::MultiplyAdd<INF_DATA_TYPE>(W0, x.data, B0);
    h = max(h, INF_DATA_TYPE(0.0));

    // Output layer, linear: no activation, matching the manifest and the software path.
    x.data = dx::linalg::MultiplyAdd<INF_DATA_TYPE>(WOUT, h, BOUT);
}

#endif // UNITY_NTC_INFERENCE_COOP_VECTORS_INCLUDED
