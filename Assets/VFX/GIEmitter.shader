// Meta-only shader: invisible in normal rendering, only feeds emission to
// Surface Cache GI (and any other system that renders the Meta pass).
//
// Notes:
// - URP never draws a "Meta" LightMode pass, so objects using this are invisible.
// - The MeshRenderer must stay enabled for Surface Cache GI to include it.
// - The material needs Realtime emission GI flags (GIEmitterShaderGUI sets this).
// - Fade at runtime by setting _EmissiveMultiplier on the material itself
//   (not a MaterialPropertyBlock): Surface Cache renders the Meta pass per material.
Shader "Custom/VFX/GIEmitter"
{
    Properties
    {
        [HDR] _EmissiveColor("Emissive Color", Color) = (1,1,1,1)
        _EmissiveMultiplier("Emissive Multiplier", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Only used by GI systems (lightmapper / Surface Cache), never drawn by URP.
        Pass
        {
            Name "Meta"
            Tags
            {
                "LightMode" = "Meta"
            }

            Cull Off

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex GIEmitterMetaVertex
            #pragma fragment GIEmitterMetaFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EmissiveColor;
                half  _EmissiveMultiplier;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings GIEmitterMetaVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                return output;
            }

            half4 GIEmitterMetaFragment(Varyings input) : SV_Target
            {
                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo   = half3(0, 0, 0);   // emitter only, no bounce colour
                metaInput.Emission = _EmissiveColor.rgb * _EmissiveMultiplier;
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    CustomEditor "Courtyard.VFXEditor.GIEmitterShaderGUI"
}
