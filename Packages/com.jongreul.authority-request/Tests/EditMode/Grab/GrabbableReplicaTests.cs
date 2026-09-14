using System.Collections.Generic;
using Jongreul.AuthorityRequest.Grab;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.Grab
{
    public class GrabbableReplicaTests
    {
        const int Cube = 10;

        [Test]
        public void RestBroadcast_PutsEveryPeerAtIdenticalPose()
        {
            var arbiter = new GrabArbiter();
            arbiter.RegisterObject(Cube, PoseData.At(0, 1, 0));
            var peers = new List<GrabbableReplica>();
            for (int player = 1; player <= 3; player++)
                peers.Add(new GrabbableReplica(player, Cube, PoseData.At(0, 1, 0)));
            arbiter.Changed += snapshot => peers.ForEach(peer => peer.Apply(snapshot));

            // 1번이 잡고 흔든다. 다른 피어는 받은 자세를 각자 다른 속도로 보간한다.
            arbiter.RequestGrab(1, Cube, Hand.Right);
            peers[0].SetLocalHandPose(PoseData.At(0.3f, 1.4f, 0.2f));
            peers[1].ApplyStreamedPose(PoseData.At(0.29f, 1.38f, 0.21f));
            peers[1].Step(0.35f);
            peers[2].ApplyStreamedPose(PoseData.At(0.25f, 1.3f, 0.2f));
            peers[2].Step(0.8f);

            // 던진 뒤 서버 물리가 멈춤을 판정한다.
            ReleaseResult release = arbiter.RequestRelease(1, Cube, PoseData.At(0.3f, 1.4f, 0.2f));
            peers.ForEach(peer => peer.ApplyStreamedPose(PoseData.At(1.1f, 0.4f, 0.9f)));
            peers[1].Step(0.5f);
            var restPose = new PoseData(1.2345f, 0.05f, 0.9876f, 0, 0.3826834f, 0, 0.9238795f);
            arbiter.ReportRest(Cube, release.Generation, restPose);

            foreach (GrabbableReplica peer in peers)
            {
                Assert.That(peer.State, Is.EqualTo(GrabbableState.Resting));
                Assert.That(peer.Pose, Is.EqualTo(restPose), $"player {peer.LocalPlayerId}");
            }
        }

        [Test]
        public void OutdatedBroadcast_IsIgnored()
        {
            var replica = new GrabbableReplica(1, Cube, PoseData.Identity);
            var resting = new GrabbableSnapshot(Cube, GrabbableState.Resting, GrabIds.None, Hand.Left, PoseData.At(1, 0, 0), 3);
            var older = new GrabbableSnapshot(Cube, GrabbableState.Held, 2, Hand.Left, PoseData.At(5, 5, 5), 2);

            replica.Apply(resting);

            Assert.That(replica.Apply(older), Is.False);
            Assert.That(replica.State, Is.EqualTo(GrabbableState.Resting));
            Assert.That(replica.Pose, Is.EqualTo(PoseData.At(1, 0, 0)));
        }

        [Test]
        public void NonOwner_InterpolatesTowardStreamedPose()
        {
            var replica = new GrabbableReplica(1, Cube, PoseData.At(0, 0, 0));
            replica.Apply(new GrabbableSnapshot(Cube, GrabbableState.Held, 2, Hand.Right, PoseData.At(0, 0, 0), 1));

            replica.ApplyStreamedPose(PoseData.At(2, 0, 0));
            replica.Step(0.5f);

            Assert.That(replica.Pose.Px, Is.EqualTo(1f).Within(1e-6));
        }

        [Test]
        public void LocalHolder_UsesHandPose_AndIgnoresStream()
        {
            var replica = new GrabbableReplica(1, Cube, PoseData.At(0, 0, 0));
            replica.Apply(new GrabbableSnapshot(Cube, GrabbableState.Held, 1, Hand.Right, PoseData.At(0, 0, 0), 1));

            replica.SetLocalHandPose(PoseData.At(0.5f, 1, 0));
            replica.ApplyStreamedPose(PoseData.At(9, 9, 9));
            replica.Step(1f);

            Assert.That(replica.IsLocallyHeld, Is.True);
            Assert.That(replica.Pose, Is.EqualTo(PoseData.At(0.5f, 1, 0)));
        }

        [Test]
        public void RestingReplica_IgnoresStreamAndStep()
        {
            var replica = new GrabbableReplica(1, Cube, PoseData.At(1, 1, 1));

            replica.ApplyStreamedPose(PoseData.At(4, 4, 4));
            replica.Step(1f);

            Assert.That(replica.Pose, Is.EqualTo(PoseData.At(1, 1, 1)));
        }
    }
}
