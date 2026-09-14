using System;
using Jongreul.AuthorityRequest.LateJoin;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.LateJoin
{
    public class MockSnapshotNetworkTests
    {
        [Test]
        public void LateJoinersWithJitter_ConvergeToServerState()
        {
            var clock = new ManualClock();
            var provider = new SnapshotProvider<int>(16);
            var network = new MockSnapshotNetwork<int>(provider, clock, seed: 7)
            {
                LatencySeconds = 0.15,
                LatencyJitterSeconds = 0.12, // 순서 역전이 생기는 폭
            };
            var random = new Random(99);

            for (int step = 0; step < 400; step++)
            {
                if (step % 60 == 0)
                {
                    // 새 클라이언트가 들어오고, 준비되면 스스로 요청한다
                    network.Join(step / 60).RequestSnapshot();
                }

                provider.Set(random.Next(16), random.Next(4)); // 0은 기본값
                clock.Advance(0.02);
                network.Tick();
            }

            // 변경이 멈춘 뒤 전송 중인 메시지를 모두 배달
            for (int i = 0; i < 200 && network.InFlight > 0; i++)
            {
                clock.Advance(0.05);
                network.Tick();
            }

            Assert.That(network.InFlight, Is.EqualTo(0));
            foreach (SnapshotRequester<int> client in network.Clients)
            {
                Assert.That(client.State, Is.EqualTo(SyncState.Synced), $"client {client.ClientId}");
                Assert.That(client.Version, Is.EqualTo(provider.Version));
                for (int slot = 0; slot < 16; slot++)
                    Assert.That(client.Get(slot), Is.EqualTo(provider.Get(slot)), $"client {client.ClientId} slot {slot}");
            }
        }

        [Test]
        public void SnapshotSize_EqualsNonDefaultSlots()
        {
            var clock = new ManualClock();
            var provider = new SnapshotProvider<int>(64);
            provider.Set(1, 1);
            provider.Set(20, 1);
            provider.Set(50, 1);
            var network = new MockSnapshotNetwork<int>(provider, clock) { LatencySeconds = 0 };

            network.Join(1).RequestSnapshot();
            network.Tick();

            Assert.That(network.SnapshotsSent, Is.EqualTo(1));
            Assert.That(network.SnapshotEntriesSent, Is.EqualTo(3));
        }

        [Test]
        public void LeftClient_ReceivesNothingMore()
        {
            var clock = new ManualClock();
            var provider = new SnapshotProvider<int>(4);
            var network = new MockSnapshotNetwork<int>(provider, clock) { LatencySeconds = 0.1 };
            SnapshotRequester<int> client = network.Join(1);
            client.RequestSnapshot();
            clock.Advance(0.3);
            network.Tick();

            provider.Set(0, 5);
            network.Leave(1);
            clock.Advance(0.3);
            network.Tick();

            Assert.That(client.Get(0), Is.EqualTo(0));
        }
    }
}
