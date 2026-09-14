using System.Collections.Generic;
using Jongreul.AuthorityRequest.Gate;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.Gate
{
    public class MockGateServerTests
    {
        const int Action = 7;

        [Test]
        public void Response_IsDeliveredOnlyAfterLatency()
        {
            var clock = new ManualClock();
            var server = new MockGateServer(clock) { LatencySeconds = 0.3 };
            var gate = new ActionGate(Action, server, clock);

            gate.TryActivate();
            clock.Advance(0.29);
            server.Tick();
            Assert.That(gate.State, Is.EqualTo(GateState.Pending));

            clock.Advance(0.01);
            server.Tick();
            Assert.That(gate.State, Is.EqualTo(GateState.Cooldown));
        }

        [Test]
        public void ServerSideCooldown_RejectsRequestThatBypassesClient()
        {
            var clock = new ManualClock();
            var server = new MockGateServer(clock) { LatencySeconds = 0 };
            server.SetCooldown(Action, 2.0);
            var responses = new List<GateResponse>();
            server.ResponseReceived += responses.Add;

            // 게이트를 거치지 않고 같은 행동을 연속 전송(조작된 클라이언트)
            server.Send(new GateRequest(Action, 1));
            server.Send(new GateRequest(Action, 2));
            server.Tick();

            Assert.That(server.ActionsExecuted, Is.EqualTo(1));
            Assert.That(responses[0].Success, Is.True);
            Assert.That(responses[1].Success, Is.False);
            Assert.That(responses[1].FailReason, Is.EqualTo("server-cooldown"));
        }

        [Test]
        public void CooldownTable_IsPerAction()
        {
            var clock = new ManualClock();
            var server = new MockGateServer(clock) { LatencySeconds = 0 };
            server.SetCooldown(1, 0.5);
            server.SetCooldown(2, 4.0);
            var a = new ActionGate(1, server, clock);
            var b = new ActionGate(2, server, clock);

            a.TryActivate();
            b.TryActivate();
            server.Tick();

            Assert.That(a.CooldownDuration, Is.EqualTo(0.5));
            Assert.That(b.CooldownDuration, Is.EqualTo(4.0));
        }

        [Test]
        public void FailureRateOne_AlwaysFails()
        {
            var clock = new ManualClock();
            var server = new MockGateServer(clock) { LatencySeconds = 0, FailureRate = 1 };
            var gate = new ActionGate(Action, server, clock);

            for (int i = 0; i < 20; i++)
            {
                gate.TryActivate();
                server.Tick();
            }

            Assert.That(gate.Stats.Failures, Is.EqualTo(20));
            Assert.That(server.ActionsExecuted, Is.EqualTo(0));
        }

        [Test]
        public void DuplicateRateOne_DeliversTwice_GateAppliesOnce()
        {
            var clock = new ManualClock();
            var server = new MockGateServer(clock) { LatencySeconds = 0.1, DuplicateRate = 1 };
            var gate = new ActionGate(Action, server, clock);

            gate.TryActivate();
            clock.Advance(0.1);
            server.Tick();

            Assert.That(gate.Stats.Successes, Is.EqualTo(1));
            Assert.That(gate.Stats.DuplicatesIgnored, Is.EqualTo(1));
        }

        [Test]
        public void SameSeed_ProducesSameRun()
        {
            Assert.That(RunChaos(seed: 42), Is.EqualTo(RunChaos(seed: 42)));
        }

        [Test]
        public void ChaosRun_KeepsGateAndServerConsistent()
        {
            var clock = new ManualClock();
            var server = new MockGateServer(clock, seed: 1234)
            {
                LatencySeconds = 0.25,
                LatencyJitterSeconds = 0.25,
                FailureRate = 0.2,
                DuplicateRate = 0.3,
                DefaultCooldownSeconds = 0.4,
            };
            var gate = new ActionGate(Action, server, clock, pendingTimeoutSeconds: 0.35);

            var appliedSequences = new List<long>();
            gate.Transitioned += t =>
            {
                if (t.Reason == GateTransitionReason.ServerSuccess || t.Reason == GateTransitionReason.ServerFail)
                    appliedSequences.Add(t.Sequence);
            };

            var executionTimes = new List<double>();
            server.RequestJudged += (request, response) =>
            {
                if (response.Success)
                    executionTimes.Add(clock.Now);
            };

            // 20ms 간격으로 3000번 연타
            for (int i = 0; i < 3000; i++)
            {
                gate.TryActivate();
                clock.Advance(0.02);
                server.Tick();
                gate.Tick();
            }

            // 게이트가 보낸 요청 수 == 서버가 받은 요청 수
            Assert.That(server.RequestsReceived, Is.EqualTo(gate.Stats.RequestsSent));

            // 적용된 응답의 시퀀스는 엄격히 증가(중복·역전 응답은 적용되지 않음)
            for (int i = 1; i < appliedSequences.Count; i++)
                Assert.That(appliedSequences[i], Is.GreaterThan(appliedSequences[i - 1]));

            // 서버 실행 간격은 서버 쿨타임 이상(행동 이중 실행 없음)
            for (int i = 1; i < executionTimes.Count; i++)
                Assert.That(executionTimes[i] - executionTimes[i - 1], Is.GreaterThanOrEqualTo(0.4 - 1e-9));

            // 이 설정에서는 중복·역전·타임아웃이 실제로 발생해야 검증이 의미 있다
            Assert.That(gate.Stats.DuplicatesIgnored, Is.GreaterThan(0));
            Assert.That(gate.Stats.StaleIgnored, Is.GreaterThan(0));
            Assert.That(gate.Stats.Timeouts, Is.GreaterThan(0));
        }

        [Test]
        public void ChaosWithoutTimeouts_GateAloneKeepsRequestsOutOfServerCooldown()
        {
            // 타임아웃이 없으면 게이트는 응답 전에 다음 요청을 보내지 않고, 쿨타임도 응답 도착 시점부터 센다.
            // 그러니 서버 쿨타임에 걸리는 요청이 하나도 없어야 한다. 서버 쪽 판정이 아니라 게이트가 이중 실행을 막는다는 증명.
            var clock = new ManualClock();
            var server = new MockGateServer(clock, seed: 77)
            {
                LatencySeconds = 0.25,
                LatencyJitterSeconds = 0.25,
                FailureRate = 0.2,
                DuplicateRate = 0.3,
                DefaultCooldownSeconds = 0.4,
            };
            var gate = new ActionGate(Action, server, clock, pendingTimeoutSeconds: 0);
            int cooldownRejections = 0;
            server.RequestJudged += (request, response) =>
            {
                if (response.FailReason == "server-cooldown")
                    cooldownRejections++;
            };

            for (int i = 0; i < 3000; i++)
            {
                gate.TryActivate();
                clock.Advance(0.02);
                server.Tick();
                gate.Tick();
            }

            Assert.That(server.ActionsExecuted, Is.GreaterThan(50));
            Assert.That(gate.Stats.DuplicatesIgnored, Is.GreaterThan(0));
            Assert.That(cooldownRejections, Is.EqualTo(0));
        }

        static string RunChaos(int seed)
        {
            var clock = new ManualClock();
            var server = new MockGateServer(clock, seed)
            {
                LatencySeconds = 0.2,
                LatencyJitterSeconds = 0.2,
                FailureRate = 0.3,
                DuplicateRate = 0.3,
            };
            var gate = new ActionGate(Action, server, clock, pendingTimeoutSeconds: 0.3);

            for (int i = 0; i < 500; i++)
            {
                gate.TryActivate();
                clock.Advance(0.05);
                server.Tick();
                gate.Tick();
            }

            GateStats s = gate.Stats;
            return $"{s.RequestsSent}/{s.Successes}/{s.Failures}/{s.Timeouts}/{s.DuplicatesIgnored}/{s.StaleIgnored}";
        }
    }
}
