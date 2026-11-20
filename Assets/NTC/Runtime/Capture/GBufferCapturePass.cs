using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace NTC.Capture
{
    sealed class GBufferCapturePass : ScriptableRenderPass, IDisposable
    {
        internal const int k_Count = 4;

        readonly RTHandle[] m_Targets = new RTHandle[k_Count];

        // Called on the main thread once per target when its readback completes.
        internal Action<int, AsyncGPUReadbackRequest> onReadback;

        class CaptureData
        {
            public readonly TextureHandle[] sources = new TextureHandle[k_Count];
            public readonly TextureHandle[] destinations = new TextureHandle[k_Count];
            public RTHandle[] rtHandles;
            public Action<int, AsyncGPUReadbackRequest> onReadback;
        }

        public GBufferCapturePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
        }

        /// <summary>Set by RecordRenderGraph: false when the camera rendered no GBuffer to capture.</summary>
        internal bool recorded { get; private set; }

        internal Vector2Int size { get; private set; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            recorded = false;
            var resourceData = frameData.Get<UniversalResourceData>();
            var gBuffer = resourceData.gBuffer;
            if (gBuffer == null || gBuffer.Length < k_Count)
                return;
            for (int i = 0; i < k_Count; ++i)
            {
                if (!gBuffer[i].IsValid())
                    return;
            }

            var desc = renderGraph.GetTextureDesc(gBuffer[0]);
            size = new Vector2Int(desc.width, desc.height);

            using (var builder = renderGraph.AddUnsafePass<CaptureData>("Capture GBuffer", out var data))
            {
                data.rtHandles = m_Targets;
                data.onReadback = onReadback;
                for (int i = 0; i < k_Count; ++i)
                {
                    EnsureTarget(i, desc.width, desc.height);
                    data.sources[i] = gBuffer[i];
                    data.destinations[i] = renderGraph.ImportTexture(m_Targets[i]);
                    builder.UseTexture(data.sources[i], AccessFlags.Read);
                    builder.UseTexture(data.destinations[i], AccessFlags.Write);
                }
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((CaptureData d, UnsafeGraphContext ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    for (int i = 0; i < k_Count; ++i)
                        Blitter.BlitCameraTexture(cmd, d.sources[i], d.destinations[i], 0, false);
                    for (int i = 0; i < k_Count; ++i)
                    {
                        int index = i;
                        cmd.RequestAsyncReadback(d.rtHandles[i].rt, request => d.onReadback?.Invoke(index, request));
                    }
                });
            }

            recorded = true;
        }

        void EnsureTarget(int i, int width, int height)
        {
            var current = m_Targets[i];
            if (current != null && current.rt != null && current.rt.width == width && current.rt.height == height)
                return;

            current?.Release();
            m_Targets[i] = RTHandles.Alloc(width, height, GraphicsFormat.R16G16B16A16_SFloat,
                filterMode: FilterMode.Point, name: $"GBufferCapture{i}");
        }

        public void Dispose()
        {
            for (int i = 0; i < k_Count; ++i)
            {
                m_Targets[i]?.Release();
                m_Targets[i] = null;
            }
        }
    }
}
