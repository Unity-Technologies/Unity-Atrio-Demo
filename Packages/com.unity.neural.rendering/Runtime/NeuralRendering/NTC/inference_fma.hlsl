#ifndef UNITY_NTC_INFERENCE_FMA_INCLUDED
#define UNITY_NTC_INFERENCE_FMA_INCLUDED

#ifndef UNITY_NTC_DECODE_INCLUDED
#error "inference_fma.hlsl is an implementation detail of NTCDecode.hlsl; include that instead."
#endif

// A cbuffer array pads every element to 16 bytes, so parameters are grouped four per element.
typedef float4 untc_param4_t;

// Block names are what Shader.SetGlobalConstantBuffer binds to, not the array names inside them.
// Sized for a single texture set.
CBUFFER_START(NTCWeightConstants)
    untc_param4_t _NTCWeightBuffer[UNTC_WEIGHT_STRIDE / 4];
CBUFFER_END

CBUFFER_START(NTCBiasConstants)
    untc_param4_t _NTCBiasBuffer[UNTC_BIAS_STRIDE / 4];
CBUFFER_END

// in -> hidden -> out: a single hidden layer, ReLU after it, linear output.
// materialIdx is ignored for FMA path as we bind cbuffer window.
void NTCEvaluateMLP(inout untc_vector_t x, uint materialIdx)
{
    // Hidden layer. One fetch covers outputs x0..x0+3 for input l0; the scalar activation broadcasts.
    INF_DATA_TYPE hidden[UNTC_HIDDEN_DIM];
    [unroll]
    for (uint x0 = 0; x0 < UNTC_HIDDEN_DIM; x0 += 4)
    {
        untc_param4_t acc = (untc_param4_t)0.0;
        [unroll]
        for (uint l0 = 0; l0 < UNTC_INPUT_DIM; ++l0)
            acc = mad(x.data[l0], _NTCWeightBuffer[(UNTC_W0_OFFSET + UNTC_HIDDEN_DIM * l0 + x0) >> 2], acc);

        // Add the bias, then RELU
        acc += _NTCBiasBuffer[(UNTC_B0_OFFSET + x0) >> 2];
        acc = max(acc, (untc_param4_t)0.0);

        [unroll]
        for (uint c0 = 0; c0 < 4; ++c0)
            hidden[x0 + c0] = acc[c0];
    }

    // Output layer, linear: no activation
    [unroll]
    for (uint x1 = 0; x1 < UNTC_OUTPUT_DIM; x1 += 4)
    {
        untc_param4_t acc = (untc_param4_t)0.0;
        [unroll]
        for (uint l1 = 0; l1 < UNTC_HIDDEN_DIM; ++l1)
            acc = mad(hidden[l1], _NTCWeightBuffer[(UNTC_WOUT_OFFSET + UNTC_OUTPUT_DIM * l1 + x1) >> 2], acc);

        acc += _NTCBiasBuffer[(UNTC_BOUT_OFFSET + x1) >> 2];

        [unroll]
        for (uint c1 = 0; c1 < 4; ++c1)
            x.data[x1 + c1] = acc[c1];
    }
}

#endif // UNITY_NTC_INFERENCE_FMA_INCLUDED
