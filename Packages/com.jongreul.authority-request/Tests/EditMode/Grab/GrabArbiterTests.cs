using System.Collections.Generic;
using Jongreul.AuthorityRequest.Grab;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.Grab
{
    public class GrabArbiterTests
    {
        const int Alice = 1;
        const int Bob = 2;
        const int Cube = 10;
        const int Ball = 11;
        const int Stone = 12;

        static GrabArbiter Create(int maxHeld = 1, HeldLimitPolicy whenFull = HeldLimitPolicy.RejectNew)
        {
            var arbiter = new GrabArbiter(new HandInventoryPolicy(maxHeld, whenFull));
            arbiter.RegisterObject(Cube, PoseData.At(0, 1, 0));
            arbiter.RegisterObject(Ball, PoseData.At(1, 1, 0));
            arbiter.RegisterObject(Stone, PoseData.At(2, 1, 0));
            return arbiter;
        }

        static GrabbableSnapshot Snapshot(GrabArbiter arbiter, int objectId)
        {
            arbiter.TryGetSnapshot(objectId, out GrabbableSnapshot snapshot);
            return snapshot;
        }

        [Test]
        public void FirstGrab_Wins_SecondIsRejected()
        {
            GrabArbiter arbiter = Create();

            GrabResult first = arbiter.RequestGrab(Alice, Cube, Hand.Right);
            GrabResult second = arbiter.RequestGrab(Bob, Cube, Hand.Left);

            Assert.That(first.Accepted, Is.True);
            Assert.That(second.Status, Is.EqualTo(GrabStatus.HeldByOther));
            Assert.That(arbiter.GetOwner(Cube), Is.EqualTo(Alice));
        }

        [Test]
        public void TenPlayersAtOnce_ExactlyOneOwner()
        {
            GrabArbiter arbiter = Create();
            int accepted = 0;

            for (int player = 100; player < 110; player++)
            {
                if (arbiter.RequestGrab(player, Cube, Hand.Right).Accepted)
                    accepted++;
            }

            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(arbiter.GetOwner(Cube), Is.EqualTo(100));
        }

        [Test]
        public void AfterRelease_AnotherPlayerCanGrab()
        {
            GrabArbiter arbiter = Create();
            arbiter.RequestGrab(Alice, Cube, Hand.Right);
            arbiter.RequestRelease(Alice, Cube, PoseData.At(0, 1, 0));

            Assert.That(arbiter.RequestGrab(Bob, Cube, Hand.Left).Accepted, Is.True);
        }

        [Test]
        public void Release_ByNonOwner_IsRejected()
        {
            GrabArbiter arbiter = Create();
            arbiter.RequestGrab(Alice, Cube, Hand.Right);

            ReleaseResult result = arbiter.RequestRelease(Bob, Cube, PoseData.At(5, 5, 5));

            Assert.That(result.Status, Is.EqualTo(ReleaseStatus.NotOwner));
            Assert.That(arbiter.GetOwner(Cube), Is.EqualTo(Alice));
        }

        [Test]
        public void Release_EntersSettling_RestReportMakesItResting()
        {
            GrabArbiter arbiter = Create();
            arbiter.RequestGrab(Alice, Cube, Hand.Right);

            ReleaseResult release = arbiter.RequestRelease(Alice, Cube, PoseData.At(0, 2, 0));
            Assert.That(Snapshot(arbiter, Cube).State, Is.EqualTo(GrabbableState.Settling));

            bool rested = arbiter.ReportRest(Cube, release.Generation, PoseData.At(0, 0.5f, 0));

            Assert.That(rested, Is.True);
            Assert.That(Snapshot(arbiter, Cube).State, Is.EqualTo(GrabbableState.Resting));
            Assert.That(Snapshot(arbiter, Cube).Pose, Is.EqualTo(PoseData.At(0, 0.5f, 0)));
        }

        [Test]
        public void GrabWhileSettling_IsAllowed_AndStaleRestReportIsIgnored()
        {
            GrabArbiter arbiter = Create();
            arbiter.RequestGrab(Alice, Cube, Hand.Right);
            ReleaseResult thrown = arbiter.RequestRelease(Alice, Cube, PoseData.At(0, 2, 0));

            GrabResult caught = arbiter.RequestGrab(Bob, Cube, Hand.Left);
            bool rested = arbiter.ReportRest(Cube, thrown.Generation, PoseData.At(0, 0, 0));

            Assert.That(caught.Accepted, Is.True);
            Assert.That(rested, Is.False);
            Assert.That(Snapshot(arbiter, Cube).State, Is.EqualTo(GrabbableState.Held));
            Assert.That(arbiter.GetOwner(Cube), Is.EqualTo(Bob));
        }

        [Test]
        public void RejectNew_SecondObject_IsLimitReached()
        {
            GrabArbiter arbiter = Create(maxHeld: 1, HeldLimitPolicy.RejectNew);
            arbiter.RequestGrab(Alice, Cube, Hand.Right);

            GrabResult result = arbiter.RequestGrab(Alice, Ball, Hand.Left);

            Assert.That(result.Status, Is.EqualTo(GrabStatus.LimitReached));
            Assert.That(arbiter.GetHeld(Alice), Is.EqualTo(new[] { Cube }));
            Assert.That(arbiter.GetOwner(Ball), Is.EqualTo(GrabIds.None));
        }

        [Test]
        public void RejectNew_OccupiedHand_IsHandOccupied()
        {
            GrabArbiter arbiter = Create(maxHeld: 2, HeldLimitPolicy.RejectNew);
            arbiter.RequestGrab(Alice, Cube, Hand.Right);

            GrabResult result = arbiter.RequestGrab(Alice, Ball, Hand.Right);

            Assert.That(result.Status, Is.EqualTo(GrabStatus.HandOccupied));
        }

        [Test]
        public void ReleaseOldest_SecondObject_AutoReleasesFirst()
        {
            GrabArbiter arbiter = Create(maxHeld: 1, HeldLimitPolicy.ReleaseOldest);
            arbiter.RequestGrab(Alice, Cube, Hand.Right);

            GrabResult result = arbiter.RequestGrab(Alice, Ball, Hand.Left);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.AutoReleasedObjectId, Is.EqualTo(Cube));
            Assert.That(arbiter.GetOwner(Cube), Is.EqualTo(GrabIds.None));
            Assert.That(Snapshot(arbiter, Cube).State, Is.EqualTo(GrabbableState.Settling));
            Assert.That(arbiter.GetHeld(Alice), Is.EqualTo(new[] { Ball }));
        }

        [Test]
        public void ReleaseOldest_SameHand_SwapsOnlyThatHand()
        {
            GrabArbiter arbiter = Create(maxHeld: 2, HeldLimitPolicy.ReleaseOldest);
            arbiter.RequestGrab(Alice, Cube, Hand.Right);
            arbiter.RequestGrab(Alice, Ball, Hand.Left);

            GrabResult result = arbiter.RequestGrab(Alice, Stone, Hand.Left);

            Assert.That(result.AutoReleasedObjectId, Is.EqualTo(Ball));
            Assert.That(arbiter.GetHeld(Alice), Is.EqualTo(new[] { Cube, Stone }));
        }

        [Test]
        public void TwoHandLimit_AllowsOnePerHand()
        {
            GrabArbiter arbiter = Create(maxHeld: 2, HeldLimitPolicy.RejectNew);

            Assert.That(arbiter.RequestGrab(Alice, Cube, Hand.Right).Accepted, Is.True);
            Assert.That(arbiter.RequestGrab(Alice, Ball, Hand.Left).Accepted, Is.True);
            Assert.That(arbiter.RequestGrab(Alice, Stone, Hand.Left).Accepted, Is.False);
        }

        [Test]
        public void BlockedPlayer_CannotGrab()
        {
            GrabArbiter arbiter = Create();
            arbiter.SetGrabBlocked(Alice, true);

            Assert.That(arbiter.RequestGrab(Alice, Cube, Hand.Right).Status, Is.EqualTo(GrabStatus.Blocked));
        }

        [Test]
        public void BlockingWhileHolding_ForcesRelease()
        {
            GrabArbiter arbiter = Create();
            arbiter.RequestGrab(Alice, Cube, Hand.Right);

            arbiter.SetGrabBlocked(Alice, true);

            Assert.That(arbiter.GetOwner(Cube), Is.EqualTo(GrabIds.None));
            Assert.That(Snapshot(arbiter, Cube).State, Is.EqualTo(GrabbableState.Settling));
            Assert.That(arbiter.GetHeld(Alice).Count, Is.EqualTo(0));
        }

        [Test]
        public void Unblocking_AllowsGrabAgain()
        {
            GrabArbiter arbiter = Create();
            arbiter.SetGrabBlocked(Alice, true);
            arbiter.SetGrabBlocked(Alice, false);

            Assert.That(arbiter.RequestGrab(Alice, Cube, Hand.Right).Accepted, Is.True);
        }

        [Test]
        public void RemovePlayer_ReleasesEverything()
        {
            GrabArbiter arbiter = Create(maxHeld: 2);
            arbiter.RequestGrab(Alice, Cube, Hand.Right);
            arbiter.RequestGrab(Alice, Ball, Hand.Left);

            arbiter.RemovePlayer(Alice);

            Assert.That(arbiter.GetOwner(Cube), Is.EqualTo(GrabIds.None));
            Assert.That(arbiter.GetOwner(Ball), Is.EqualTo(GrabIds.None));
            Assert.That(arbiter.RequestGrab(Bob, Ball, Hand.Right).Accepted, Is.True);
        }

        [Test]
        public void UnknownObject_IsRejected()
        {
            GrabArbiter arbiter = Create();

            Assert.That(arbiter.RequestGrab(Alice, 999, Hand.Right).Status, Is.EqualTo(GrabStatus.UnknownObject));
            Assert.That(arbiter.RequestRelease(Alice, 999, PoseData.Identity).Status,
                Is.EqualTo(ReleaseStatus.UnknownObject));
        }

        [Test]
        public void GrabbingOwnObjectAgain_IsAlreadyHeld()
        {
            GrabArbiter arbiter = Create(maxHeld: 2);
            arbiter.RequestGrab(Alice, Cube, Hand.Right);

            Assert.That(arbiter.RequestGrab(Alice, Cube, Hand.Left).Status, Is.EqualTo(GrabStatus.AlreadyHeld));
        }

        [Test]
        public void HeldPose_FromNonOwner_IsIgnored()
        {
            GrabArbiter arbiter = Create();
            arbiter.RequestGrab(Alice, Cube, Hand.Right);

            Assert.That(arbiter.ReportHeldPose(Bob, Cube, PoseData.At(9, 9, 9)), Is.False);
            Assert.That(arbiter.ReportHeldPose(Alice, Cube, PoseData.At(0, 2, 1)), Is.True);
            Assert.That(Snapshot(arbiter, Cube).Pose, Is.EqualTo(PoseData.At(0, 2, 1)));
        }

        [Test]
        public void EveryStateChange_RaisesIncreasingGeneration()
        {
            GrabArbiter arbiter = Create();
            var generations = new List<int>();
            arbiter.Changed += snapshot =>
            {
                if (snapshot.ObjectId == Cube)
                    generations.Add(snapshot.Generation);
            };

            arbiter.RequestGrab(Alice, Cube, Hand.Right);
            ReleaseResult release = arbiter.RequestRelease(Alice, Cube, PoseData.At(0, 1, 0));
            arbiter.ReportRest(Cube, release.Generation, PoseData.At(0, 0, 0));

            Assert.That(generations, Is.EqualTo(new List<int> { 1, 2, 3 }));
        }
    }
}
