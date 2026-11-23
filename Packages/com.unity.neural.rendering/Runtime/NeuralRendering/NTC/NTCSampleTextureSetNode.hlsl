#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#include "Packages/com.unity.neural.rendering/Runtime/NeuralRendering/NTC/NTCDecode.hlsl"

#define DEBUG_NTC 0

// NTC-TODO: We should test regressing UV offset instead of using constant + decimator factor. 
#define UNTC_PIXEL_OFFSET    2.5 //
#define UNTC_BASE_RES _NeuralTextureLatent0_TexelSize.z

// g_NTCLatent13UVOffsets[0]: stores plane1 offset, [1]: stores plane3 offset :
static const float2 g_NTCLatent13UVOffsets = float2(UNTC_PIXEL_OFFSET / UNTC_BASE_RES, 2.0 * UNTC_PIXEL_OFFSET / UNTC_BASE_RES);

// Note: Unity imports the textures with a vertical flip,
// so this requires the model mesh to have texcoords to get their v flipped (ie v -> 1-v ) (and vertex windings changed accordingly),
// v_new = 1 - v
// v_new_off = 1 - (v + offset)
// v_new_off = 1 - v - offset = v_new - offset
static const float2 g_NTCLatent1UVOffsets = float2(g_NTCLatent13UVOffsets[0], -g_NTCLatent13UVOffsets[0]);
static const float2 g_NTCLatent3UVOffsets = float2(g_NTCLatent13UVOffsets[1], -g_NTCLatent13UVOffsets[1]);

void NTCSampleLatents(float2 uv, float2 dUVdx, float2 dUVdy, out float3 latents[UNTC_LATENT_PLANE_COUNT])
{
    latents[0] = SAMPLE_TEXTURE2D_GRAD(_NeuralTextureLatent0, UNTC_LATENT_SAMPLER, uv, dUVdx, dUVdy).rgb;
    latents[1] = SAMPLE_TEXTURE2D_GRAD(_NeuralTextureLatent1, UNTC_LATENT_SAMPLER, uv + g_NTCLatent1UVOffsets, dUVdx, dUVdy).rgb;
    latents[2] = SAMPLE_TEXTURE2D_GRAD(_NeuralTextureLatent2, UNTC_LATENT_SAMPLER, uv, dUVdx, dUVdy).rgb;
    latents[3] = SAMPLE_TEXTURE2D_GRAD(_NeuralTextureLatent3, UNTC_LATENT_SAMPLER, uv + g_NTCLatent3UVOffsets, dUVdx, dUVdy).rgb;
}

/// <funchints>
///     <sg:ProviderKey>Unity.Neural.Rendering.NTC.SampleTextureSetNTC</sg:ProviderKey>
///     <sg:DisplayName>Sample Texture Set (NTC, reflected)</sg:DisplayName>
///     <sg:SearchCategory>Input/Neural</sg:SearchCategory>
///     <sg:SearchName>Sample Texture Set NTC reflected</sg:SearchName>
///     <sg:SearchTerms>neural, ntc, texture set, latent</sg:SearchTerms>
/// </funchints>
///<paramhints name = "uv">
///     <UV />
///     <Default>UV0</Default>
///</paramhints>
///<paramhints name = "baseColor">
///     <sg:DisplayName>Base Color</sg:DisplayName>
///</paramhints>
///<paramhints name = "normalTS">
///     <sg:DisplayName>Normal TS</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
void SampleTextureSetNTC(
float2 uv,
out float3 baseColor, out float3 normalTS,
out float metallic, out float smoothness, out float occlusion, out float3 emission)
{
#if defined(UNTC_NO_DECODE)
    // Not the fragment stage, or a Shader Graph preview: nothing to decode with (see NTCDecode.hlsl).
    baseColor = float3(0.5, 0.5, 0.5);
    normalTS = float3(0.0, 0.0, 1.0);
    metallic = 0.0;
    occlusion = 1.0;
    smoothness = 0.5;
    emission = float3(0.0, 0.0, 0.0);
#else
    float2 dUVdx = ddx(uv);
    float2 dUVdy = ddy(uv);

    float3 latents[UNTC_LATENT_PLANE_COUNT];
    NTCSampleLatents(uv, dUVdx, dUVdy, latents);

    untc_vector_t x = NTCPackInputs(latents, NTCComputeLodNorm(UNTC_BASE_RES, dUVdx, dUVdy));

    // NTC-TODO: the FMA4 path needs no index, as each material binds its own cbuffer window.
    // Later we have to pass _NTCMaterialIndex as argument for coop_vectors.
    NTCEvaluateMLP(x, 0);

    // Output indices follow the trained sets' latent_manifest.json "output.features" list:
    // 0-2 albedo, 3-4 normal XY, 5 metallic, 6 occlusion, 7 smoothness -- the mask map's R, G and
    // A, in that order -- then 8-10 emission RGB, decoded only while the material has _EMISSION on.
    baseColor = float3(x.data[0], x.data[1], x.data[2]);

    // The network predicts only x and y; z is reconstructed, as the manifest's normalSets entry
    // asks for with z = -1. The two channels are stored [0,1]-encoded like the source normal map,
    // so they are decoded first. The result is unit length by construction.
    float2 normalXY = saturate(float2(x.data[3], x.data[4])) * 2.0 - 1.0;
    normalTS = float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));

    metallic = x.data[5];

    // A set trained without occlusion leaves this output untrained. The graph blends occlusion
    // with _OcclusionStrength, so such a material sets that to 0 rather than the decode guessing.
    occlusion = x.data[6];
    smoothness = x.data[7];

    // NTC-TODO: This code currently expects the trainer can setup that _EMISSION keyword. This needs to
    // be validated.
    emission = float3(0.0, 0.0, 0.0);
#ifdef _EMISSION
    emission = float3(x.data[8], x.data[9], x.data[10]);
#endif

#if DEBUG_NTC == 1
    // Tints what the network decoded, so NTC materials stand out from conventionally sampled ones.
    baseColor *= half3(0.0, 1.0, 0.0);
#endif
#endif // UNTC_NO_DECODE
}
