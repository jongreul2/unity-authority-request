using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jongreul.AuthorityRequest.Demos
{
    /// <summary>
    /// 최근 몇 초 동안의 이벤트를 레인별 점으로 흘려 보여 준다. 오른쪽 끝이 지금이다.
    /// "탭은 많은데 요청은 적다"를 한눈에 보이게 하려는 용도.
    /// </summary>
    public sealed class TimelineView
    {
        struct Mark
        {
            public Image Dot;
            public double Time;
        }

        const float DotSize = 12f;
        const float LabelWidth = 92f;

        readonly RectTransform[] _tracks;
        readonly List<Mark> _marks = new List<Mark>();
        readonly Stack<Image> _pool = new Stack<Image>();
        readonly Func<double> _now;
        readonly float _windowSeconds;

        public TimelineView(RectTransform root, IReadOnlyList<string> lanes, Func<double> now, float windowSeconds)
        {
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _windowSeconds = windowSeconds;
            _tracks = new RectTransform[lanes.Count];

            float laneHeight = 1f / lanes.Count;
            for (int i = 0; i < lanes.Count; i++)
            {
                float top = 1f - i * laneHeight;
                RectTransform lane = DemoUi.CreateRect(root, lanes[i])
                    .Place(new Vector2(0, top - laneHeight), new Vector2(1, top));

                DemoUi.CreateText(lane, "Label", lanes[i], 14, TextAnchor.MiddleLeft, DemoUi.Muted).rectTransform
                    .Place(Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(LabelWidth, 0));

                Image track = DemoUi.CreatePanel(lane, "Track", DemoUi.Track, 8);
                track.raycastTarget = false;
                _tracks[i] = track.rectTransform.Place(new Vector2(0, 0.14f), new Vector2(1, 0.86f),
                    new Vector2(LabelWidth, 0), new Vector2(-8, 0));

                // 지금 위치 표시선
                Image nowLine = DemoUi.CreatePanel(_tracks[i], "Now", new Color(1, 1, 1, 0.25f));
                nowLine.raycastTarget = false;
                nowLine.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 0), Vector2.zero);
            }
        }

        public void Add(int lane, Color color)
        {
            Image dot = _pool.Count > 0 ? _pool.Pop() : CreateDot();
            dot.color = color;
            dot.rectTransform.SetParent(_tracks[lane], false);
            dot.gameObject.SetActive(true);
            _marks.Add(new Mark { Dot = dot, Time = _now() });
            Position(dot.rectTransform, 0f);
        }

        /// <summary>매 프레임 호출. 오래된 점은 풀로 돌려보낸다.</summary>
        public void Update()
        {
            double now = _now();
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                double age = now - _marks[i].Time;
                if (age > _windowSeconds)
                {
                    Image dot = _marks[i].Dot;
                    dot.gameObject.SetActive(false);
                    _pool.Push(dot);
                    _marks.RemoveAt(i);
                    continue;
                }

                Position(_marks[i].Dot.rectTransform, (float)(age / _windowSeconds));
            }
        }

        static void Position(RectTransform dot, float age01)
        {
            // 새 점은 오른쪽 끝(지금)에서 시작해 왼쪽으로 흐른다. 반쯤 잘리지 않게 점 반지름만큼 안쪽에 둔다.
            float x = 1f - age01;
            dot.anchorMin = dot.anchorMax = new Vector2(x, 0.5f);
            dot.anchoredPosition = new Vector2(-DotSize * 0.5f - 4f, 0);
        }

        Image CreateDot()
        {
            Image dot = DemoUi.CreatePanel(_tracks[0], "Dot", Color.white);
            dot.sprite = DemoUi.Circle();
            dot.raycastTarget = false;
            dot.rectTransform.sizeDelta = new Vector2(DotSize, DotSize);
            return dot;
        }
    }
}
