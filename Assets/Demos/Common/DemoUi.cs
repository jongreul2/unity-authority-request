using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jongreul.AuthorityRequest.Demos
{
    /// <summary>
    /// 데모 UI를 코드로 조립하는 도우미. 씬 파일에는 부트스트랩 오브젝트 하나만 두고
    /// 화면은 전부 여기서 만든다(씬 diff가 작고 리뷰하기 쉽다).
    /// 둥근 모서리·원 스프라이트는 실행 중에 텍스처로 만들어 외부 에셋 없이 쓴다.
    /// </summary>
    public static class DemoUi
    {
        public static readonly Color Background = new Color(0.07f, 0.08f, 0.1f);
        public static readonly Color Panel = new Color(0.12f, 0.13f, 0.16f);
        public static readonly Color PanelLight = new Color(0.17f, 0.18f, 0.22f);
        // 반투명 흰색은 리니어 색공간에서 생각보다 밝게 섞이므로 불투명한 어두운 색을 쓴다.
        public static readonly Color Track = new Color(0.19f, 0.2f, 0.25f);
        public static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f);
        public static readonly Color Muted = new Color(0.56f, 0.6f, 0.67f);
        public static readonly Color Ready = new Color(0.22f, 0.74f, 0.47f);
        public static readonly Color Pending = new Color(0.96f, 0.72f, 0.24f);
        public static readonly Color Cooldown = new Color(0.4f, 0.55f, 0.95f);
        public static readonly Color Danger = new Color(0.93f, 0.36f, 0.36f);
        public static readonly Color Accent = new Color(0.36f, 0.52f, 0.98f);

        static readonly Dictionary<int, Sprite> RoundedSprites = new Dictionary<int, Sprite>();
        static Sprite _circle;
        static Font _font;

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

        #region 스프라이트

        /// <summary>모서리 반지름 radius(px)인 9-슬라이스 둥근 사각형.</summary>
        public static Sprite Rounded(int radius)
        {
            if (RoundedSprites.TryGetValue(radius, out Sprite cached) && cached != null)
                return cached;

            int size = radius * 2 + 4;
            Texture2D texture = CreateTexture(size, (x, y) =>
            {
                float qx = Mathf.Clamp(x, radius, size - radius);
                float qy = Mathf.Clamp(y, radius, size - radius);
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(qx, qy));
                return Mathf.Clamp01(radius + 0.5f - distance);
            });

            float border = radius + 1;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            RoundedSprites[radius] = sprite;
            return sprite;
        }

        public static Sprite Circle()
        {
            if (_circle != null)
                return _circle;

            const int size = 64;
            const float radius = size / 2f;
            Texture2D texture = CreateTexture(size, (x, y) =>
                Mathf.Clamp01(radius - Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius)) + 0.5f));
            _circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _circle.hideFlags = HideFlags.HideAndDontSave;
            return _circle;
        }

        static Texture2D CreateTexture(int size, Func<float, float, float> alphaAt)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte alpha = (byte)Mathf.RoundToInt(alphaAt(x + 0.5f, y + 0.5f) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        #endregion

        #region 조립

        /// <summary>카메라에 그려지는 캔버스. 캡처 도구가 카메라 렌더 결과를 그대로 저장할 수 있다.</summary>
        public static RectTransform CreateCanvas(Camera camera, string name = "Canvas")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        public static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(this RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        /// <summary>radius가 0보다 크면 둥근 모서리 패널.</summary>
        public static Image CreatePanel(Transform parent, string name, Color color, int radius = 0)
        {
            RectTransform rect = CreateRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (radius > 0)
            {
                image.sprite = Rounded(radius);
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        public static Text CreateText(Transform parent, string name, string value, int size = 18,
            TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null, bool bold = false)
        {
            RectTransform rect = CreateRect(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = anchor;
            text.color = color ?? TextColor;
            text.text = value;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, Action onClick,
            Color? color = null, int fontSize = 18, int radius = 10)
        {
            Image image = CreatePanel(parent, name, color ?? PanelLight, radius);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            Text text = CreateText(image.transform, "Label", label, fontSize, TextAnchor.MiddleCenter, bold: true);
            text.rectTransform.Place(Vector2.zero, Vector2.one);
            return button;
        }

        /// <summary>이름 + 슬라이더 + 값 표시 한 줄. 세로 레이아웃에서 높이를 갖는 것은 행이다.</summary>
        public static Slider CreateSlider(Transform parent, string label, float min, float max, float value,
            Func<float, string> format, Action<float> onChanged)
        {
            RectTransform row = CreateRect(parent, label);
            var rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 30;
            rowLayout.minHeight = 30;

            CreateText(row, "Name", label, 15, TextAnchor.MiddleLeft, Muted).rectTransform
                .Place(new Vector2(0, 0), new Vector2(0.36f, 1));
            Text valueText = CreateText(row, "Value", format(value), 15, TextAnchor.MiddleRight, bold: true);
            valueText.rectTransform.Place(new Vector2(0.82f, 0), new Vector2(1, 1));

            GameObject sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderGo.transform.SetParent(row, false);
            ((RectTransform)sliderGo.transform).Place(new Vector2(0.37f, 0.2f), new Vector2(0.8f, 0.8f));

            foreach (Image image in sliderGo.GetComponentsInChildren<Image>())
            {
                switch (image.name)
                {
                    case "Handle":
                        image.sprite = Circle();
                        image.color = TextColor;
                        image.rectTransform.sizeDelta = new Vector2(18, 0);
                        break;
                    case "Fill":
                        image.sprite = Rounded(4);
                        image.type = Image.Type.Sliced;
                        image.color = Accent;
                        break;
                    default:
                        image.sprite = Rounded(4);
                        image.type = Image.Type.Sliced;
                        image.color = Track;
                        break;
                }
            }

            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(v =>
            {
                valueText.text = format(v);
                onChanged(v);
            });
            return slider;
        }

        public static Toggle CreateToggle(Transform parent, string label, bool value, Action<bool> onChanged)
        {
            GameObject toggleGo = DefaultControls.CreateToggle(new DefaultControls.Resources());
            toggleGo.transform.SetParent(parent, false);
            toggleGo.name = label;

            foreach (Image image in toggleGo.GetComponentsInChildren<Image>())
            {
                bool check = image.name == "Checkmark";
                image.sprite = Rounded(check ? 3 : 5);
                image.type = Image.Type.Sliced;
                image.color = check ? Danger : PanelLight;
                if (check)
                    image.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            }

            Text text = toggleGo.GetComponentInChildren<Text>();
            text.font = Font;
            text.fontSize = 15;
            text.color = TextColor;
            text.text = label;

            var toggle = toggleGo.GetComponent<Toggle>();
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(v => onChanged(v));
            return toggle;
        }

        /// <summary>세로로 쌓는 레이아웃. 자식은 LayoutElement 높이를 따른다.</summary>
        public static VerticalLayoutGroup Stack(this RectTransform rect, float spacing = 8, int padding = 12)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return layout;
        }

        public static T Height<T>(this T component, float height) where T : Component
        {
            LayoutElement element = component.GetComponent<LayoutElement>();
            if (element == null)
                element = component.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            return component;
        }

        #endregion
    }
}
