using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.LateJoin
{
    /// <summary>
    /// 서버 하나와 클라이언트 여럿을 잇는 가짜 네트워크. 메시지마다 지연(흔들림 포함)을 준다.
    /// 변경은 그 시점에 접속해 있는 클라이언트에게만 방송된다. 스냅샷은 요청이 서버에 도착한 시점의 상태다.
    /// </summary>
    public sealed class MockSnapshotNetwork<T>
    {
        struct Message
        {
            public double DeliverAt;
            public long Order;
            public Action Deliver;
        }

        readonly SnapshotProvider<T> _provider;
        readonly IClock _clock;
        readonly Random _random;
        readonly IEqualityComparer<T> _comparer;
        readonly List<Message> _queue = new List<Message>();
        readonly Dictionary<int, SnapshotRequester<T>> _clients = new Dictionary<int, SnapshotRequester<T>>();
        long _order;

        public MockSnapshotNetwork(SnapshotProvider<T> provider, IClock clock,
            IEqualityComparer<T> comparer = null, int seed = 0)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _comparer = comparer;
            _random = new Random(seed);
            _provider.Changed += Broadcast;
        }

        public double LatencySeconds { get; set; } = 0.1;
        public double LatencyJitterSeconds { get; set; }

        public int DeltasSent { get; private set; }
        public int SnapshotsSent { get; private set; }
        public int SnapshotEntriesSent { get; private set; }
        public int InFlight => _queue.Count;

        public IReadOnlyCollection<SnapshotRequester<T>> Clients => _clients.Values;

        /// <summary>
        /// 클라이언트 접속. 접속 즉시 변경 방송을 받기 시작하지만 스냅샷은 클라이언트가 준비됐을 때 스스로 요청한다.
        /// </summary>
        public SnapshotRequester<T> Join(int clientId)
        {
            var client = new SnapshotRequester<T>(clientId, _provider.SlotCount, _provider.DefaultValue, _comparer);
            client.RequestReady += request => Send(() => ServeSnapshot(request));
            _clients.Add(clientId, client);
            return client;
        }

        public void Leave(int clientId) => _clients.Remove(clientId);

        /// <summary>도착 시각이 된 메시지를 도착 순서대로 배달한다.</summary>
        public void Tick()
        {
            while (TryTakeEarliestDue(out Action deliver))
                deliver();
        }

        void ServeSnapshot(SnapshotRequest request)
        {
            if (!_clients.ContainsKey(request.ClientId))
                return;

            SnapshotResponse<T> snapshot = _provider.CreateSnapshot(request);
            SnapshotsSent++;
            SnapshotEntriesSent += snapshot.Entries.Count;
            Send(() =>
            {
                if (_clients.TryGetValue(request.ClientId, out SnapshotRequester<T> client))
                    client.ApplySnapshot(snapshot);
            });
        }

        void Broadcast(SlotDelta<T> delta)
        {
            foreach (SnapshotRequester<T> client in _clients.Values)
            {
                int clientId = client.ClientId;
                DeltasSent++;
                Send(() =>
                {
                    if (_clients.TryGetValue(clientId, out SnapshotRequester<T> target))
                        target.ApplyDelta(delta);
                });
            }
        }

        void Send(Action deliver)
        {
            double jitter = LatencyJitterSeconds > 0 ? (_random.NextDouble() * 2 - 1) * LatencyJitterSeconds : 0;
            double deliverAt = _clock.Now + Math.Max(0, LatencySeconds + jitter);
            _queue.Add(new Message { DeliverAt = deliverAt, Order = _order++, Deliver = deliver });
        }

        bool TryTakeEarliestDue(out Action deliver)
        {
            double now = _clock.Now;
            int best = -1;
            for (int i = 0; i < _queue.Count; i++)
            {
                Message candidate = _queue[i];
                if (candidate.DeliverAt > now)
                    continue;

                if (best < 0 || candidate.DeliverAt < _queue[best].DeliverAt ||
                    (candidate.DeliverAt == _queue[best].DeliverAt && candidate.Order < _queue[best].Order))
                    best = i;
            }

            if (best < 0)
            {
                deliver = null;
                return false;
            }

            deliver = _queue[best].Deliver;
            _queue.RemoveAt(best);
            return true;
        }
    }
}
