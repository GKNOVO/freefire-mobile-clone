using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BR.Bootstrap
{
    /// <summary>Barra de progresso/vida que não depende de sprite (escala por anchor).</summary>
    public sealed class RuntimeBar : MonoBehaviour
    {
        public Image Fill;
        public RectTransform FillRect;

        public void SetValue(float value01)
        {
            if (FillRect == null) return;

            var value = Mathf.Clamp01(value01);
            FillRect.anchorMin = new Vector2(0f, 0f);
            FillRect.anchorMax = new Vector2(value, 1f);
            FillRect.offsetMin = Vector2.zero;
            FillRect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Fábrica de UI por código (uGUI + Text legado).
    ///
    /// Por que Text legado e não TMP? TMP exige os "TMP Essentials" importados no projeto
    /// (asset de fonte). Em build de CI ninguém clica em "Import TMP Essentials", então
    /// a UI gerada por código usa o Text nativo, que só precisa da fonte built-in.
    /// </summary>
    public static class RuntimeUI
    {
        private static Font _font;
        private static Sprite _circleSprite;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;

                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 24);

                return _font;
            }
        }

        public static Sprite CircleSprite
        {
            get
            {
                if (_circleSprite != null) return _circleSprite;

                const int size = 128;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "RuntimeCircle",
                    wrapMode = TextureWrapMode.Clamp
                };

                var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
                var radius = size * 0.5f;

                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var distance = Vector2.Distance(new Vector2(x, y), center);
                        var alpha = Mathf.Clamp01(radius - distance);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }

                texture.Apply();
                _circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
                return _circleSprite;
            }
        }

        // ------------------------------------------------------------ Estrutura

        public static Canvas CreateCanvas(string canvasName, int sortingOrder)
        {
            var go = new GameObject(canvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        public static void CreateEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static RectTransform CreateRect(
            Transform parent, string rectName,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(rectName, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        // ------------------------------------------------------------ Elementos

        public static Image CreateImage(
            Transform parent, string imageName, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var rect = CreateRect(parent, imageName, anchorMin, anchorMax, pivot, anchoredPosition, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Image CreateFullScreen(Transform parent, string imageName, Color color)
        {
            var image = CreateImage(parent, imageName, color, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(image.rectTransform);
            return image;
        }

        public static Text CreateText(
            Transform parent, string textName, string content, int fontSize, TextAnchor alignment, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var rect = CreateRect(parent, textName, anchorMin, anchorMax, pivot, anchoredPosition, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button CreateButton(
            Transform parent, string buttonName, string label, int fontSize,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, Color background, UnityAction onClick)
        {
            var rect = CreateRect(parent, buttonName, anchorMin, anchorMax, pivot, anchoredPosition, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = background;
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);

            var text = CreateText(rect, "Label", label, fontSize, TextAnchor.MiddleCenter, Color.white,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(text.rectTransform, 8f);

            return button;
        }

        public static RuntimeBar CreateBar(
            Transform parent, string barName, Vector2 size, Color fillColor, Color backColor,
            Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition)
        {
            var background = CreateImage(parent, barName, backColor, anchor, anchor, pivot, anchoredPosition, size);

            var fillRect = CreateRect(background.transform, "Fill", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(fillRect, 2f);

            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;

            var bar = background.gameObject.AddComponent<RuntimeBar>();
            bar.Fill = fill;
            bar.FillRect = fillRect;
            bar.SetValue(1f);

            return bar;
        }

        public static InputField CreateInputField(
            Transform parent, string fieldName, string placeholderText,
            Vector2 anchoredPosition, Vector2 size)
        {
            var background = CreateImage(parent, fieldName, new Color(0.12f, 0.14f, 0.18f, 0.95f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                anchoredPosition, size);
            background.raycastTarget = true;

            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 16;

            var text = CreateText(background.transform, "Text", "", 34, TextAnchor.MiddleLeft, Color.white,
                Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(text.rectTransform, 16f);
            text.supportRichText = false;

            var placeholder = CreateText(background.transform, "Placeholder", placeholderText, 34, TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.35f), Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(placeholder.rectTransform, 16f);

            input.textComponent = text;
            input.placeholder = placeholder;

            return input;
        }
    }
}
