using System;
using System.Collections.Generic;
using Jongreul.AuthorityRequest.Gate;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.Gate
{
    public class ActionGateTests
    {
        const int Action = 1;

        ManualClock _clock;
        ScriptedGateServer _server;
        ActionGate _gate;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _server = new ScriptedGateServer();
            _gate = new ActionGate(Action, _server, _clock);
        }

        [TearDown]
        public void TearDown() => _gate.Dispose();

        void SucceedLast(double cooldown) =>
            _server.Respond(GateResponse.Succeeded(Action, _server.Last.Sequence, cooldown));

        void FailLast() =>
            _server.Respond(GateResponse.Failed(Action, _server.Last.Sequence, "rejected"));

        [Test]
        public void TryActivate_WhenReady_SendsOneRequestAndEntersPending()
        {
            GateInputResult result = _gate.TryActivate();

            Assert.That(result, Is.EqualTo(GateInputResult.Sent));
            Assert.That(_server.Requests.Count, Is.EqualTo(1));
            Assert.That(_gate.State, Is.EqualTo(GateState.Pending));
            Assert.That(_gate.PendingSequence, Is.EqualTo(_server.Last.Sequence));
        }

        [Test]
        public void RapidTaps_TenTimes_SendsOnlyOneRequest()
        {
            for (int i = 0; i < 10; i++)
                _gate.TryActivate();

            Assert.That(_server.Requests.Count, Is.EqualTo(1));
            Assert.That(_gate.Stats.InputsRejected, Is.EqualTo(9));
        }

        [Test]
        public void Input_WhilePending_IsRejectedAsPending()
        {
            _gate.TryActivate();
            _clock.Advance(1.0);

            Assert.That(_gate.TryActivate(), Is.EqualTo(GateInputResult.RejectedPending));
            Assert.That(_server.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Success_EntersCooldown_WithServerDuration()
        {
            _gate.TryActivate();
            SucceedLast(2.5);

            Assert.That(_gate.State, Is.EqualTo(GateState.Cooldown));
            Assert.That(_gate.CooldownDuration, Is.EqualTo(2.5));
            Assert.That(_gate.CooldownRemaining, Is.EqualTo(2.5));
        }

        [TestCase(0.5)]
        [TestCase(3.0)]
        public void Cooldown_LengthComesFromServer(double serverCooldown)
        {
            _gate.TryActivate();
            SucceedLast(serverCooldown);

            _clock.Advance(serverCooldown - 0.01);
            _gate.Tick();
            Assert.That(_gate.State, Is.EqualTo(GateState.Cooldown));

            _clock.Advance(0.01);
            _gate.Tick();
            Assert.That(_gate.State, Is.EqualTo(GateState.Ready));
        }

        [Test]
        public void Input_WhileCooldown_IsRejectedAsCooldown()
        {
            _gate.TryActivate();
            SucceedLast(1.0);

            Assert.That(_gate.TryActivate(), Is.EqualTo(GateInputResult.RejectedCooldown));
            Assert.That(_server.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryActivate_AfterCooldownExpiry_SendsWithoutExplicitTick()
        {
            _gate.TryActivate();
            SucceedLast(1.0);
            _clock.Advance(1.0);

            Assert.That(_gate.TryActivate(), Is.EqualTo(GateInputResult.Sent));
            Assert.That(_server.Requests.Count, Is.EqualTo(2));
        }

        [Test]
        public void Fail_ReturnsToReadyImmediately()
        {
            _gate.TryActivate();
            FailLast();

            Assert.That(_gate.State, Is.EqualTo(GateState.Ready));
            Assert.That(_gate.Stats.Failures, Is.EqualTo(1));
        }

        [Test]
        public void Fail_AllowsRetryWithNewSequence()
        {
            _gate.TryActivate();
            long first = _server.Last.Sequence;
            FailLast();

            Assert.That(_gate.TryActivate(), Is.EqualTo(GateInputResult.Sent));
            Assert.That(_server.Last.Sequence, Is.GreaterThan(first));
        }

        [Test]
        public void DuplicateSuccess_IsIgnored_AndDoesNotRestartCooldown()
        {
            _gate.TryActivate();
            SucceedLast(2.0);
            _clock.Advance(1.5);

            SucceedLast(2.0);

            Assert.That(_gate.Stats.DuplicatesIgnored, Is.EqualTo(1));
            Assert.That(_gate.CooldownRemaining, Is.EqualTo(0.5).Within(1e-9));
        }

        [Test]
        public void DuplicateOfPreviousResponse_DoesNotResolveNextRequest()
        {
            _gate.TryActivate();
            GateResponse first = GateResponse.Succeeded(Action, _server.Last.Sequence, 0);
            _server.Respond(first);

            _gate.TryActivate();
            _server.Respond(first);

            Assert.That(_gate.State, Is.EqualTo(GateState.Pending));
            Assert.That(_gate.Stats.DuplicatesIgnored, Is.EqualTo(1));
        }

        [Test]
        public void LateResponse_AfterTimeout_IsIgnoredAsStale()
        {
            _gate.TryActivate();
            long timedOut = _server.Last.Sequence;
            _clock.Advance(ActionGate.DefaultPendingTimeoutSeconds);
            _gate.Tick();
            Assert.That(_gate.State, Is.EqualTo(GateState.Ready));

            _gate.TryActivate();
            _server.Respond(GateResponse.Succeeded(Action, timedOut, 1.0));

            Assert.That(_gate.State, Is.EqualTo(GateState.Pending));
            Assert.That(_gate.Stats.StaleIgnored, Is.EqualTo(1));
        }

        [Test]
        public void ReorderedResponses_NewerFirst_OlderIsIgnored()
        {
            var gate = new ActionGate(Action, _server, _clock, pendingTimeoutSeconds: 1.0);
            gate.TryActivate();
            long older = _server.Last.Sequence;
            _clock.Advance(1.0);
            gate.TryActivate(); // 타임아웃 뒤 재요청
            long newer = _server.Last.Sequence;

            _server.Respond(GateResponse.Succeeded(Action, newer, 3.0));
            _server.Respond(GateResponse.Failed(Action, older, "late"));

            Assert.That(gate.State, Is.EqualTo(GateState.Cooldown));
            Assert.That(gate.CooldownDuration, Is.EqualTo(3.0));
            Assert.That(gate.Stats.StaleIgnored, Is.EqualTo(1));
            gate.Dispose();
        }

        [Test]
        public void PendingTimeout_ReturnsToReady()
        {
            _gate.TryActivate();
            _clock.Advance(ActionGate.DefaultPendingTimeoutSeconds - 0.001);
            _gate.Tick();
            Assert.That(_gate.State, Is.EqualTo(GateState.Pending));

            _clock.Advance(0.001);
            _gate.Tick();
            Assert.That(_gate.State, Is.EqualTo(GateState.Ready));
            Assert.That(_gate.Stats.Timeouts, Is.EqualTo(1));
            Assert.That(_gate.PendingSequence, Is.EqualTo(0));
        }

        [Test]
        public void PendingTimeout_Disabled_KeepsPending()
        {
            var gate = new ActionGate(Action, _server, _clock, pendingTimeoutSeconds: 0);
            gate.TryActivate();
            _clock.Advance(1000);
            gate.Tick();

            Assert.That(gate.State, Is.EqualTo(GateState.Pending));
            gate.Dispose();
        }

        [Test]
        public void ResponseForOtherAction_IsIgnoredWithoutCounting()
        {
            _gate.TryActivate();
            _server.Respond(GateResponse.Succeeded(Action + 1, _server.Last.Sequence, 1.0));

            Assert.That(_gate.State, Is.EqualTo(GateState.Pending));
            Assert.That(_gate.Stats.ResponsesIgnored, Is.EqualTo(0));
        }

        [Test]
        public void UnknownSequence_IsIgnored()
        {
            _gate.TryActivate();
            _server.Respond(GateResponse.Succeeded(Action, 999, 1.0));

            Assert.That(_gate.State, Is.EqualTo(GateState.Pending));
            Assert.That(_gate.Stats.UnknownIgnored, Is.EqualTo(1));
        }

        [Test]
        public void ZeroCooldownSuccess_GoesStraightToReady()
        {
            _gate.TryActivate();
            SucceedLast(0);

            Assert.That(_gate.State, Is.EqualTo(GateState.Ready));
            Assert.That(_gate.Stats.Successes, Is.EqualTo(1));
        }

        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidServerCooldown_IsTreatedAsZero(double cooldown)
        {
            _gate.TryActivate();
            SucceedLast(cooldown);

            Assert.That(_gate.State, Is.EqualTo(GateState.Ready));
            Assert.That(_gate.CooldownDuration, Is.EqualTo(0));
        }

        [Test]
        public void SynchronousReplyInsideSend_IsApplied()
        {
            _server.AutoReply = request => GateResponse.Succeeded(request.ActionId, request.Sequence, 1.0);

            Assert.That(_gate.TryActivate(), Is.EqualTo(GateInputResult.Sent));
            Assert.That(_gate.State, Is.EqualTo(GateState.Cooldown));
        }

        [Test]
        public void Sequences_AreStrictlyIncreasing()
        {
            _server.AutoReply = request => GateResponse.Failed(request.ActionId, request.Sequence, "no");
            for (int i = 0; i < 5; i++)
                _gate.TryActivate();

            for (int i = 1; i < _server.Requests.Count; i++)
                Assert.That(_server.Requests[i].Sequence, Is.GreaterThan(_server.Requests[i - 1].Sequence));
        }

        [Test]
        public void Transitions_AreRaisedInOrder()
        {
            var log = new List<GateTransition>();
            _gate.Transitioned += log.Add;

            _gate.TryActivate();
            SucceedLast(1.0);
            _clock.Advance(1.0);
            _gate.Tick();

            Assert.That(log.Count, Is.EqualTo(3));
            Assert.That(log[0].Reason, Is.EqualTo(GateTransitionReason.RequestSent));
            Assert.That(log[1].Reason, Is.EqualTo(GateTransitionReason.ServerSuccess));
            Assert.That(log[1].To, Is.EqualTo(GateState.Cooldown));
            Assert.That(log[2].Reason, Is.EqualTo(GateTransitionReason.CooldownElapsed));
            Assert.That(log[2].To, Is.EqualTo(GateState.Ready));
        }

        [Test]
        public void CooldownProgress_GoesFromZeroToOne()
        {
            _gate.TryActivate();
            SucceedLast(2.0);
            Assert.That(_gate.CooldownProgress, Is.EqualTo(0).Within(1e-9));

            _clock.Advance(1.0);
            Assert.That(_gate.CooldownProgress, Is.EqualTo(0.5).Within(1e-9));

            _clock.Advance(1.0);
            _gate.Tick();
            Assert.That(_gate.CooldownProgress, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_UnsubscribesFromServer()
        {
            Assert.That(_server.SubscriberCount, Is.EqualTo(1));
            _gate.Dispose();
            Assert.That(_server.SubscriberCount, Is.EqualTo(0));
        }

        [Test]
        public void TryActivate_AfterDispose_Throws()
        {
            _gate.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _gate.TryActivate());
        }
    }
}
