#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>MothParticleDisplace</sg:ProviderKey>
///     <sg:DisplayName>Moth Particle Displace</sg:DisplayName>
///     <sg:SearchCategory>Custom/Moth</sg:SearchCategory>
///</funchints>
///<paramhints name = "PositionOS">
///     <sg:DisplayName>Position OS</sg:DisplayName>
///</paramhints>
///<paramhints name = "NormalOS">
///     <sg:DisplayName>Normal OS</sg:DisplayName>
///</paramhints>
///<paramhints name = "UV0">
///     <sg:DisplayName>UV0</sg:DisplayName>
///</paramhints>
///<paramhints name = "UV1">
///     <sg:DisplayName>UV1</sg:DisplayName>
///</paramhints>
///<paramhints name = "UV2">
///     <sg:DisplayName>UV2</sg:DisplayName>
///</paramhints>
///<paramhints name = "UV3">
///     <sg:DisplayName>UV3</sg:DisplayName>
///</paramhints>
///<paramhints name = "Speed">
///     <sg:DisplayName>Speed</sg:DisplayName>
///     <sg:Default>15</sg:Default>
///</paramhints>
///<paramhints name = "Phase">
///     <sg:DisplayName>Phase</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "Amplitude">
///     <sg:DisplayName>Amplitude</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "Bend">
///     <sg:DisplayName>Bend</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "NormalBend">
///     <sg:DisplayName>Normal Bend</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void MothParticleDisplace_float(
    float3 PositionOS,
    float3 NormalOS,
    float4 UV0,
    float4 UV1,
    float4 UV2,
    float4 UV3,
    float Speed,
    float Phase,
    float Amplitude,
    float Bend,
    float NormalBend,
    out float3 OutPosition,
    out float3 OutNormal,
    out float3 OutMotionVector)
{

// Particle streams:
//   UV0 = (mesh uv.xy, rotation3D.xy)
//   UV1 = (rotation3D.z, center.xyz)
//   UV2 = (size, ID, velocity.xy)
//   UV3 = (velocity.z, ...)
// Particle mesh vertices arrive already in world space (rotated, sized, translated),
// so rebuild the particle's local frame, displace there, then transform back.
float3 rot = float3(UV0.z, UV0.w, UV1.x);
float3 center = UV1.yzw;
float size = max(UV2.x, 1e-5);
float3 sn, cs;
sincos(rot, sn, cs);
// Unity euler order (Z, then X, then Y): R = Ry * Rx * Rz
float3x3 Rx = float3x3(1, 0, 0,   0, cs.x, -sn.x,   0, sn.x, cs.x);
float3x3 Ry = float3x3(cs.y, 0, sn.y,   0, 1, 0,   -sn.y, 0, cs.y);
float3x3 Rz = float3x3(cs.z, -sn.z, 0,   sn.z, cs.z, 0,   0, 0, 1);
float3x3 R = mul(Ry, mul(Rx, Rz));
float3x3 invR = transpose(R);

// Back to unscaled mesh space so the displacement matches the single Moth model
float3 p = mul(invR, PositionOS - center) / size;
float3 n = mul(invR, NormalOS);

// Per-particle phase offset from the ID (golden ratio hash, 0..2PI)
float offset = frac(UV2.y * 0.61803398875) * 6.28318530718;
float wave = UV0.x * Phase + offset;

// Same math as Moth.shadergraph, in particle-local space
float s = sin(_TimeParameters.x * Speed + wave);
float sv = s * UV0.y;
float3 pNow = float3(p.x * (1.0 - Bend * abs(sv)), p.y, p.z + sv * Amplitude);
n.xy += s * sign(p.x) * NormalBend;

OutPosition = center + mul(R, pNow * size);
OutNormal = normalize(mul(R, normalize(n)));

// Motion vector = this frame's position - last frame's position (world space).
// Only evaluated in the motion vector pass; other passes don't read it.
OutMotionVector = float3(0, 0, 0);
#if defined(SHADERPASS) && defined(SHADERPASS_MOTION_VECTORS)
#if SHADERPASS == SHADERPASS_MOTION_VECTORS
{
    float lastTime = _LastTimeParameters.x;
    float3 velocity = float3(UV2.z, UV2.w, UV3.x);
    float sPrev = sin(lastTime * Speed + wave);
    float svPrev = sPrev * UV0.y;
    float3 pPrev = float3(p.x * (1.0 - Bend * abs(svPrev)), p.y, p.z + svPrev * Amplitude);
    OutMotionVector = velocity * (_TimeParameters.x - lastTime) + mul(R, (pNow - pPrev) * size);
}
#endif
#endif
}