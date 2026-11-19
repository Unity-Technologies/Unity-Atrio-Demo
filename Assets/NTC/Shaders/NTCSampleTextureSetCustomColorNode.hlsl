#ifndef NTC_SAMPLE_TEXTURE_SET_CUSTOM_COLOR_NODE_INCLUDED
#define NTC_SAMPLE_TEXTURE_SET_CUSTOM_COLOR_NODE_INCLUDED

#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#include "Packages/com.unity.neural.rendering/Runtime/NeuralRendering/NTC/NTCDecode.hlsl"

// The decode for LitCustomColor_NTC and LitCustomColorWind_NTC. It is the package's Sample Texture Set
// (NTC) node with the extra outputs those graphs sample conventionally: the base map's alpha (tint
// weight) and the color mask's R and G. Latent sampling matches NTCSampleTextureSetNode.hlsl.

// TODO-NTC: We should test regressing UV offset instead of using constant + decimator factor.
#define UNTC_CC_PIXEL_OFFSET 2.5
#define UNTC_CC_BASE_RES     _NeuralTextureLatent0_TexelSize.z

// Planes 1 and 3 are offset by one and two pixel offsets; v is flipped (see NTCSampleTextureSetNode.hlsl).
static const float2 g_NTCCustomColorLatent1UVOffsets = float2(UNTC_CC_PIXEL_OFFSET / UNTC_CC_BASE_RES, -UNTC_CC_PIXEL_OFFSET / UNTC_CC_BASE_RES);
static const float2 g_NTCCustomColorLatent3UVOffsets = float2(2.0 * UNTC_CC_PIXEL_OFFSET / UNTC_CC_BASE_RES, -2.0 * UNTC_CC_PIXEL_OFFSET / UNTC_CC_BASE_RES);

void NTCCustomColorSampleLatents(float2 uv, float2 dUVdx, float2 dUVdy, out float3 latents[UNTC_LATENT_PLANE_COUNT])
{
    latents[0] = SAMPLE_TEXTURE2D_GRAD(_NeuralTextureLatent0, UNTC_LATENT_SAMPLER, uv, dUVdx, dUVdy).rgb;
    latents[1] = SAMPLE_TEXTURE2D_GRAD(_NeuralTextureLatent1, UNTC_LATENT_SAMPLER, uv + g_NTCCustomColorLatent1UVOffsets, dUVdx, dUVdy).rgb;
    latents[2] = SAMPLE_TEXTURE2D_GRAD(_NeuralTextureLatent2, UNTC_LATENT_SAMPLER, uv, dUVdx, dUVdy).rgb;
    latents[3] = SAMPLE_TEXTURE2D_GRAD(_NeuralTextureLatent3, UNTC_LATENT_SAMPLER, uv + g_NTCCustomColorLatent3UVOffsets, dUVdx, dUVdy).rgb;
}

/// <funchints>
///     <sg:ProviderKey>Unity.Neural.Rendering.NTC.SampleTextureSetNTCCustomColor</sg:ProviderKey>
///     <sg:DisplayName>Sample Texture Set (NTC, Custom Color)</sg:DisplayName>
///     <sg:SearchCategory>Input/Neural</sg:SearchCategory>
///     <sg:SearchName>Sample Texture Set NTC Custom Color</sg:SearchName>
///     <sg:SearchTerms>neural, ntc, texture set, latent, color mask, tint</sg:SearchTerms>
/// </funchints>
///<paramhints name = "uv">
///     <UV />
///     <Default>UV0</Default>
///</paramhints>
///<paramhints name = "baseColor">
///     <sg:DisplayName>Base Color</sg:DisplayName>
///</paramhints>
///<paramhints name = "tintWeight">
///     <sg:DisplayName>Tint Weight</sg:DisplayName>
///</paramhints>
///<paramhints name = "normalTS">
///     <sg:DisplayName>Normal TS</sg:DisplayName>
///</paramhints>
///<paramhints name = "metallic">
///     <sg:DisplayName>Metallic</sg:DisplayName>
///</paramhints>
///<paramhints name = "smoothness">
///     <sg:DisplayName>Smoothness</sg:DisplayName>
///</paramhints>
///<paramhints name = "occlusion">
///     <sg:DisplayName>Occlusion</sg:DisplayName>
///</paramhints>
///<paramhints name = "colorMaskR">
///     <sg:DisplayName>Color Mask R</sg:DisplayName>
///</paramhints>
///<paramhints name = "colorMaskG">
///     <sg:DisplayName>Color Mask G</sg:DisplayName>
///</paramhints>
///<paramhints name = "emission">
///     <sg:DisplayName>Emission</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
void SampleTextureSetNTCCustomColor(
float2 uv,
out float3 baseColor, out float tintWeight, out float3 normalTS,
out float metallic, out float smoothness, out float occlusion,
out float colorMaskR, out float colorMaskG, out float3 emission)
{
#if defined(UNTC_NO_DECODE)
    // Not the fragment stage, or a Shader Graph preview: nothing to decode with (see NTCDecode.hlsl).
    baseColor = float3(0.5, 0.5, 0.5);
    tintWeight = 0.0;
    normalTS = float3(0.0, 0.0, 1.0);
    metallic = 0.0;
    smoothness = 0.5;
    occlusion = 1.0;
    colorMaskR = 0.0;
    colorMaskG = 0.0;
    emission = float3(0.0, 0.0, 0.0);
#else
    float2 dUVdx = ddx(uv);
    float2 dUVdy = ddy(uv);

    float3 latents[UNTC_LATENT_PLANE_COUNT];
    NTCCustomColorSampleLatents(uv, dUVdx, dUVdy, latents);

    untc_vector_t x = NTCPackInputs(latents, NTCComputeLodNorm(UNTC_CC_BASE_RES, dUVdx, dUVdy));

    // NTC-TODO: the FMA4 path needs no index, as each material binds its own cbuffer window.
    NTCEvaluateMLP(x, 0);

    // Output indices, as the sets must be trained (latent_manifest.json "output.features"):
    // 0-7 match StandardLit_NTC -- 0-2 albedo, 3-4 normal XY, 5 metallic, 6 occlusion, 7 smoothness.
    // 8 base map alpha (tint weight), 9-10 color mask R and G, 11-13 emission.
    baseColor = float3(x.data[0], x.data[1], x.data[2]);

    // Normal XY is stored [0,1]-encoded like the source normal map; z is reconstructed.
    float2 normalXY = saturate(float2(x.data[3], x.data[4])) * 2.0 - 1.0;
    normalTS = float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));

    metallic = x.data[5];

    // A set trained without occlusion leaves this output untrained; such a material sets
    // _OcclusionStrength to 0 rather than the decode guessing.
    occlusion = x.data[6];
    smoothness = x.data[7];

    // The tint weight and mask blend colors across region edges, so keep them in [0,1].
    tintWeight = saturate(x.data[8]);
    colorMaskR = saturate(x.data[9]);
    colorMaskG = saturate(x.data[10]);

    // Always decoded, like the conventional graph always samples its emission map: a set trained
    // without emission must leave these outputs at zero, or the material sets _EmissionColour to black.
    emission = float3(x.data[11], x.data[12], x.data[13]);
#endif // UNTC_NO_DECODE
}

#endif // NTC_SAMPLE_TEXTURE_SET_CUSTOM_COLOR_NODE_INCLUDED
