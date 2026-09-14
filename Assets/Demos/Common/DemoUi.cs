using System;
using UnityEngine;
using UnityEngine.UI;

namespace Jongreul.AuthorityRequest.Demos
{
    /// <summary>
    /// 데모 UI를 코드로 조립하는 도우미. 씬 파일에는 부트스트랩 오브젝트 하나만 두고
    /// 화면은 전부 여기서 만든다(씬 diff가 작고 리뷰하기 쉽다).
    /// </summary>
    public static class DemoUi
    {
        public static readonly Color Background = new Color(0.09f, 0.1f, 0.12f);
        public static readonly Color Panel = new Color(0.14f, 0.15f, 0.18f);
        public static readonly Color PanelLight = new Color(0.2f, 0.22f, 0.26f);
        public static readonly Color TextColor = new Color(0.92f, 0.93f, 0.95f);
        public static readonly Color Muted = new Color(0.6f, 0.63f, 0.68f);
        public static readonly Color Ready = new Color(0.2f, 0.68f, 0.42f);
        public static readonly Color Pending = new Color(0.93f, 0.7f, 0.2f);
        public static readonly Color Cooldown = new Color(0.35f, 0.38f, 0.45f);
        public static readonly Color Danger = new Color(0.85f, 0.3f, 0.3f);
        public static readonly Color Accent = new Color(0.35f, 0.55f, 0.95f);

        static Font _font;

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

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

        public static Image CreatePanel(Transform parent, string name, Color color)
        {
            RectTransform rect = CreateRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text CreateText(Transform parent, string name, string value, int size = 18,
            TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            RectTransform rect = CreateRect(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color ?? TextColor;
            text.text = value;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, Action onClick,
            Color? color = null, int fontSize = 18)
        {
            Image image = CreatePanel(parent, name, color ?? PanelLight);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            Text text = CreateText(image.transform, "Label", label, fontSize, TextAnchor.MiddleCenter);
            text.rectTransform.Place(Vector2.zero, Vector2.one);
            return button;
        }

        /// <summary>이름 + 슬라이더 + 값 표시 한 줄.</summary>
        public static Slider CreateSlider(Transform parent, string label, float min, float max, float value,
            Func<float, string> format, Action<float> onChanged)
        {
            RectTransform row = CreateRect(parent, label);
            // 세로 레이아웃 안에서 높이를 갖는 것은 행이다(슬라이더는 행 안에 앵커로 배치).
            var rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 30;
            rowLayout.minHeight = 30;
            Text name = CreateText(row, "Name", label, 16, TextAnchor.MiddleLeft, Muted);
            name.rectTransform.Place(new Vector2(0, 0), new Vector2(0.38f, 1));
            Text valueText = CreateText(row, "Value", format(value), 16, TextAnchor.MiddleRight);
            valueText.rectTransform.Place(new Vector2(0.82f, 0), new Vector2(1, 1));

            GameObject sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderGo.transform.SetParent(row, false);
            ((RectTransform)sliderGo.transform).Place(new Vector2(0.4f, 0.3f), new Vector2(0.8f, 0.7f));
            foreach (Image image in sliderGo.GetComponentsInChildren<Image>())
                image.color = image.name == "Handle" ? TextColor : image.name == "Fill" ? Accent : PanelLight;

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
                image.color = image.name == "Checkmark" ? Accent : PanelLight;

            Text text = toggleGo.GetComponentInChildren<Text>();
            text.font = Font;
            text.fontSize = 16;
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
    }
}
