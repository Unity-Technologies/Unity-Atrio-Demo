using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Neural.Rendering
{
    /// <summary>
    /// The network's shape, shared with the decode through the generated
    /// <c>NTCNetworkConfig.cs.hlsl</c>. Run <c>Edit &gt; Rendering &gt; Generate Shader Includes</c>
    /// after editing, and commit the generated file.
    /// </summary>
    /// <remarks>
    /// Names are spelled to land on the UNTC_* macros the decode already used: the generator inserts
    /// underscores at lower-&gt;upper boundaries and uppercases, and digits do not start a new word.
    /// So untcLatentPlaneCount becomes UNTC_LATENT_PLANE_COUNT. The fields must be public to be
    /// emitted at all.
    /// </remarks>
    [GenerateHLSL(needAccessors = false)]
    public static class NTCNetworkConfig
    {
        // The latent planes the network consumes, three channels each.
        public const int untcLatentPlaneCount = 4;

        public const int untcInputDim = 16;
        public const int untcOutputDim = 16;

        // Width of the network's single hidden layer: in -> hidden -> out, the only shape
        // NTCEvaluateMLP decodes. Edit, then Edit > Rendering > Generate Shader Includes. Must be a
        // multiple of 16 (NTCDecode.hlsl checks) and match the weights asset.
        public const int untcHiddenDim = 32;

        // Hidden layers between the input and output layers. Currently DecodeMLP only support 1 hidden layer.
        public const int untcHiddenLayerCount = 1;

        // Granule a cooperative-vector matrix load may widen a fetch to. The DXIL validator will not
        // let us declare it below 128, and the padding in NTCWeightsBuffer is sized from it.
        public const int untcMatrixAlign = 128;
    }
}
