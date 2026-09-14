using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.Gate
{
    /// <summary>
    /// 지연·실패·중복 응답·순서 역전을 흉내 내는 가짜 서버.
    /// 서버 쪽에서도 쿨타임을 판정하므로, 클라이언트가 게이트를 우회해도 행동이 두 번 실행되지 않는다.
    /// </summary>
    public sealed class MockGateServer : IGateServer
    {
        struct Scheduled
        {
            public double DeliverAt;
            public long Order;
            public GateResponse Response;
        }

        readonly IClock _clock;
        readonly Random _random;
        readonly List<Scheduled> _outbox = new List<Scheduled>();
        readonly Dictionary<int, double> _cooldowns = new Dictionary<int, double>();
        readonly Dictionary<int, double> _serverCooldownEndsAt = new Dictionary<int, double>();
        long _scheduleOrder;

        public MockGateServer(IClock clock, int seed = 0)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _random = new Random(seed);
        }

        /// <summary>기본 편도 지연(초).</summary>
        public double LatencySeconds { get; set; } = 0.2;

        /// <summary>지연 흔들림 폭(초). 클수록 응답 순서가 뒤바뀌기 쉽다.</summary>
        public double LatencyJitterSeconds { get; set; }

        /// <summary>서버가 요청을 거절할 확률 0~1.</summary>
        public double FailureRate { get; set; }

        /// <summary>같은 응답을 한 번 더 보낼 확률 0~1.</summary>
        public double DuplicateRate { get; set; }

        /// <summary>쿨타임 표에 없는 행동의 쿨타임(초).</summary>
        public double DefaultCooldownSeconds { get; set; } = 1.0;

        public int RequestsReceived { get; private set; }

        /// <summary>서버가 실제로 실행한 행동 수.</summary>
        public int ActionsExecuted { get; private set; }

        public int PendingDeliveries => _outbox.Count;

        public event Action<GateResponse> ResponseReceived;

        /// <summary>서버 판정 직후 호출. 데모 로그용.</summary>
        public event Action<GateRequest, GateResponse> RequestJudged;

        public void SetCooldown(int actionId, double seconds) => _cooldowns[actionId] = seconds;

        public void Send(GateRequest request)
        {
            RequestsReceived++;
            GateResponse response = Judge(request);
            RequestJudged?.Invoke(request, response);

            double now = _clock.Now;
            Schedule(response, now + NextLatency());

            if (DuplicateRate > 0 && _random.NextDouble() < DuplicateRate)
                Schedule(response, now + NextLatency());
        }

        /// <summary>도착 시각이 된 응답을 도착 순서대로 배달한다. 매 프레임 호출한다.</summary>
        public void Tick()
        {
            double now = _clock.Now;
            // 배달 도중 새 예약이 생길 수 있으므로 매번 가장 이른 것 하나씩 꺼낸다.
            while (TryTakeEarliestDue(now, out GateResponse response))
                ResponseReceived?.Invoke(response);
        }

        GateResponse Judge(GateRequest request)
        {
            double now = _clock.Now;

            if (_serverCooldownEndsAt.TryGetValue(request.ActionId, out double endsAt) && now < endsAt)
                return GateResponse.Failed(request.ActionId, request.Sequence, "server-cooldown");

            if (FailureRate > 0 && _random.NextDouble() < FailureRate)
                return GateResponse.Failed(request.ActionId, request.Sequence, "server-rejected");

            double cooldown = _cooldowns.TryGetValue(request.ActionId, out double value)
                ? value
                : DefaultCooldownSeconds;

            _serverCooldownEndsAt[request.ActionId] = now + cooldown;
            ActionsExecuted++;
            return GateResponse.Succeeded(request.ActionId, request.Sequence, cooldown);
        }

        double NextLatency()
        {
            double jitter = LatencyJitterSeconds > 0
                ? (_random.NextDouble() * 2 - 1) * LatencyJitterSeconds
                : 0;
            return Math.Max(0, LatencySeconds + jitter);
        }

        void Schedule(GateResponse response, double deliverAt)
        {
            _outbox.Add(new Scheduled { DeliverAt = deliverAt, Order = _scheduleOrder++, Response = response });
        }

        bool TryTakeEarliestDue(double now, out GateResponse response)
        {
            int best = -1;
            for (int i = 0; i < _outbox.Count; i++)
            {
                Scheduled candidate = _outbox[i];
                if (candidate.DeliverAt > now)
                    continue;

                if (best < 0 || candidate.DeliverAt < _outbox[best].DeliverAt ||
                    (candidate.DeliverAt == _outbox[best].DeliverAt && candidate.Order < _outbox[best].Order))
                    best = i;
            }

            if (best < 0)
            {
                response = default;
                return false;
            }

            response = _outbox[best].Response;
            _outbox.RemoveAt(best);
            return true;
        }
    }
}
