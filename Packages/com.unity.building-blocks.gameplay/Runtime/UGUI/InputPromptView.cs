using UnityEngine;
using UnityEngine.UI;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// The built-in look of an <see cref="InputPrompt"/>: a hint dot, then a glyph box and a label, drawn on
    /// one shared screen-space canvas and pinned to the prompt's anchor with a <see cref="WorldAnchor"/>.
    /// </summary>
    sealed class InputPromptView : IInputPromptView
    {
        const float k_FadeSpeed = 10f;
        const string k_CanvasName = "Input Prompts";

        static Canvas s_Canvas;
        static Sprite s_Dot;

        readonly GameObject m_Root;
        readonly WorldAnchor m_Anchor;
        readonly CanvasGroup m_DotGroup;
        readonly CanvasGroup m_FullGroup;
        readonly RectTransform m_FullRect;
        readonly Image m_Dot;
        readonly GameObject m_GlyphBox;
        readonly Image m_GlyphBackground;
        readonly Image m_Glyph;
        readonly Text m_GlyphText;
        readonly GameObject m_LabelBox;
        readonly Image m_LabelBackground;
        readonly Text m_Label;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => InputPrompt.s_DefaultViewFactory = prompt => new InputPromptView(prompt);

        InputPromptView(InputPrompt prompt)
        {
            m_Root = new GameObject(prompt.name + " Prompt", typeof(RectTransform));
            m_Root.transform.SetParent(SharedCanvas().transform, false);
            m_Anchor = m_Root.AddComponent<WorldAnchor>();

            m_Dot = NewImage("Hint Dot", m_Root.transform);
            m_DotGroup = m_Dot.gameObject.AddComponent<CanvasGroup>();

            var full = new GameObject("Glyph And Label", typeof(RectTransform));
            m_FullRect = (RectTransform)full.transform;
            m_FullRect.SetParent(m_Root.transform, false);
            m_FullRect.pivot = new Vector2(0f, 0.5f);
            m_FullGroup = full.AddComponent<CanvasGroup>();
            var row = full.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            row.spacing = 2f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            var fit = full.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            m_GlyphBackground = NewImage("Glyph", m_FullRect);
            m_GlyphBox = m_GlyphBackground.gameObject;
            m_GlyphBox.AddComponent<LayoutElement>();
            m_Glyph = NewImage("Icon", m_GlyphBox.transform);
            m_Glyph.preserveAspect = true;
            Stretch(m_Glyph.rectTransform, 0.08f);
            m_GlyphText = NewText("Glyph Text", m_GlyphBox.transform, TextAnchor.MiddleCenter);
            Stretch(m_GlyphText.rectTransform, 0f);

            // The label box sizes to its text; the background stretches behind it outside the layout,
            // because an Image would report its sprite's full pixel size as its preferred width.
            var labelBox = new GameObject("Label", typeof(RectTransform));
            labelBox.transform.SetParent(m_FullRect, false);
            m_LabelBox = labelBox;
            m_LabelBackground = NewImage("Background", labelBox.transform);
            m_LabelBackground.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch(m_LabelBackground.rectTransform, 0f);
            var pad = labelBox.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset(22, 26, 8, 10);
            pad.childAlignment = TextAnchor.MiddleLeft;
            pad.childControlWidth = true;
            pad.childControlHeight = true;
            m_Label = NewText("Text", labelBox.transform, TextAnchor.MiddleLeft);

            m_DotGroup.alpha = 0f;
            m_FullGroup.alpha = 0f;
        }

        public void Present(InputPromptState state, in InputPromptContent content)
        {
            // A scene load destroys the shared canvas, and this view with it.
            if (m_Root == null)
                return;

            InputPromptStyle style = content.Style;
            m_Anchor.Target = content.Anchor;

            m_Dot.sprite = style.HintDot != null ? style.HintDot : DefaultDot();
            m_Dot.color = style.TextColor;
            m_Dot.rectTransform.sizeDelta = Vector2.one * style.HintDotSize;

            m_FullRect.anchoredPosition = style.Offset + new Vector2(0f, style.GlyphSize * 0.5f);

            bool hasGlyph = content.Glyph != null || !string.IsNullOrEmpty(content.GlyphText);
            m_GlyphBox.SetActive(hasGlyph);
            var glyphLayout = m_GlyphBox.GetComponent<LayoutElement>();
            glyphLayout.preferredHeight = style.GlyphSize;
            glyphLayout.preferredWidth = style.GlyphSize;
            glyphLayout.minWidth = style.GlyphSize;
            Paint(m_GlyphBackground, style.GlyphBackground);
            m_Glyph.enabled = content.Glyph != null;
            m_Glyph.sprite = content.Glyph;
            m_GlyphText.enabled = content.Glyph == null;
            m_GlyphText.text = content.GlyphText;
            Style(m_GlyphText, style, Mathf.RoundToInt(style.FontSize * 0.85f));

            Paint(m_LabelBackground, style.LabelBackground);
            m_LabelBox.SetActive(!string.IsNullOrEmpty(content.Label));
            m_Label.text = content.Label;
            Style(m_Label, style, style.FontSize);

            float step = k_FadeSpeed * Time.unscaledDeltaTime;
            m_DotGroup.alpha = Mathf.MoveTowards(m_DotGroup.alpha, state != InputPromptState.Hidden ? 1f : 0f, step);
            m_FullGroup.alpha = Mathf.MoveTowards(m_FullGroup.alpha, state == InputPromptState.Full ? 1f : 0f, step);
        }

        public bool Contains(Vector2 screenPoint) =>
            m_Root != null && m_FullGroup.alpha > 0.5f && m_Anchor.IsInFront &&
            RectTransformUtility.RectangleContainsScreenPoint(m_FullRect, screenPoint, null);

        public void Release()
        {
            if (m_Root != null)
                Object.Destroy(m_Root);
        }

        static void Paint(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : new Color(0.14f, 0.2f, 0.33f, 0.9f);
        }

        static void Style(Text text, InputPromptStyle style, int size)
        {
            text.font = style.Font != null ? style.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = style.TextColor;
        }

        static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        static Text NewText(string name, Transform parent, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.alignment = alignment;
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = new Vector2(inset, inset);
            rect.anchorMax = new Vector2(1f - inset, 1f - inset);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static Canvas SharedCanvas()
        {
            if (s_Canvas != null)
                return s_Canvas;
            var go = new GameObject(k_CanvasName, typeof(Canvas), typeof(CanvasScaler));
            s_Canvas = go.GetComponent<Canvas>();
            s_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            s_Canvas.sortingOrder = 100;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return s_Canvas;
        }

        static Sprite DefaultDot()
        {
            if (s_Dot != null)
                return s_Dot;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius;
                    // Solid core with a soft glow falling off to the edge.
                    float a = d < 0.45f ? 1f : Mathf.Clamp01(1f - (d - 0.45f) / 0.55f) * 0.6f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            texture.Apply();
            s_Dot = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            s_Dot.hideFlags = HideFlags.HideAndDontSave;
            return s_Dot;
        }
    }
}
