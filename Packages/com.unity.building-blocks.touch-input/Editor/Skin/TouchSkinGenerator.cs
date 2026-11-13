using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// Draws the default skin's four 256 px sprites into <c>Runtime/UGUI/Skin/</c>.
    /// They are checked in, so run this only to redesign them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bright parts are white so the view's tint recolours them. Shadows are low-opacity black, which a tint
    /// leaves dark, so a control stays readable on bright and dark backgrounds.
    /// </para>
    /// <para>Regenerate from a live editor that mounts this package:</para>
    /// <code>
    /// unity command eval --timeout 30000 --code 'System.Type.GetType("Unity.BuildingBlocks.TouchInput.Editor.TouchSkinGenerator, Unity.BuildingBlocks.TouchInput.Editor").GetMethod("Generate").Invoke(null, null); return 0;'
    /// </code>
    /// </remarks>
    static class TouchSkinGenerator
    {
        public const int Size = 256;

        /// <summary>Writes the four PNGs next to the UGUI assembly, imports them as sprites, and returns the folder.</summary>
        public static string Generate()
        {
            string folder = SkinFolder();
            Directory.CreateDirectory(folder);

            Write(folder, TouchSkin.JoystickRingFile, JoystickRing());
            Write(folder, TouchSkin.JoystickKnobFile, JoystickKnob());
            Write(folder, TouchSkin.ButtonDiscFile, ButtonDisc());
            Write(folder, TouchSkin.ButtonRingFile, ButtonRing());
            return folder;
        }

        /// <summary>The <c>Runtime/UGUI/Skin</c> folder, found from the UGUI assembly definition.</summary>
        static string SkinFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("Unity.BuildingBlocks.TouchInput.UGUI t:asmdef"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == "Unity.BuildingBlocks.TouchInput.UGUI.asmdef")
                    return Path.GetDirectoryName(path).Replace('\\', '/') + "/Skin";
            }

            throw new InvalidOperationException("The Touch Input UGUI assembly definition was not found.");
        }

        static void Write(string folder, string file, Color[] pixels)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            string path = folder + "/" + file;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.spritePixelsPerUnit = 100f;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsOutline = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        // Distances are in pixels of the 256 px canvas; every edge is anti-aliased over about a pixel.

        const float k_C = Size * 0.5f;

        /// <summary>
        /// The joystick's base: a white rim, four small chevrons pointing the ways the stick goes, a dark well that
        /// deepens toward the rim, and a soft shadow outside the rim.
        /// </summary>
        static Color[] JoystickRing()
        {
            const float rimOuter = 116f, rimWidth = 6.5f;
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = Distance(x, y, k_C, k_C);
                    Color c = Clear;

                    // Shadow outside the rim, falling off over ~10 px.
                    float shadow = 0.30f * (1f - Smooth(rimOuter - 1f, rimOuter + 11f, r)) * Step(rimOuter - 2f, r);
                    c = Over(c, Black(shadow));

                    // The well: dark, slightly darker toward the rim so the centre reads as the rest position.
                    float inside = 1f - Smooth(rimOuter - rimWidth - 0.5f, rimOuter - rimWidth + 0.5f, r);
                    float t = Mathf.Clamp01(r / (rimOuter - rimWidth));
                    c = Over(c, Black(inside * (0.16f + 0.14f * t * t)));

                    // A faint inner glow just inside the rim.
                    float glow = 0.10f * Band(r, rimOuter - rimWidth - 10f, rimOuter - rimWidth, 6f);
                    c = Over(c, White(glow));

                    // Chevrons at N, E, S, W.
                    c = Over(c, White(0.55f * Chevrons(x, y, 88f, 12f, 4.2f)));

                    // The rim.
                    c = Over(c, White(0.92f * Band(r, rimOuter - rimWidth, rimOuter, 1f)));

                    pixels[y * Size + x] = c;
                }

            return pixels;
        }

        /// <summary>
        /// The knob: a white disc lit from above (a touch brighter at the top), a faint darker edge just inside its
        /// rim, and a soft drop shadow below it.
        /// </summary>
        static Color[] JoystickKnob()
        {
            const float radius = 98f;
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    // Texture rows go up: y = 0 is the bottom, so "below" is a smaller y.
                    float rShadow = Distance(x, y, k_C, k_C - 7f);
                    float r = Distance(x, y, k_C, k_C + 3f);
                    Color c = Clear;

                    float shadow = 0.38f * (1f - Smooth(radius - 14f, radius + 16f, rShadow));
                    c = Over(c, Black(shadow));

                    float disc = 1f - Smooth(radius - 0.75f, radius + 0.75f, r);
                    if (disc > 0f)
                    {
                        float vertical = Mathf.Clamp01((y - (k_C + 3f - radius)) / (2f * radius));   // 0 bottom .. 1 top
                        float grey = Mathf.Lerp(0.88f, 1f, Mathf.SmoothStep(0f, 1f, vertical));
                        float edge = Smooth(radius - 9f, radius - 1f, r);                              // the faint inner edge
                        grey = Mathf.Lerp(grey, 0.80f, edge * 0.55f);
                        c = Over(c, new Color(grey, grey, grey, disc));
                    }

                    pixels[y * Size + x] = c;
                }

            return pixels;
        }

        /// <summary>The button's face: a white disc, slightly darker toward its edge. The view tints it.</summary>
        static Color[] ButtonDisc()
        {
            const float radius = 112f;
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = Distance(x, y, k_C, k_C);
                    float disc = 1f - Smooth(radius - 0.75f, radius + 0.75f, r);
                    float t = Mathf.Clamp01(r / radius);
                    float grey = Mathf.Lerp(1f, 0.90f, t * t);
                    pixels[y * Size + x] = disc <= 0f ? Clear : new Color(grey, grey, grey, disc);
                }

            return pixels;
        }

        /// <summary>The button's rim: a white ring with a soft shadow outside it and a faint one inside, drawn over the disc.</summary>
        static Color[] ButtonRing()
        {
            const float rimOuter = 116f, rimWidth = 6f;
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = Distance(x, y, k_C, k_C);
                    Color c = Clear;
                    c = Over(c, Black(0.28f * (1f - Smooth(rimOuter - 1f, rimOuter + 10f, r)) * Step(rimOuter - 2f, r)));
                    c = Over(c, Black(0.14f * Band(r, rimOuter - rimWidth - 8f, rimOuter - rimWidth, 5f)));
                    c = Over(c, White(0.95f * Band(r, rimOuter - rimWidth, rimOuter, 1f)));
                    pixels[y * Size + x] = c;
                }

            return pixels;
        }

        static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        static Color White(float alpha) => new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));

        static Color Black(float alpha) => new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));

        /// <summary>Straight-alpha "over" compositing.</summary>
        static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0f)
                return Clear;

            Color rgb = (over * over.a + under * under.a * (1f - over.a)) / a;
            return new Color(rgb.r, rgb.g, rgb.b, a);
        }

        static float Distance(float x, float y, float cx, float cy)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static float Smooth(float edge0, float edge1, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, v));

        static float Step(float edge, float v) => v >= edge ? 1f : 0f;

        /// <summary>Returns 1 between <paramref name="inner"/> and <paramref name="outer"/>, fading to 0 over <paramref name="soft"/> px each side.</summary>
        static float Band(float r, float inner, float outer, float soft) =>
            Smooth(inner - soft * 0.5f, inner + soft * 0.5f, r) * (1f - Smooth(outer - soft * 0.5f, outer + soft * 0.5f, r));

        /// <summary>Four chevrons at <paramref name="radius"/> pointing outward, each two strokes of <paramref name="half"/> px, <paramref name="width"/> px thick.</summary>
        static float Chevrons(int x, int y, float radius, float half, float width)
        {
            float px = x + 0.5f - k_C, py = y + 0.5f - k_C;
            float best = 0f;
            for (int i = 0; i < 4; i++)
            {
                // Rotate the point so this chevron points up (+y).
                float angle = i * Mathf.PI * 0.5f;
                float cos = Mathf.Cos(-angle), sin = Mathf.Sin(-angle);
                float u = px * cos - py * sin, v = px * sin + py * cos;

                var tip = new Vector2(0f, radius + half * 0.45f);
                var left = new Vector2(-half, radius - half * 0.45f);
                var right = new Vector2(half, radius - half * 0.45f);
                float d = Mathf.Min(SegmentDistance(new Vector2(u, v), left, tip), SegmentDistance(new Vector2(u, v), right, tip));
                best = Mathf.Max(best, 1f - Smooth(width * 0.5f - 0.75f, width * 0.5f + 0.75f, d));
            }

            return best;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
