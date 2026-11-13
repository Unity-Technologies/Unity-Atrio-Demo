// Full-screen workload for QualityTierBenchmark: dependent texture reads mixed with transcendental math.
// Changing it changes scores, so bump QualityTierBenchmark.Version and recalibrate the thresholds afterwards.
Shader "Hidden/QualityTiers/Benchmark"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "gray" {}
    }

    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            // Additive blending keeps the GPU from skipping any of the overlapping full-screen draws.
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            Texture2D _NoiseTex;
            SamplerState sampler_NoiseTex;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                output.uv = uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv;
                float3 accumulated = 0;
                [loop]
                for (int i = 0; i < 48; i++)
                {
                    float4 n = _NoiseTex.SampleLevel(sampler_NoiseTex, p, 0);
                    p = frac(p * 1.37 + n.xy * 0.71 + i * 0.013);
                    accumulated += sin(n.rgb * 6.2831 + accumulated.zxy) * 0.5 + 0.5;
                }
                return float4(accumulated * (1.0 / 4096.0), 1.0 / 4096.0);
            }
            ENDHLSL
        }
    }
}
