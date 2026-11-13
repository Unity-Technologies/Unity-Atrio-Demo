Shader "Atrio/Pond"
{
    Properties
    {
        _DeepColor ("Deep Color", Color) = (0.0, 0.2, 0.5, 1.0)
        _RippleSpeed ("Ripple Speed", Float) = 2.0
        _RippleFrequency ("Ripple Frequency", Float) = 20.0
        _RippleStrength ("Ripple Strength", Float) = 0.05
        _RippleDensity ("Ripple Density", Float) = 2.0
        _RippleSize ("Ripple Size", Float) = 0.5
        _RippleThickness ("Ripple Thickness", Range(0, 20)) = 5.0
        _RippleFadeIn ("Ripple Fade In", Range(0.01, 0.5)) = 0.1
        _RippleFadeOut ("Ripple Fade Out", Range(0.5, 0.99)) = 0.8
        _RippleNormalStrength ("Ripple Normal Strength", Range(0, 10)) = 1.0
        _RippleRingCount ("Rings Per Burst", Range(1, 10)) = 3.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.8
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _FlowMap ("Flow Normal Map", 2D) = "bump" {}
        _FlowSpeed ("Flow Speed", Vector) = (0.02, 0.02, 0, 0)
        _FlowTiling ("Flow Tiling", Float) = 1.0
        _FlowStrength ("Flow Strength", Range(0, 2)) = 0.2
        _PeakColor ("Peak Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _PeakPower ("Peak Power", Range(0.1, 10)) = 2.0
        _PeakIntensity ("Peak Intensity", Range(0, 5)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _DeepColor;
        float _RippleSpeed;
        float _RippleFrequency;
        float _RippleStrength;
        float _RippleDensity;
        float _RippleSize;
        float _RippleThickness;
        float _RippleFadeIn;
        float _RippleFadeOut;
        float _RippleNormalStrength;
        float _RippleRingCount;
        float _Smoothness;
        float _Metallic;
        float4 _FlowSpeed;
        float _FlowTiling;
        float _FlowStrength;
        float4 _PeakColor;
        float _PeakPower;
        float _PeakIntensity;
        CBUFFER_END

        TEXTURE2D(_FlowMap);
        SAMPLER(sampler_FlowMap);

        float2 hash22(float2 p)
        {
            p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
            return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
        }

        // Ripple + flow-map normal. Shared by ForwardLit and DepthNormals so SSR, SSAO and SCGI
        // (which read the prepass normals) see the same surface the forward pass shades.
        float3 PondNormalWS(float2 uv0, float3 geomNormalWS)
        {
            // Scattered ripple bursts, one per grid cell.
            float2 uv = uv0 * _RippleDensity;
            float2 gv = frac(uv) - 0.5;
            float2 noise = hash22(floor(uv));
            float2 center = noise * 0.3;
            float dist = length(gv - center);
            float cellRandom = frac(noise.x * 123.4);

            // Frequency high enough to fit RingCount wavelengths within 80% of RippleSize.
            float minFreq = (TWO_PI * _RippleRingCount) / max(0.01, _RippleSize * 0.8);
            float effectiveFreq = max(_RippleFrequency, minFreq);
            float wavelength = TWO_PI / effectiveFreq;

            float speed = max(1e-4, _RippleSpeed / max(0.01, _RippleFrequency));
            float maxTravelDist = _RippleSize * 1.5;
            float burstPeriod = maxTravelDist / speed;
            float burstProgress = frac((_Time.y + cellRandom * burstPeriod) / burstPeriod);
            float packetRadius = burstProgress * maxTravelDist;

            // Phase shifted by PI/2 so a crest sits at the packet centre.
            float ripplePhase = (dist - packetRadius) * effectiveFreq + HALF_PI;
            float packetMask = saturate(1.0 - abs(dist - packetRadius) / (_RippleRingCount * wavelength * 0.5));

            float expansion = dist / _RippleSize;
            float lifeMask = smoothstep(0.0, _RippleFadeIn, expansion) * (1.0 - smoothstep(_RippleFadeOut, 1.0, expansion));

            float rippleBase = saturate(sin(ripplePhase) * 0.5 + 0.5);
            float invThickness = 1.0 / max(0.01, _RippleThickness);
            float rippleSlope = invThickness * pow(rippleBase, max(0.0, invThickness - 1.0)) * cos(ripplePhase) * 0.5 * effectiveFreq;

            // Horizontal perturbation in world XZ (the pond is a flat, upward-facing plane).
            float2 rippleDir = normalize(gv - center + 0.0001);
            float2 rippleXZ = rippleDir * (rippleSlope * _RippleStrength * _RippleNormalStrength * packetMask * lifeMask);

            float2 flowUV = uv0 * _FlowTiling + _FlowSpeed.xy * _Time.y;
            float3 flowTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, flowUV), _FlowStrength);

            return normalize(geomNormalWS + float3(rippleXZ.x + flowTS.x, flowTS.z, rippleXZ.y + flowTS.y));
        }
        ENDHLSL

        Pass
        {
            // Forward-only so the deferred renderer draws it in its forward opaque pass (there is no GBuffer pass).
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Only the pipeline features this project's renderers use (Forward+ / Deferred+ with the GPU
            // Resident Drawer, cascaded soft shadows, SSAO, SSR, SCGI, pre-exposure). No fog, lightmaps,
            // light cookies or light layers (every light includes the pond's rendering layer).
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_REFLECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _EXPOSURE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 geomNormalWS = normalize(input.normalWS);
                float3 normalWS = PondNormalWS(input.uv, geomNormalWS);

                // Tint the steepest ripple/flow slopes with the peak colour.
                float normalDeviation = 1.0 - saturate(dot(normalWS, geomNormalWS));
                float3 albedo = _DeepColor.rgb + _PeakColor.rgb * (pow(normalDeviation, _PeakPower) * _PeakIntensity);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = float3(0, 0, 1);
                surfaceData.alpha = 1.0;
                surfaceData.occlusion = 1.0;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                // Per-pixel, like Lit without the shadow-coord interpolator; handles cascades.
                inputData.shadowCoord = MainLightShadowsAvailable() ? TransformWorldToShadowCoord(input.positionWS, false) : float4(0, 0, 0, 0);
                inputData.preExposureMultiplier = GetPreExposureMultiplier();

                // Indirect diffuse: SCGI's screen-space irradiance when it is running, else the ambient probe.
                GIParams giParams = (GIParams)0;
                giParams.positionWS = input.positionWS;
                giParams.normalWS = normalWS;
                giParams.viewDirWS = inputData.viewDirectionWS;
                giParams.positionSS = input.positionCS.xy;
                InitializeBakedGI(giParams, inputData.bakedGI, inputData.shadowMask);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = ClampExposed(inputData.preExposureMultiplier * color.rgb);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _WRITE_SMOOTHNESS

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = PondNormalWS(input.uv, normalize(input.normalWS));

                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    half4 outNormalWS = half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
                #else
                    half4 outNormalWS = half4(normalWS, 0.0);
                #endif

                #if defined(_WRITE_SMOOTHNESS)
                    outNormalWS.a = _Smoothness;
                #endif

                return outNormalWS;
            }
            ENDHLSL
        }

        Pass
        {
            // Used when the depth prepass doesn't need normals (e.g. depth priming with SCGI off).
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            // SCGI's surface cache reads material albedo through the Meta pass. The water contributes flat
            // white albedo and no emission, so its bounce is just whatever light SCGI lands on it.
            Name "Meta"
            Tags { "LightMode"="Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex UniversalVertexMeta
            #pragma fragment frag

            #define _BaseMap_ST unity_LightmapST
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = half3(1.0, 1.0, 1.0);
                return UniversalFragmentMeta(input, metaInput);
            }
            ENDHLSL
        }
    }
}
