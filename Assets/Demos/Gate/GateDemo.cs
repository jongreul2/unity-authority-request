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
        const int LogLines = 14;

        static readonly string[] SkillNames = { "Skill A", "Skill B", "Skill C" };
        static readonly double[] SkillCooldowns = { 1.0, 2.5, 5.0 };

        [SerializeField] float latencyMs = 250;
        [SerializeField] float jitterMs;
        [SerializeField] float failurePercent;
        [SerializeField] float duplicatePercent;

        sealed class SkillView
        {
            public Image Background;
            public Text Label;
            public Text State;
            public RectTransform CooldownBar;
        }

        readonly Queue<string> _log = new Queue<string>();
        readonly StringBuilder _builder = new StringBuilder();

        UnityClock _clock;
        MockGateServer _server;
        ActionGate[] _gates;
        SkillView[] _views;
        Text _statsText;
        Text _logText;
        Toggle _bypassToggle;
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
                gate.Transitioned += t => Log($"{skill}: {t.From} → {t.To} ({t.Reason})");
                gate.ResponseIgnored += (r, reason) => Log($"{skill}: ignored {reason} response seq {r.Sequence}");
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
                _server.Send(new GateRequest(index + 1 + NaiveActionOffset, ++_naiveSequence));
                Log($"{SkillNames[index]}: sent without gate (seq {_naiveSequence})");
                return;
            }

            GateInputResult result = _gates[index].TryActivate();
            if (result != GateInputResult.Sent)
                Log($"{SkillNames[index]}: tap rejected locally ({result})");
        }

        /// <summary>연타 흉내. 간격 초마다 count번 누른다.</summary>
        public Coroutine Mash(int index, int count = 10, float interval = 0.04f) =>
            StartCoroutine(MashRoutine(index, count, interval));

        public void SetBypassGate(bool bypass)
        {
            _bypassGate = bypass;
            if (_bypassToggle != null)
                _bypassToggle.SetIsOnWithoutNotify(bypass);
            Log(bypass ? "Gate bypassed: every tap goes to the server" : "Gate enabled");
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

        void OnRequestJudged(GateRequest request, GateResponse response)
        {
            bool naive = request.ActionId > NaiveActionOffset;
            int index = (naive ? request.ActionId - NaiveActionOffset : request.ActionId) - 1;
            if (!response.Success)
                _serverRejected++;
            Log(response.Success
                ? $"SERVER: {SkillNames[index]} executed, cooldown {response.CooldownSeconds:0.0}s"
                : $"SERVER: {SkillNames[index]} rejected ({response.FailReason})");
        }

        void Log(string line)
        {
            _log.Enqueue($"[{_clock.Now - _startTime,6:0.00}] {line}");
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
                switch (gate.State)
                {
                    case GateState.Ready:
                        view.Background.color = DemoUi.Ready;
                        view.State.text = "READY";
                        break;
                    case GateState.Pending:
                        view.Background.color = DemoUi.Pending;
                        view.State.text = $"WAITING FOR SERVER (seq {gate.PendingSequence})";
                        break;
                    default:
                        view.Background.color = DemoUi.Cooldown;
                        view.State.text = $"COOLDOWN {gate.CooldownRemaining:0.0}s  (server value {gate.CooldownDuration:0.0}s)";
                        break;
                }

                float remaining = gate.State == GateState.Cooldown ? (float)(1 - gate.CooldownProgress) : 0f;
                view.CooldownBar.anchorMax = new Vector2(remaining, 1);
            }

            _statsText.text =
                $"Mode  <b>{(_bypassGate ? "NO GATE (naive)" : "GATED")}</b>     " +
                $"Taps  <b>{_taps}</b>     Requests sent  <b>{requestsSent}</b>     Rejected locally  <b>{rejectedLocally}</b>\n" +
                $"Server executed  <b>{_server.ActionsExecuted}</b>     Server rejected  <b>{_serverRejected}</b>     Ignored responses  <b>{ignored}</b>";
        }

        void BuildUi()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = DemoUi.Background;
            }

            RectTransform canvas = DemoUi.CreateCanvas(cam);
            Text title = DemoUi.CreateText(canvas, "Title", "<b>Response Gate</b>  —  the server response is the authority", 26);
            title.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(32, -64), new Vector2(-32, -16));

            // 왼쪽: 스킬 버튼
            RectTransform left = DemoUi.CreatePanel(canvas, "Skills", DemoUi.Panel).rectTransform
                .Place(new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(32, 150), new Vector2(-12, -80));
            left.Stack(14, 18);

            _views = new SkillView[SkillNames.Length];
            for (int i = 0; i < SkillNames.Length; i++)
                _views[i] = BuildSkillRow(left, i);

            // 서버 설정
            DemoUi.CreateText(left, "ServerHeader", "Mock server", 18, TextAnchor.MiddleLeft, DemoUi.Muted).Height(26);
            DemoUi.CreateSlider(left, "Latency", 0, 1000, latencyMs, v => $"{v:0} ms", v => { latencyMs = v; ApplyServerSettings(); });
            DemoUi.CreateSlider(left, "Jitter", 0, 500, jitterMs, v => $"{v:0} ms", v => { jitterMs = v; ApplyServerSettings(); });
            DemoUi.CreateSlider(left, "Failure rate", 0, 100, failurePercent, v => $"{v:0} %", v => { failurePercent = v; ApplyServerSettings(); });
            DemoUi.CreateSlider(left, "Duplicate responses", 0, 100, duplicatePercent, v => $"{v:0} %", v => { duplicatePercent = v; ApplyServerSettings(); });
            _bypassToggle = DemoUi.CreateToggle(left, "Bypass gate (naive client: every tap is a request)", false, SetBypassGate);
            _bypassToggle.Height(30);

            // 오른쪽: 로그
            Image logPanel = DemoUi.CreatePanel(canvas, "LogPanel", DemoUi.Panel);
            logPanel.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(1, 1), new Vector2(12, 150), new Vector2(-32, -80));
            _logText = DemoUi.CreateText(logPanel.transform, "Log", "", 15, TextAnchor.UpperLeft);
            _logText.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(16, 12), new Vector2(-16, -12));
            _logText.supportRichText = false;

            // 아래: 통계
            Image statsPanel = DemoUi.CreatePanel(canvas, "Stats", DemoUi.PanelLight);
            statsPanel.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(32, 24), new Vector2(-32, 132));
            _statsText = DemoUi.CreateText(statsPanel.transform, "StatsText", "", 20, TextAnchor.MiddleCenter);
            _statsText.rectTransform.Place(Vector2.zero, Vector2.one);
        }

        SkillView BuildSkillRow(RectTransform parent, int index)
        {
            RectTransform row = DemoUi.CreateRect(parent, SkillNames[index]);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            Button tap = DemoUi.CreateButton(row, "Tap", SkillNames[index], () => Tap(index), DemoUi.Ready, 20);
            ((RectTransform)tap.transform).Place(new Vector2(0, 0), new Vector2(0.78f, 1));
            var view = new SkillView
            {
                Background = tap.GetComponent<Image>(),
                Label = tap.GetComponentInChildren<Text>(),
            };
            view.Label.rectTransform.Place(new Vector2(0, 0.45f), new Vector2(1, 1));

            view.State = DemoUi.CreateText(tap.transform, "State", "", 13, TextAnchor.MiddleCenter);
            view.State.rectTransform.Place(new Vector2(0, 0.08f), new Vector2(1, 0.5f));

            Image bar = DemoUi.CreatePanel(tap.transform, "CooldownBar", new Color(1, 1, 1, 0.18f));
            bar.raycastTarget = false;
            view.CooldownBar = bar.rectTransform.Place(Vector2.zero, new Vector2(0, 1));

            Button mash = DemoUi.CreateButton(row, "Mash", "Mash ×10", () => Mash(index), DemoUi.Accent, 16);
            ((RectTransform)mash.transform).Place(new Vector2(0.8f, 0), new Vector2(1, 1));
            return view;
        }
    }
}
