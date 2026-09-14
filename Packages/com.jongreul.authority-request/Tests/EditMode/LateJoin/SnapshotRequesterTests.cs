using System.Collections.Generic;
using Jongreul.AuthorityRequest.LateJoin;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.LateJoin
{
    public class SnapshotRequesterTests
    {
        const int Slots = 8;

        SnapshotProvider<int> _provider;
        SnapshotRequester<int> _client;
        List<SnapshotRequest> _requests;
        List<SlotDelta<int>> _deltas;

        [SetUp]
        public void SetUp()
        {
            _provider = new SnapshotProvider<int>(Slots);
            _client = new SnapshotRequester<int>(clientId: 1, Slots);
            _requests = new List<SnapshotRequest>();
            _deltas = new List<SlotDelta<int>>();
            _client.RequestReady += _requests.Add;
            _provider.Changed += _deltas.Add;
        }

        SnapshotResponse<int> ServeLast() => _provider.CreateSnapshot(_requests[_requests.Count - 1]);

        void AssertMatchesServer()
        {
            for (int i = 0; i < Slots; i++)
                Assert.That(_client.Get(i), Is.EqualTo(_provider.Get(i)), $"slot {i}");
            Assert.That(_client.Version, Is.EqualTo(_provider.Version));
        }

        [Test]
        public void LateJoiner_ReceivesExactState()
        {
            for (int i = 0; i < 20; i++)
                _provider.Set(i % Slots, i * 7);

            _client.RequestSnapshot();
            _client.ApplySnapshot(ServeLast());

            Assert.That(_client.State, Is.EqualTo(SyncState.Synced));
            AssertMatchesServer();
        }

        [Test]
        public void ChangeDuringRequest_ArrivingBeforeSnapshot_IsNotLost()
        {
            _provider.Set(0, 10);
            _client.RequestSnapshot();
            SnapshotResponse<int> snapshot = ServeLast();
            _provider.Set(2, 30); // 스냅샷을 만든 뒤, 응답이 도착하기 전에 생긴 변경

            _client.ApplyDelta(_deltas[1]); // 변경이 스냅샷보다 먼저 도착
            Assert.That(_client.BufferedCount, Is.EqualTo(1));
            _client.ApplySnapshot(snapshot);

            AssertMatchesServer();
            Assert.That(_client.Get(2), Is.EqualTo(30));
        }

        [Test]
        public void BufferedDeltaAlreadyInSnapshot_IsNotAppliedTwice()
        {
            _provider.Set(0, 1);
            _client.RequestSnapshot();
            _client.ApplyDelta(_deltas[0]); // v1: 스냅샷에도 들어갈 변경
            _provider.Set(0, 2);            // v2
            _client.ApplySnapshot(ServeLast());

            Assert.That(_client.Get(0), Is.EqualTo(2));
            Assert.That(_client.DeltasIgnored, Is.EqualTo(1));

            _client.ApplyDelta(_deltas[1]); // v2가 늦게 도착해도 무시
            Assert.That(_client.Get(0), Is.EqualTo(2));
            AssertMatchesServer();
        }

        [Test]
        public void DeltaBeforeFirstRequest_IsBufferedNotLost()
        {
            _provider.Set(1, 5);
            SnapshotResponse<int> earlySnapshot = _provider.CreateSnapshot(new SnapshotRequest(1, 1));
            _provider.Set(1, 6);

            _client.ApplyDelta(_deltas[1]); // Idle 상태에서 받은 변경
            _client.RequestSnapshot();
            _client.ApplySnapshot(earlySnapshot); // 요청 ID 1, 버전 1

            AssertMatchesServer();
        }

        [Test]
        public void RepeatedRequests_AreIdempotent()
        {
            _provider.Set(4, 44);
            _client.RequestSnapshot();
            _client.ApplySnapshot(ServeLast());
            long versionAfterFirst = _client.Version;

            _client.RequestSnapshot();
            _client.ApplySnapshot(ServeLast());

            Assert.That(_client.Version, Is.EqualTo(versionAfterFirst));
            Assert.That(_client.SnapshotsApplied, Is.EqualTo(2));
            AssertMatchesServer();
        }

        [Test]
        public void SnapshotForOlderRequest_IsIgnored()
        {
            _client.RequestSnapshot();
            SnapshotResponse<int> first = ServeLast();
            _provider.Set(0, 9);
            _client.RequestSnapshot();

            Assert.That(_client.ApplySnapshot(first), Is.False);
            Assert.That(_client.State, Is.EqualTo(SyncState.Requesting));

            _client.ApplySnapshot(ServeLast());
            AssertMatchesServer();
        }

        [Test]
        public void DuplicateSnapshot_IsIgnored()
        {
            _client.RequestSnapshot();
            SnapshotResponse<int> snapshot = ServeLast();
            _client.ApplySnapshot(snapshot);
            _provider.Set(0, 3);
            _client.ApplyDelta(_deltas[0]);

            Assert.That(_client.ApplySnapshot(snapshot), Is.False);
            Assert.That(_client.Get(0), Is.EqualTo(3));
            Assert.That(_client.SnapshotsIgnored, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateDelta_IsIgnored()
        {
            _client.RequestSnapshot();
            _client.ApplySnapshot(ServeLast());
            _provider.Set(0, 1);
            _provider.Set(0, 2);

            _client.ApplyDelta(_deltas[0]);
            _client.ApplyDelta(_deltas[1]);
            _client.ApplyDelta(_deltas[0]);

            Assert.That(_client.Get(0), Is.EqualTo(2));
            Assert.That(_client.DeltasIgnored, Is.EqualTo(1));
        }

        [Test]
        public void MissingDelta_TriggersReRequest_AndRecovers()
        {
            _client.RequestSnapshot();
            _client.ApplySnapshot(ServeLast());
            _provider.Set(0, 1); // v1 — 유실된다
            _provider.Set(1, 2); // v2

            _client.ApplyDelta(_deltas[1]);

            Assert.That(_client.GapsDetected, Is.EqualTo(1));
            Assert.That(_client.State, Is.EqualTo(SyncState.Requesting));
            Assert.That(_requests.Count, Is.EqualTo(2));

            _client.ApplySnapshot(ServeLast());
            AssertMatchesServer();
        }

        [Test]
        public void GapInsideBuffer_TriggersReRequest()
        {
            _client.RequestSnapshot();
            SnapshotResponse<int> snapshot = ServeLast(); // v0
            _provider.Set(0, 1); // v1 — 유실
            _provider.Set(1, 2); // v2
            _client.ApplyDelta(_deltas[1]);

            _client.ApplySnapshot(snapshot);

            Assert.That(_client.GapsDetected, Is.EqualTo(1));
            Assert.That(_client.State, Is.EqualTo(SyncState.Requesting));
            _client.ApplySnapshot(ServeLast());
            AssertMatchesServer();
        }

        [Test]
        public void SlotChanged_IsRaisedOnlyForSlotsThatDiffer()
        {
            var changed = new List<int>();
            _client.SlotChanged += (slot, _) => changed.Add(slot);
            _provider.Set(1, 10);
            _provider.Set(5, 50);

            _client.RequestSnapshot();
            _client.ApplySnapshot(ServeLast());
            _client.RequestSnapshot();
            _client.ApplySnapshot(ServeLast());

            Assert.That(changed, Is.EqualTo(new List<int> { 1, 5 }));
        }

        SnapshotRequester<int> CreateTimedClient(ManualClock clock, List<SnapshotRequest> requests)
        {
            var client = new SnapshotRequester<int>(clientId: 2, Slots, clock: clock);
            client.RequestReady += requests.Add;
            client.RequestSnapshot();
            client.ApplySnapshot(_provider.CreateSnapshot(requests[0]));
            return client;
        }

        [Test]
        public void WithClock_ReorderedDelta_IsHealedWithoutReRequest()
        {
            var clock = new ManualClock();
            var requests = new List<SnapshotRequest>();
            SnapshotRequester<int> client = CreateTimedClient(clock, requests);
            _provider.Set(0, 1);
            _provider.Set(1, 2);

            client.ApplyDelta(_deltas[1]); // v2가 먼저
            Assert.That(client.HasPendingGap, Is.True);
            clock.Advance(0.1);
            client.Tick();
            client.ApplyDelta(_deltas[0]); // v1이 늦게

            Assert.That(client.HasPendingGap, Is.False);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(client.DeltasOutOfOrder, Is.EqualTo(1));
            Assert.That(client.Version, Is.EqualTo(2));
            Assert.That(client.Get(1), Is.EqualTo(2));
        }

        [Test]
        public void WithClock_LostDelta_ReRequestsAfterGrace()
        {
            var clock = new ManualClock();
            var requests = new List<SnapshotRequest>();
            SnapshotRequester<int> client = CreateTimedClient(clock, requests);
            _provider.Set(0, 1); // v1 — 유실
            _provider.Set(1, 2); // v2
            client.ApplyDelta(_deltas[1]);

            clock.Advance(SnapshotRequester<int>.DefaultGapGraceSeconds - 0.01);
            client.Tick();
            Assert.That(requests.Count, Is.EqualTo(1));

            clock.Advance(0.01);
            client.Tick();
            Assert.That(requests.Count, Is.EqualTo(2));
            Assert.That(client.GapsDetected, Is.EqualTo(1));

            client.ApplySnapshot(_provider.CreateSnapshot(requests[1]));
            Assert.That(client.Get(0), Is.EqualTo(1));
            Assert.That(client.Version, Is.EqualTo(_provider.Version));
        }

        [Test]
        public void WithClock_LostSnapshotResponse_RetriesAfterTimeout()
        {
            var clock = new ManualClock();
            var client = new SnapshotRequester<int>(clientId: 3, Slots, clock: clock);
            var requests = new List<SnapshotRequest>();
            client.RequestReady += requests.Add;
            _provider.Set(5, 50);

            client.RequestSnapshot(); // 응답이 유실된다
            clock.Advance(SnapshotRequester<int>.DefaultRequestTimeoutSeconds);
            client.Tick();

            Assert.That(requests.Count, Is.EqualTo(2));
            Assert.That(client.RequestTimeouts, Is.EqualTo(1));
            Assert.That(client.ApplySnapshot(_provider.CreateSnapshot(requests[0])), Is.False); // 늦게 온 옛 응답
            Assert.That(client.ApplySnapshot(_provider.CreateSnapshot(requests[1])), Is.True);
            Assert.That(client.Get(5), Is.EqualTo(50));
        }

        [Test]
        public void SnapshotOlderThanAppliedState_IsIgnored()
        {
            _client.RequestSnapshot();
            _client.ApplySnapshot(ServeLast());
            _provider.Set(0, 1);
            _client.ApplyDelta(_deltas[0]); // Version 1

            _client.RequestSnapshot();
            var stale = new SnapshotResponse<int>(1, 2, version: 0, new List<SlotEntry<int>>());

            Assert.That(_client.ApplySnapshot(stale), Is.False);
            Assert.That(_client.Get(0), Is.EqualTo(1));
        }
    }
}
