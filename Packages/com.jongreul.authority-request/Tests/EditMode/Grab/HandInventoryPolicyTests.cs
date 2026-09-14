using System;
using Jongreul.AuthorityRequest.Grab;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.Grab
{
    public class HandInventoryPolicyTests
    {
        [Test]
        public void EmptyHands_Accept()
        {
            var policy = new HandInventoryPolicy();

            HandDecision decision = policy.Decide(new HandInventory(), Hand.Left);

            Assert.That(decision.Status, Is.EqualTo(GrabStatus.Accepted));
            Assert.That(decision.ReleaseFirst, Is.EqualTo(GrabIds.None));
        }

        [TestCase(0)]
        [TestCase(3)]
        public void MaxHeldOutsideOneOrTwo_Throws(int maxHeld)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HandInventoryPolicy(maxHeld));
        }

        [Test]
        public void Decide_DoesNotChangeInventory()
        {
            var policy = new HandInventoryPolicy(1, HeldLimitPolicy.ReleaseOldest);
            var inventory = new HandInventory();
            inventory.Put(Hand.Right, 5);

            HandDecision decision = policy.Decide(inventory, Hand.Left);

            Assert.That(decision.ReleaseFirst, Is.EqualTo(5));
            Assert.That(inventory.Count, Is.EqualTo(1));
            Assert.That(inventory.Get(Hand.Right), Is.EqualTo(5));
        }

        [Test]
        public void Oldest_FollowsGrabOrderNotHand()
        {
            var inventory = new HandInventory();
            inventory.Put(Hand.Left, 7);
            inventory.Put(Hand.Right, 3);

            Assert.That(inventory.Oldest, Is.EqualTo(7));
            inventory.Remove(7);
            Assert.That(inventory.Oldest, Is.EqualTo(3));
            Assert.That(inventory.Get(Hand.Left), Is.EqualTo(GrabIds.None));
        }
    }
}
