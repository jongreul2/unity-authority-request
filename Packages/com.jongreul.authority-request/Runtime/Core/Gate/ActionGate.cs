using System;

namespace Jongreul.AuthorityRequest.Gate
{
    /// <summary>
    /// 서버 응답을 권위로 두는 행동 게이트.
    /// Ready → Pending(요청 전송) → 서버 Success → Cooldown(서버가 준 길이) → Ready,
    /// 서버 Fail → Ready. Pending·Cooldown 중 입력은 거부한다.
    /// 요청마다 단조 증가 시퀀스를 붙여 중복·순서 역전 응답을 걸러낸다.
    /// </summary>
    public sealed class ActionGate : IDisposable
    {
        public const double DefaultPendingTimeoutSeconds = 5.0;

        readonly IGateServer _server;
        readonly IClock _clock;
        readonly double _pendingTimeoutSeconds;

        long _nextSequence = 1;
        long _pendingSequence;
        long _lastResolvedSequence;
        double _pendingSince;
        double _cooldownEndsAt;
        double _cooldownDuration;
        bool _disposed;

        /// <param name="pendingTimeoutSeconds">응답이 이 시간 안에 안 오면 Ready로 복귀. 0 이하면 타임아웃 없음.</param>
        public ActionGate(int actionId, IGateServer server, IClock clock,
            double pendingTimeoutSeconds = DefaultPendingTimeoutSeconds)
        {
            ActionId = actionId;
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _pendingTimeoutSeconds = pendingTimeoutSeconds;
            _server.ResponseReceived += HandleResponse;
        }

        public int ActionId { get; }
        public GateState State { get; private set; } = GateState.Ready;
        public GateStats Stats { get; } = new GateStats();

        /// <summary>대기 중인 요청의 시퀀스. 없으면 0.</summary>
        public long PendingSequence => _pendingSequence;

        /// <summary>마지막으로 적용한 서버 쿨타임 길이(초).</summary>
        public double CooldownDuration => _cooldownDuration;

        public double CooldownRemaining =>
            State == GateState.Cooldown ? Math.Max(0, _cooldownEndsAt - _clock.Now) : 0;

        /// <summary>쿨타임 진행률 0(시작)~1(끝). Cooldown이 아니면 1.</summary>
        public double CooldownProgress =>
            State == GateState.Cooldown && _cooldownDuration > 0
                ? 1 - Math.Min(1, CooldownRemaining / _cooldownDuration)
                : 1;

        public event Action<GateTransition> Transitioned;
        public event Action<GateResponse, GateIgnoreReason> ResponseIgnored;

        /// <summary>입력 한 번. Ready일 때만 서버로 요청을 보낸다.</summary>
        public GateInputResult TryActivate()
        {
            ThrowIfDisposed();
            Tick();
            Stats.InputsReceived++;

            if (State == GateState.Pending)
            {
                Stats.InputsRejected++;
                return GateInputResult.RejectedPending;
            }

            if (State == GateState.Cooldown)
            {
                Stats.InputsRejected++;
                return GateInputResult.RejectedCooldown;
            }

            // 전송 전에 Pending으로 바꾼다. 서버가 Send 안에서 동기 응답해도 순서가 맞다.
            long sequence = _nextSequence++;
            _pendingSequence = sequence;
            _pendingSince = _clock.Now;
            Stats.RequestsSent++;
            SetState(GateState.Pending, GateTransitionReason.RequestSent, sequence);
            _server.Send(new GateRequest(ActionId, sequence));
            return GateInputResult.Sent;
        }

        /// <summary>시간 경과 처리(쿨타임 만료, 응답 타임아웃). 매 프레임 호출한다.</summary>
        public void Tick()
        {
            if (_disposed)
                return;

            double now = _clock.Now;

            if (State == GateState.Pending && _pendingTimeoutSeconds > 0 &&
                now - _pendingSince >= _pendingTimeoutSeconds)
            {
                long timedOut = _pendingSequence;
                _pendingSequence = 0;
                Stats.Timeouts++;
                SetState(GateState.Ready, GateTransitionReason.PendingTimeout, timedOut);
            }
            else if (State == GateState.Cooldown && now >= _cooldownEndsAt)
            {
                SetState(GateState.Ready, GateTransitionReason.CooldownElapsed, _lastResolvedSequence);
            }
        }

        void HandleResponse(GateResponse response)
        {
            if (_disposed || response.ActionId != ActionId)
                return;

            if (State != GateState.Pending || response.Sequence != _pendingSequence)
            {
                Ignore(response);
                return;
            }

            _lastResolvedSequence = response.Sequence;
            _pendingSequence = 0;

            if (!response.Success)
            {
                Stats.Failures++;
                SetState(GateState.Ready, GateTransitionReason.ServerFail, response.Sequence);
                return;
            }

            Stats.Successes++;
            double cooldown = SanitizeCooldown(response.CooldownSeconds);
            _cooldownDuration = cooldown;

            if (cooldown <= 0)
            {
                SetState(GateState.Ready, GateTransitionReason.ServerSuccess, response.Sequence);
                return;
            }

            // 쿨타임은 응답을 받은 시점부터 센다. 서버보다 편도 지연만큼 늦게 끝나므로
            // 클라이언트가 서버보다 먼저 다음 입력을 허용하는 일은 없다.
            _cooldownEndsAt = _clock.Now + cooldown;
            SetState(GateState.Cooldown, GateTransitionReason.ServerSuccess, response.Sequence);
        }

        void Ignore(GateResponse response)
        {
            GateIgnoreReason reason;
            if (response.Sequence >= _nextSequence || response.Sequence <= 0)
            {
                reason = GateIgnoreReason.Unknown;
                Stats.UnknownIgnored++;
            }
            else if (response.Sequence == _lastResolvedSequence)
            {
                reason = GateIgnoreReason.Duplicate;
                Stats.DuplicatesIgnored++;
            }
            else
            {
                reason = GateIgnoreReason.Stale;
                Stats.StaleIgnored++;
            }

            ResponseIgnored?.Invoke(response, reason);
        }

        static double SanitizeCooldown(double seconds) =>
            double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 ? 0 : seconds;

        void SetState(GateState next, GateTransitionReason reason, long sequence)
        {
            GateState previous = State;
            State = next;
            Transitioned?.Invoke(new GateTransition(previous, next, reason, sequence));
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ActionGate));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _server.ResponseReceived -= HandleResponse;
        }
    }
}
