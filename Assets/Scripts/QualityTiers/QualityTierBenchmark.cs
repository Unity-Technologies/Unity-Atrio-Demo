using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace QualityTiers
{
    /// <summary>
    /// Short synchronous GPU benchmark: draws a heavy full-screen shader into an off-screen target until the work
    /// takes long enough to time reliably, then reports megapixels shaded per second. Higher is faster.
    /// Scores are only comparable with the same Version, so bump it whenever the workload changes.
    /// </summary>
    public static class QualityTierBenchmark
    {
        public const int Version = 1;

        const int TargetSize = 1024;
        const int NoiseSize = 256;
        const int MaxDraws = 1024;
        const double MinTimedSeconds = 0.03;
        const double SettleSeconds = 0.8;
        const double BudgetSeconds = 2.0;
        const int MinSamples = 3;

        public static float Run(Shader shader)
        {
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[QualityTiers] Benchmark shader is missing or unsupported on this GPU.");
                return float.NaN;
            }

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D noise = CreateNoiseTexture();
            material.SetTexture("_NoiseTex", noise);
            var target = new RenderTexture(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32)
            {
                name = "Quality Tier Benchmark",
                hideFlags = HideFlags.HideAndDontSave,
            };
            target.Create();
            var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var commands = new CommandBuffer { name = "Quality Tier Benchmark" };

            try
            {
                var total = Stopwatch.StartNew();

                // Untimed first pass: shader compilation and pipeline creation would otherwise land in the first sample.
                Measure(commands, material, target, readback, 1);

                // Double the work until a pass takes long enough to time; this also lets mobile GPU clocks ramp up.
                int draws = 1;
                double seconds = Measure(commands, material, target, readback, draws);
                while (seconds < MinTimedSeconds && draws < MaxDraws && total.Elapsed.TotalSeconds < BudgetSeconds)
                {
                    draws *= 2;
                    seconds = Measure(commands, material, target, readback, draws);
                }

                // Keep the fastest of several samples: anything slower was interrupted by something else.
                double best = seconds;
                double settleUntil = total.Elapsed.TotalSeconds + SettleSeconds;
                for (int samples = 1; samples < MinSamples || total.Elapsed.TotalSeconds < settleUntil; samples++)
                {
                    if (total.Elapsed.TotalSeconds >= BudgetSeconds) break;
                    best = Math.Min(best, Measure(commands, material, target, readback, draws));
                }

                double megapixels = (double)TargetSize * TargetSize * draws / 1e6;
                return (float)(megapixels / best);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return float.NaN;
            }
            finally
            {
                commands.Release();
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(noise);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static double Measure(CommandBuffer commands, Material material, RenderTexture target, Texture2D readback, int draws)
        {
            commands.Clear();
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(false, true, Color.clear);
            for (int i = 0; i < draws; i++)
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);

            var timer = Stopwatch.StartNew();
            Graphics.ExecuteCommandBuffer(commands);
            WaitForGpu(target, readback);
            return timer.Elapsed.TotalSeconds;
        }

        // Reading a pixel back blocks until the GPU has finished every draw before it.
        static void WaitForGpu(RenderTexture target, Texture2D readback)
        {
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1);
                request.WaitForCompletion();
                return;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            RenderTexture.active = previous;
        }

        static Texture2D CreateNoiseTexture()
        {
            var texture = new Texture2D(NoiseSize, NoiseSize, TextureFormat.RGBA32, false)
            {
                name = "Quality Tier Benchmark Noise",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var random = new System.Random(1234);
            var pixels = new Color32[NoiseSize * NoiseSize];
            var bytes = new byte[4];
            for (int i = 0; i < pixels.Length; i++)
            {
                random.NextBytes(bytes);
                pixels[i] = new Color32(bytes[0], bytes[1], bytes[2], bytes[3]);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
