#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>WingTransmission</sg:ProviderKey>
///     <sg:DisplayName>Wing Transmission</sg:DisplayName>
///     <sg:SearchCategory>Custom/Moth</sg:SearchCategory>
///</funchints>
///<paramhints name = "PositionWS">
///     <sg:DisplayName>Position WS</sg:DisplayName>
///</paramhints>
///<paramhints name = "NormalWS">
///     <sg:DisplayName>Normal WS</sg:DisplayName>
///</paramhints>
///<paramhints name = "ViewDirWS">
///     <sg:DisplayName>View Dir WS</sg:DisplayName>
///</paramhints>
///<paramhints name = "UV">
///     <sg:DisplayName>UV</sg:DisplayName>
///</paramhints>
///<paramhints name = "BaseColor">
///     <sg:DisplayName>Base Color</sg:DisplayName>
///</paramhints>
///<paramhints name = "Thickness">
///     <sg:DisplayName>Thickness</sg:DisplayName>
///</paramhints>
///<paramhints name = "Threshold">
///     <sg:DisplayName>Threshold</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
void WingTransmission_float(
    float3 PositionWS,
    float3 NormalWS,
    float3 ViewDirWS,
    float4 UV,
    float3 BaseColor,
    float Thickness,
    float Threshold,
    out float3 Transmission)
{
// Light transmission through the wings, added on top of URP Lit via Emission.
// Main light works in every rendering path. Additional lights need the forward
// light loop, so under Deferred/Deferred+ (G-buffer pass) only the main light contributes.
Transmission = float3(0, 0, 0);
#if !defined(SHADERGRAPH_PREVIEW) && defined(UNIVERSAL_LIGHTING_INCLUDED)
// Everything below the uv.y threshold (the body) stays solid
if (UV.y >= Threshold)
{
    float3 normalWS = normalize(NormalWS);
    float3 viewDirWS = normalize(ViewDirWS);
    // brdfData.diffuse for a non-metallic surface
    half3 internalColor = BaseColor * (1.0 - 0.04);
    half3 colorFactor = (1.0 - Thickness) / ((2.0 - internalColor) * (2.0 - internalColor));
    half3 normalOffset = normalWS * (0.5 - internalColor * 0.5);

    Light mainLight = GetMainLight();
    half3 subsurfaceHalfDir = SafeNormalize(mainLight.direction + normalOffset);
    Transmission += saturate(dot(viewDirWS, -subsurfaceHalfDir)) * colorFactor * mainLight.color;

#if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = PositionWS;
    float4 screenPos = ComputeScreenPos(TransformWorldToHClip(PositionWS));
    inputData.normalizedScreenSpaceUV = screenPos.xy / screenPos.w;
    uint pixelLightCount = GetAdditionalLightsCount();

#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, PositionWS);
        half3 h = SafeNormalize(light.direction + normalOffset);
        Transmission += saturate(dot(viewDirWS, -h)) * colorFactor * light.color * light.distanceAttenuation;
    }
#endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, PositionWS);
        half3 h = SafeNormalize(light.direction + normalOffset);
        Transmission += saturate(dot(viewDirWS, -h)) * colorFactor * light.color * light.distanceAttenuation;
    LIGHT_LOOP_END
#endif
}
#endif
}
