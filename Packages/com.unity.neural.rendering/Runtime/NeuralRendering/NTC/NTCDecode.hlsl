#ifndef UNITY_NTC_DECODE_INCLUDED
#define UNITY_NTC_DECODE_INCLUDED

// The network's shape, generated from NTCNetworkConfig.cs
#include "Packages/com.unity.neural.rendering/Runtime/NeuralRendering/NTC/NTCNetworkConfig.cs.hlsl"

// No decode, and no inference path, in two cases; the node then outputs a flat surface:
//
//   Any stage but the fragment one. The inference keywords are multi_compile_fragment, so they
//   do not exist in the vertex (or any other) program, yet Shader Graph includes the node's HLSL
//   in every stage's program. Without this, every vertex program would hit the #error below.
//
//   Shader Graph's own previews (node previews define SHADERGRAPH_PREVIEW, the master preview
//   "Shader Graphs/Master" SHADERGRAPH_PREVIEW_MAIN), which have no network bound whatever global
//   keywords happen to be set. The real shader's passes never define either.
// NTC-TODO: we need to make preview work on trainer window.
#if !defined(SHADER_STAGE_FRAGMENT) || defined(SHADERGRAPH_PREVIEW) || defined(SHADERGRAPH_PREVIEW_MAIN)
    #define UNTC_NO_DECODE 1
    #undef _UNITY_NTC_FMA4
    #undef _UNITY_NTC_COOP_VECTORS
    #undef _UNITY_NTC_OFF
#endif

#if defined(_UNITY_NTC_COOP_VECTORS)
    #define INF_DATA_TYPE       float16_t
    #define INF_DATA_TYPE_BYTES 2
#else
    // NTC-TODO: currently in float as there are some changes required in Unity for proper fp16 support and DXC.
    #define INF_DATA_TYPE       float
    #define INF_DATA_TYPE_BYTES 4
#endif

#if (UNTC_HIDDEN_DIM / 16) * 16 != UNTC_HIDDEN_DIM
    #error "UNTC_HIDDEN_DIM must be a multiple of 16."
#endif

// NTCEvaluateMLP decodes exactly one hidden layer: in -> hidden -> out. The offsets below follow
// UNTC_HIDDEN_LAYER_COUNT like NTCWeightsBuffer.cs does, but only 1 is supported.
#if UNTC_HIDDEN_LAYER_COUNT != 1
    #error "NTC decodes a single hidden layer only, but UNTC_HIDDEN_LAYER_COUNT is not 1. Set NTCNetworkConfig.untcHiddenLayerCount to 1, regenerate NTCNetworkConfig.cs.hlsl (Edit > Rendering > Generate Shader Includes), and use weights trained with one hidden layer."
#endif

#define UNTC_W0_WEIGHT_COUNT     (UNTC_INPUT_DIM  * UNTC_HIDDEN_DIM)
#define UNTC_HIDDEN_WEIGHT_COUNT (UNTC_HIDDEN_DIM * UNTC_HIDDEN_DIM)
#define UNTC_WOUT_WEIGHT_COUNT   (UNTC_HIDDEN_DIM * UNTC_OUTPUT_DIM)

// W0, a hidden-to-hidden block per extra hidden layer (none while there is one), then WOUT.
#define UNTC_W0_OFFSET       0
#define UNTC_WOUT_OFFSET     (UNTC_W0_OFFSET + UNTC_W0_WEIGHT_COUNT + (UNTC_HIDDEN_LAYER_COUNT - 1) * UNTC_HIDDEN_WEIGHT_COUNT)

// One hidden-width bias block per hidden layer, then BOUT.
#define UNTC_B0_OFFSET       0
#define UNTC_BOUT_OFFSET     (UNTC_B0_OFFSET + UNTC_HIDDEN_LAYER_COUNT * UNTC_HIDDEN_DIM)

#define UNTC_WEIGHT_STRIDE   (UNTC_WOUT_OFFSET + UNTC_WOUT_WEIGHT_COUNT)
#define UNTC_BIAS_STRIDE     (UNTC_BOUT_OFFSET + UNTC_OUTPUT_DIM)

typedef struct _untc_vector_t
{
#if defined(_UNITY_NTC_COOP_VECTORS)
    vector<INF_DATA_TYPE, UNTC_INPUT_DIM> data;
#else
    INF_DATA_TYPE data[UNTC_INPUT_DIM];
#endif
} untc_vector_t;

// NTC-TODO: This needs to match training side. 
// NTC-TODO: Use the sampler of one of the latent textures instead of an inline one, with the lod
// mode (none, trilinear, aniso) selected by a uniform to match it.
SAMPLER(sampler_LinearRepeat_Aniso8);
#define UNTC_LATENT_SAMPLER sampler_LinearRepeat_Aniso8
#define UNTC_MAX_ANISO      8.0

// Normalized lod, matching the trained test asset for now. An input to the network rather than a
// property of the fetch, so it travels to NTCDecode alongside the latents.
//
// It has to name the mip the latents were actually fetched at, since training pairs each mip's
// latents with that mip's lod. URP's SAMPLE_TEXTURE2D_GRAD scales the gradients by
// _GlobalMipBias.y (2^bias; negative bias while an upscaler or TAA sharpens), so the lod here is
// computed from the same scaled gradients rather than the raw ones.
float NTCComputeLodNorm(float baseResolution, float2 dUVdx, float2 dUVdy)
{
#if defined(UNIVERSAL_SHADER_VARIABLES_INCLUDED)
    dUVdx *= _GlobalMipBias.y;
    dUVdy *= _GlobalMipBias.y;
#endif

    // Note for trilinear you would do
    //float lod = log2(max(length(dUVdx * baseResolution), length(dUVdy * baseResolution)));
    
    // NTC-TODO: optimize this once new training is merged.
    // Clamped away from zero so a zero gradient (constant UVs, helper lanes) can't produce
    // log2(0) = -inf or 0/0.
    const float minFootprint = 1e-8;
    float2 axes = float2(length(dUVdx * baseResolution), length(dUVdy * baseResolution));
    float major = max(max(axes.x, axes.y), minFootprint);
    float minor = max(min(axes.x, axes.y), minFootprint);

    float ratio = major / minor;
    float N = min(ceil(ratio), UNTC_MAX_ANISO);

    float lod = log2(major / N);
    float normalizedLod = saturate(lod / max(log2(baseResolution), 1.0));

    return normalizedLod;
}

// The latents' channels in plane order, the normalized lod, then zero padding to the input width.
// Written once here rather than once per backend: it is the same values either way, and only the
// storage differs.
untc_vector_t NTCPackInputs(float3 latents[UNTC_LATENT_PLANE_COUNT], float lodNorm)
{
    untc_vector_t inputs;

    [unroll]
    for (uint p = 0; p < UNTC_LATENT_PLANE_COUNT; ++p)
    {
        inputs.data[3 * p + 0] = INF_DATA_TYPE(latents[p].r);
        inputs.data[3 * p + 1] = INF_DATA_TYPE(latents[p].g);
        inputs.data[3 * p + 2] = INF_DATA_TYPE(latents[p].b);
    }

    const uint lodIndex = 3 * UNTC_LATENT_PLANE_COUNT;
    inputs.data[lodIndex] = INF_DATA_TYPE(lodNorm);

    [unroll]
    for (uint pad = lodIndex + 1; pad < UNTC_INPUT_DIM; ++pad)
        inputs.data[pad] = INF_DATA_TYPE(0.0);

    return inputs;
}

// Explicit error when trying to run a variant with NTC disable. UNTC_NO_DECODE is a special path for previews to not
// render with error.
#if defined(UNTC_NO_DECODE)
#elif defined(_UNITY_NTC_COOP_VECTORS)
    #include "Packages/com.unity.neural.rendering/Runtime/NeuralRendering/NTC/inference_coop_vectors.hlsl"
#elif defined(_UNITY_NTC_FMA4)
    #include "Packages/com.unity.neural.rendering/Runtime/NeuralRendering/NTC/inference_fma.hlsl"
#else
    #error "Neural Texture Compression is not enabled (_UNITY_NTC_OFF), and NTC shaders have no conventional fallback. Enable NTC with a valid MLP asset in Project Settings > Graphics, or put the material on its conventional shader."
#endif

#endif // UNITY_NTC_DECODE_INCLUDED
