using System.Collections;
using System.Collections.Generic;
using System.Text;
using Jongreul.AuthorityRequest.Gate;
using UnityEngine;
using UnityEngine.UI;

namespace Jongreul.AuthorityRequest.Demos
{
    /// <summary>
    /// 응답 게이트 데모. 버튼을 연타해도 서버 요청은 한 번만 나간다.
    /// "Bypass gate"를 켜면 게이트 없이 누를 때마다 요청을 보내고, 서버 쿨타임이 그 요청들을 거절하는 것이 보인다.
    /// </summary>
    public sealed class GateDemo : MonoBehaviour
    {
        const int NaiveActionOffset = 100;
        const int LogLines = 12;
        const int LaneTaps = 0;
        const int LaneRequests = 1;
        const int LaneServer = 2;

        static readonly string[] SkillNames = { "Skill A", "Skill B", "Skill C" };
        static readonly double[] SkillCooldowns = { 1.0, 2.5, 5.0 };

        [SerializeField] float latencyMs = 250;
        [SerializeField] float jitterMs;
        [SerializeField] float failurePercent;
        [SerializeField] float duplicatePercent;

        sealed class SkillView
        {
            public Image Accent;
            public Text State;
            public Text Countdown;
            public RectTransform CooldownFill;
        }

        readonly Queue<string> _log = new Queue<string>();
        readonly StringBuilder _builder = new StringBuilder();

        UnityClock _clock;
        MockGateServer _server;
        ActionGate[] _gates;
        SkillView[] _views;
        TimelineView _timeline;
        Text _logText;
        Toggle _bypassToggle;
        Image _modePill;
        Text _modeText;
        Text _tapsValue;
        Text _requestsValue;
        Text _rejectedValue;
        Text _executedValue;
        Text _serverRejectedValue;
        Text _ignoredValue;
        bool _bypassGate;
        long _naiveSequence;
        int _taps;
        int _naiveRequests;
        int _serverRejected;
        double _startTime;

        public MockGateServer Server => _server;
        public IReadOnlyList<ActionGate> Gates => _gates;

        void Awake()
        {
            _clock = new UnityClock();
            _startTime = _clock.Now;
            _server = new MockGateServer(_clock, seed: 1);
            _server.RequestJudged += OnRequestJudged;

            _gates = new ActionGate[SkillNames.Length];
            for (int i = 0; i < _gates.Length; i++)
            {
                int actionId = i + 1;
                _server.SetCooldown(actionId, SkillCooldowns[i]);
                _server.SetCooldown(actionId + NaiveActionOffset, SkillCooldowns[i]);

                var gate = new ActionGate(actionId, _server, _clock);
                string skill = SkillNames[i];
                gate.Transitioned += t => OnTransition(skill, t);
                gate.ResponseIgnored += (r, reason) =>
                    Log($"{skill}  ignored {reason.ToString().ToLowerInvariant()} response  seq {r.Sequence}", DemoUi.Pending);
                _gates[i] = gate;
            }

            BuildUi();
            ApplyServerSettings();
        }

        void OnDestroy()
        {
            if (_gates == null)
                return;
            foreach (ActionGate gate in _gates)
                gate.Dispose();
        }

        void Update()
        {
            _server.Tick();
            foreach (ActionGate gate in _gates)
                gate.Tick();
            Refresh();
        }

        /// <summary>버튼 한 번 누름.</summary>
        public void Tap(int index)
        {
            _taps++;
            if (_bypassGate)
            {
                _naiveRequests++;
                _timeline?.Add(LaneTaps, DemoUi.Accent);
                _timeline?.Add(LaneRequests, DemoUi.Pending);
                _server.Send(new GateRequest(index + 1 + NaiveActionOffset, ++_naiveSequence));
                Log($"{SkillNames[index]}  sent without gate  seq {_naiveSequence}", DemoUi.Danger);
                return;
            }

            GateInputResult result = _gates[index].TryActivate();
            _timeline?.Add(LaneTaps, result == GateInputResult.Sent ? DemoUi.Accent : DemoUi.Muted);
            if (result != GateInputResult.Sent)
            {
                string why = result == GateInputResult.RejectedPending ? "waiting for server" : "cooling down";
                Log($"{SkillNames[index]}  tap blocked locally ({why})", DemoUi.Muted);
            }
        }

        /// <summary>연타 흉내. 간격 초마다 count번 누른다.</summary>
        public Coroutine Mash(int index, int count = 10, float interval = 0.04f) =>
            StartCoroutine(MashRoutine(index, count, interval));

        public void SetBypassGate(bool bypass)
        {
            _bypassGate = bypass;
            if (_bypassToggle != null)
                _bypassToggle.SetIsOnWithoutNotify(bypass);
            Log(bypass ? "Gate OFF: every tap becomes a request" : "Gate ON", bypass ? DemoUi.Danger : DemoUi.Ready);
        }

        public void SetLatency(float ms)
        {
            latencyMs = ms;
            ApplyServerSettings();
        }

        IEnumerator MashRoutine(int index, int count, float interval)
        {
            for (int i = 0; i < count; i++)
            {
                Tap(index);
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        void ApplyServerSettings()
        {
            _server.LatencySeconds = latencyMs / 1000.0;
            _server.LatencyJitterSeconds = jitterMs / 1000.0;
            _server.FailureRate = failurePercent / 100.0;
            _server.DuplicateRate = duplicatePercent / 100.0;
        }

        void OnTransition(string skill, GateTransition transition)
        {
            switch (transition.Reason)
            {
                case GateTransitionReason.RequestSent:
                    _timeline?.Add(LaneRequests, DemoUi.Pending);
                    Log($"{skill}  request sent  seq {transition.Sequence}", DemoUi.TextColor);
                    break;
                case GateTransitionReason.ServerSuccess:
                    Log($"{skill}  server OK → cooldown starts", DemoUi.Ready);
                    break;
                case GateTransitionReason.ServerFail:
                    Log($"{skill}  server said no → ready again", DemoUi.Danger);
                    break;
                case GateTransitionReason.PendingTimeout:
                    Log($"{skill}  no answer in time → ready again", DemoUi.Danger);
                    break;
                case GateTransitionReason.CooldownElapsed:
                    Log($"{skill}  ready", DemoUi.Muted);
                    break;
            }
        }

        void OnRequestJudged(GateRequest request, GateResponse response)
        {
            bool naive = request.ActionId > NaiveActionOffset;
            int index = (naive ? request.ActionId - NaiveActionOffset : request.ActionId) - 1;
            if (response.Success)
            {
                _timeline?.Add(LaneServer, DemoUi.Ready);
                Log($"SERVER  executed {SkillNames[index]}  (cooldown {response.CooldownSeconds:0.0}s)", DemoUi.Ready, bold: true);
            }
            else
            {
                _serverRejected++;
                _timeline?.Add(LaneServer, DemoUi.Danger);
                Log($"SERVER  rejected {SkillNames[index]}  ({response.FailReason})", DemoUi.Danger, bold: true);
            }
        }

        void Log(string line, Color color, bool bold = false)
        {
            string body = bold ? $"<b>{line}</b>" : line;
            _log.Enqueue($"<color={DemoUi.Hex(DemoUi.Muted)}>{_clock.Now - _startTime,6:0.00}</color>   <color={DemoUi.Hex(color)}>{body}</color>");
            while (_log.Count > LogLines)
                _log.Dequeue();

            if (_logText == null)
                return;
            _builder.Clear();
            foreach (string entry in _log)
                _builder.AppendLine(entry);
            _logText.text = _builder.ToString();
        }

        void Refresh()
        {
            int requestsSent = _naiveRequests;
            int rejectedLocally = 0;
            int ignored = 0;
            for (int i = 0; i < _gates.Length; i++)
            {
                ActionGate gate = _gates[i];
                requestsSent += gate.Stats.RequestsSent;
                rejectedLocally += gate.Stats.InputsRejected;
                ignored += gate.Stats.ResponsesIgnored;

                SkillView view = _views[i];
                float remaining = 0f;
                switch (gate.State)
                {
                    case GateState.Ready:
                        view.Accent.color = DemoUi.Ready;
                        view.State.text = "READY";
                        view.Countdown.text = "";
                        break;
                    case GateState.Pending:
                        view.Accent.color = DemoUi.Pending;
                        view.State.text = $"WAITING FOR SERVER · seq {gate.PendingSequence}";
                        view.Countdown.text = "…";
                        break;
                    default:
                        view.Accent.color = DemoUi.Cooldown;
                        view.State.text = $"COOLDOWN · length from server {gate.CooldownDuration:0.0}s";
                        view.Countdown.text = $"{gate.CooldownRemaining:0.0}s";
                        remaining = (float)(1 - gate.CooldownProgress);
                        break;
                }

                view.State.color = gate.State == GateState.Ready ? DemoUi.Ready : DemoUi.Muted;
                view.CooldownFill.anchorMax = new Vector2(remaining, 1);
            }

            _tapsValue.text = _taps.ToString();
            _requestsValue.text = requestsSent.ToString();
            _rejectedValue.text = rejectedLocally.ToString();
            _executedValue.text = _server.ActionsExecuted.ToString();
            _serverRejectedValue.text = _serverRejected.ToString();
            _ignoredValue.text = ignored.ToString();

            _modePill.color = _bypassGate ? DemoUi.Danger : DemoUi.Ready;
            _modeText.text = _bypassGate ? "GATE OFF" : "GATE ON";

            _timeline.Update();
        }

        #region UI

        void BuildUi()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = DemoUi.Background;
            }

            RectTransform canvas = DemoUi.CreateCanvas(cam);
            Image background = DemoUi.CreatePanel(canvas, "Background", DemoUi.Background);
            background.rectTransform.Place(Vector2.zero, Vector2.one);
            background.raycastTarget = false;

            BuildHeader(canvas);
            BuildControls(canvas);
            BuildTimelineAndLog(canvas);
            BuildMetrics(canvas);
        }

        void BuildHeader(RectTransform canvas)
        {
            DemoUi.CreateText(canvas, "Title", "Response Gate", 30, bold: true).rectTransform
                .Place(new Vector2(0, 1), new Vector2(0.7f, 1), new Vector2(32, -60), new Vector2(0, -16));
            DemoUi.CreateText(canvas, "Subtitle",
                    "Taps are blocked until the server answers. The cooldown length comes from the server, not the client.",
                    16, TextAnchor.MiddleLeft, DemoUi.Muted).rectTransform
                .Place(new Vector2(0, 1), new Vector2(0.8f, 1), new Vector2(32, -92), new Vector2(0, -60));

            _modePill = DemoUi.CreatePanel(canvas, "Mode", DemoUi.Ready, 18);
            _modePill.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-200, -64), new Vector2(-32, -28));
            _modeText = DemoUi.CreateText(_modePill.transform, "Label", "GATE ON", 16, TextAnchor.MiddleCenter, bold: true);
            _modeText.rectTransform.Place(Vector2.zero, Vector2.one);
        }

        void BuildControls(RectTransform canvas)
        {
            RectTransform left = DemoUi.CreateRect(canvas, "Controls")
                .Place(new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(32, 158), new Vector2(-10, -106));
            left.Stack(10, 0);

            _views = new SkillView[SkillNames.Length];
            for (int i = 0; i < SkillNames.Length; i++)
                _views[i] = BuildSkillRow(left, i);

            Image server = DemoUi.CreatePanel(left, "Server", DemoUi.Panel, 12).Height(212);
            server.rectTransform.Stack(2, 14);
            DemoUi.CreateText(server.rectTransform, "Header", "MOCK SERVER", 13, TextAnchor.MiddleLeft, DemoUi.Muted, bold: true).Height(22);
            DemoUi.CreateSlider(server.rectTransform, "Latency", 0, 1000, latencyMs, v => $"{v:0} ms", v => { latencyMs = v; ApplyServerSettings(); });
            DemoUi.CreateSlider(server.rectTransform, "Jitter", 0, 500, jitterMs, v => $"{v:0} ms", v => { jitterMs = v; ApplyServerSettings(); });
            DemoUi.CreateSlider(server.rectTransform, "Failure rate", 0, 100, failurePercent, v => $"{v:0} %", v => { failurePercent = v; ApplyServerSettings(); });
            DemoUi.CreateSlider(server.rectTransform, "Duplicate replies", 0, 100, duplicatePercent, v => $"{v:0} %", v => { duplicatePercent = v; ApplyServerSettings(); });
            _bypassToggle = DemoUi.CreateToggle(server.rectTransform, "Bypass gate (every tap becomes a request)", false, SetBypassGate);
            _bypassToggle.Height(30);
        }

        SkillView BuildSkillRow(RectTransform parent, int index)
        {
            RectTransform row = DemoUi.CreateRect(parent, SkillNames[index]);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 64;
            layout.minHeight = 64;

            Button card = DemoUi.CreateButton(row, "Card", SkillNames[index], () => Tap(index), DemoUi.Panel, 20, 12);
            ((RectTransform)card.transform).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-128, 0));
            Text name = card.GetComponentInChildren<Text>();
            name.alignment = TextAnchor.MiddleLeft;
            name.rectTransform.Place(new Vector2(0, 0.45f), new Vector2(0.7f, 1), new Vector2(28, 0), new Vector2(0, -4));

            var view = new SkillView();
            view.Accent = DemoUi.CreatePanel(card.transform, "Accent", DemoUi.Ready, 3);
            view.Accent.raycastTarget = false;
            view.Accent.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 1), new Vector2(10, 12), new Vector2(16, -12));

            view.State = DemoUi.CreateText(card.transform, "State", "", 13, TextAnchor.MiddleLeft, DemoUi.Muted, bold: true);
            view.State.rectTransform.Place(new Vector2(0, 0.14f), new Vector2(0.8f, 0.5f), new Vector2(28, 0), Vector2.zero);

            view.Countdown = DemoUi.CreateText(card.transform, "Countdown", "", 26, TextAnchor.MiddleRight, bold: true);
            view.Countdown.rectTransform.Place(new Vector2(0.6f, 0), new Vector2(1, 1), Vector2.zero, new Vector2(-18, 0));

            Image track = DemoUi.CreatePanel(card.transform, "CooldownTrack", DemoUi.Track);
            track.raycastTarget = false;
            track.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 7), new Vector2(-18, 10));
            Image fill = DemoUi.CreatePanel(track.transform, "Fill", DemoUi.Cooldown);
            fill.raycastTarget = false;
            view.CooldownFill = fill.rectTransform.Place(Vector2.zero, new Vector2(0, 1));

            Button mash = DemoUi.CreateButton(row, "Mash", "Mash ×10", () => Mash(index), DemoUi.Accent, 16, 12);
            ((RectTransform)mash.transform).Place(new Vector2(1, 0), new Vector2(1, 1), new Vector2(-118, 0), Vector2.zero);
            return view;
        }

        void BuildTimelineAndLog(RectTransform canvas)
        {
            Image timeline = DemoUi.CreatePanel(canvas, "Timeline", DemoUi.Panel, 12);
            timeline.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(10, -270), new Vector2(-32, -106));
            DemoUi.CreateText(timeline.transform, "Header", "LAST 8 SECONDS", 13, TextAnchor.MiddleLeft, DemoUi.Muted, bold: true)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(16, -34), new Vector2(0, -10));
            Text legend = DemoUi.CreateText(timeline.transform, "Legend",
                $"<color={DemoUi.Hex(DemoUi.Accent)}>●</color> tap → request   <color={DemoUi.Hex(DemoUi.Muted)}>●</color> blocked   " +
                $"<color={DemoUi.Hex(DemoUi.Ready)}>●</color> executed   <color={DemoUi.Hex(DemoUi.Danger)}>●</color> rejected",
                13, TextAnchor.MiddleRight, DemoUi.Muted);
            legend.rectTransform.Place(new Vector2(0.4f, 1), new Vector2(1, 1), new Vector2(0, -34), new Vector2(-16, -10));

            RectTransform lanes = DemoUi.CreateRect(timeline.transform, "Lanes")
                .Place(Vector2.zero, Vector2.one, new Vector2(16, 10), new Vector2(-12, -38));
            _timeline = new TimelineView(lanes, new[] { "Taps", "Requests", "Server" }, () => _clock.Now, 8f);

            Image logPanel = DemoUi.CreatePanel(canvas, "LogPanel", DemoUi.Panel, 12);
            logPanel.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(1, 1), new Vector2(10, 158), new Vector2(-32, -282));
            DemoUi.CreateText(logPanel.transform, "Header", "EVENT LOG", 13, TextAnchor.MiddleLeft, DemoUi.Muted, bold: true)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -34), new Vector2(-16, -10));
            _logText = DemoUi.CreateText(logPanel.transform, "Log", "", 14, TextAnchor.UpperLeft);
            _logText.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(16, 10), new Vector2(-16, -38));
            _logText.supportRichText = true;
        }

        void BuildMetrics(RectTransform canvas)
        {
            RectTransform metrics = DemoUi.CreateRect(canvas, "Metrics")
                .Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(32, 24), new Vector2(-32, 140));
            var row = metrics.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;

            _tapsValue = BuildMetric(metrics, "Taps", DemoUi.PanelLight);
            _requestsValue = BuildMetric(metrics, "Requests sent", new Color(0.15f, 0.22f, 0.4f));
            _rejectedValue = BuildMetric(metrics, "Blocked locally", DemoUi.PanelLight);
            _executedValue = BuildMetric(metrics, "Server executed", new Color(0.12f, 0.28f, 0.21f));
            _serverRejectedValue = BuildMetric(metrics, "Server rejected", DemoUi.PanelLight);
            _ignoredValue = BuildMetric(metrics, "Ignored replies", DemoUi.PanelLight);
        }

        static Text BuildMetric(RectTransform parent, string label, Color color)
        {
            Image tile = DemoUi.CreatePanel(parent, label, color, 12);
            Text value = DemoUi.CreateText(tile.transform, "Value", "0", 36, TextAnchor.MiddleCenter, bold: true);
            value.rectTransform.Place(new Vector2(0, 0.36f), new Vector2(1, 1), Vector2.zero, new Vector2(0, -6));
            DemoUi.CreateText(tile.transform, "Label", label, 14, TextAnchor.MiddleCenter, DemoUi.Muted).rectTransform
                .Place(new Vector2(0, 0), new Vector2(1, 0.4f), new Vector2(0, 10), Vector2.zero);
            return value;
        }

        #endregion
    }
}
