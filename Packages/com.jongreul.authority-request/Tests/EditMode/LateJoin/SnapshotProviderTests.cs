using System.Collections.Generic;
using Jongreul.AuthorityRequest.LateJoin;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.LateJoin
{
    public class SnapshotProviderTests
    {
        [Test]
        public void Snapshot_ContainsOnlyNonDefaultSlots()
        {
            var provider = new SnapshotProvider<int>(64);
            provider.Set(3, 30);
            provider.Set(40, 400);
            provider.Set(63, 630);

            SnapshotResponse<int> snapshot = provider.CreateSnapshot(new SnapshotRequest(1, 1));

            Assert.That(snapshot.Entries.Count, Is.EqualTo(3));
            Assert.That(snapshot.Entries[0].Slot, Is.EqualTo(3));
            Assert.That(snapshot.Entries[1].Slot, Is.EqualTo(40));
            Assert.That(snapshot.Entries[2].Value, Is.EqualTo(630));
        }

        [Test]
        public void SlotResetToDefault_IsExcludedFromSnapshot()
        {
            var provider = new SnapshotProvider<int>(8);
            provider.Set(2, 5);
            provider.Set(2, 0);

            SnapshotResponse<int> snapshot = provider.CreateSnapshot(new SnapshotRequest(1, 1));

            Assert.That(snapshot.Entries.Count, Is.EqualTo(0));
            Assert.That(provider.NonDefaultCount, Is.EqualTo(0));
            Assert.That(snapshot.Version, Is.EqualTo(2));
        }

        [Test]
        public void SettingSameValue_DoesNotBumpVersionOrNotify()
        {
            var provider = new SnapshotProvider<int>(4);
            var deltas = new List<SlotDelta<int>>();
            provider.Changed += deltas.Add;

            provider.Set(0, 1);
            bool changed = provider.Set(0, 1);

            Assert.That(changed, Is.False);
            Assert.That(provider.Version, Is.EqualTo(1));
            Assert.That(deltas.Count, Is.EqualTo(1));
        }

        [Test]
        public void EveryChange_BumpsVersionByOne()
        {
            var provider = new SnapshotProvider<int>(4);
            var deltas = new List<SlotDelta<int>>();
            provider.Changed += deltas.Add;

            provider.Set(0, 1);
            provider.Set(1, 1);
            provider.Set(0, 2);

            Assert.That(deltas.ConvertAll(d => d.Version), Is.EqualTo(new List<long> { 1, 2, 3 }));
        }

        [Test]
        public void CustomDefault_IsRespected()
        {
            var provider = new SnapshotProvider<string>(3, defaultValue: "empty");
            provider.Set(1, "ready");

            SnapshotResponse<string> snapshot = provider.CreateSnapshot(new SnapshotRequest(1, 1));

            Assert.That(provider.Get(0), Is.EqualTo("empty"));
            Assert.That(snapshot.Entries.Count, Is.EqualTo(1));
            Assert.That(snapshot.Entries[0].Value, Is.EqualTo("ready"));
        }
    }
}
