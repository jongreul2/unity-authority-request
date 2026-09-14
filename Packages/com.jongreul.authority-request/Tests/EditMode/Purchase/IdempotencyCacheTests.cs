using System;
using Jongreul.AuthorityRequest.Purchase;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.Purchase
{
    public class IdempotencyCacheTests
    {
        ManualClock _clock;
        IdempotencyCache<long, string> _cache;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _cache = new IdempotencyCache<long, string>(_clock, ttlSeconds: 10, capacity: 3);
        }

        [Test]
        public void TryAdd_ThenTryGet_ReturnsStoredResult()
        {
            _cache.TryAdd(1, "ok");

            Assert.That(_cache.TryGet(1, out string result), Is.True);
            Assert.That(result, Is.EqualTo("ok"));
        }

        [Test]
        public void TryGet_Missing_ReturnsFalse()
        {
            Assert.That(_cache.TryGet(42, out _), Is.False);
        }

        [Test]
        public void Entry_JustBeforeTtl_IsStillHit()
        {
            _cache.TryAdd(1, "ok");
            _clock.Advance(9.999);

            Assert.That(_cache.TryGet(1, out _), Is.True);
        }

        [Test]
        public void Entry_AtTtl_IsExpired()
        {
            _cache.TryAdd(1, "ok");
            _clock.Advance(10);

            Assert.That(_cache.TryGet(1, out _), Is.False);
            Assert.That(_cache.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryAdd_ExistingKey_KeepsFirstResult()
        {
            _cache.TryAdd(1, "first");

            Assert.That(_cache.TryAdd(1, "second"), Is.False);
            _cache.TryGet(1, out string result);
            Assert.That(result, Is.EqualTo("first"));
        }

        [Test]
        public void TryAdd_AfterExpiry_StoresAgain()
        {
            _cache.TryAdd(1, "old");
            _clock.Advance(10);

            Assert.That(_cache.TryAdd(1, "new"), Is.True);
            _cache.TryGet(1, out string result);
            Assert.That(result, Is.EqualTo("new"));
        }

        [Test]
        public void OverCapacity_EvictsOldestFirst()
        {
            _cache.TryAdd(1, "a");
            _clock.Advance(1);
            _cache.TryAdd(2, "b");
            _cache.TryAdd(3, "c");
            _cache.TryAdd(4, "d");

            Assert.That(_cache.TryGet(1, out _), Is.False);
            Assert.That(_cache.TryGet(2, out _), Is.True);
            Assert.That(_cache.TryGet(4, out _), Is.True);
            Assert.That(_cache.Evictions, Is.EqualTo(1));
        }

        [Test]
        public void ExpiredEntries_DoNotCountAsEvictions()
        {
            _cache.TryAdd(1, "a");
            _cache.TryAdd(2, "b");
            _cache.TryAdd(3, "c");
            _clock.Advance(10);
            _cache.TryAdd(4, "d");

            Assert.That(_cache.Evictions, Is.EqualTo(0));
            Assert.That(_cache.Count, Is.EqualTo(1));
        }

        [Test]
        public void PurgeExpired_RemovesOnlyExpiredEntries()
        {
            _cache.TryAdd(1, "a");
            _clock.Advance(5);
            _cache.TryAdd(2, "b");
            _clock.Advance(5);

            Assert.That(_cache.PurgeExpired(), Is.EqualTo(1));
            Assert.That(_cache.TryGet(2, out _), Is.True);
        }

        [Test]
        public void CompositeKeys_AreSeparatedPerPlayer()
        {
            var cache = new IdempotencyCache<PurchaseKey, string>(_clock, 10, 10);
            cache.TryAdd(new PurchaseKey(1, 7), "player1");

            Assert.That(cache.TryAdd(new PurchaseKey(2, 7), "player2"), Is.True);
            cache.TryGet(new PurchaseKey(1, 7), out string first);
            Assert.That(first, Is.EqualTo("player1"));
        }

        [TestCase(0.0, 1)]
        [TestCase(-1.0, 1)]
        [TestCase(double.NaN, 1)]
        [TestCase(1.0, 0)]
        public void InvalidArguments_Throw(double ttl, int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyCache<int, int>(_clock, ttl, capacity));
        }

        [Test]
        public void RemoveWhere_RemovesOnlyMatchingKeys()
        {
            var cache = new IdempotencyCache<PurchaseKey, string>(_clock, 10, 10);
            cache.TryAdd(new PurchaseKey(1, 1), "a");
            cache.TryAdd(new PurchaseKey(2, 1), "b");
            cache.TryAdd(new PurchaseKey(1, 2), "c");

            int removed = cache.RemoveWhere(key => key.PlayerId == 1);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(cache.TryGet(new PurchaseKey(2, 1), out _), Is.True);
            Assert.That(cache.TryAdd(new PurchaseKey(1, 1), "again"), Is.True);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            _cache.TryAdd(1, "a");
            _cache.TryAdd(2, "b");
            _cache.Clear();

            Assert.That(_cache.Count, Is.EqualTo(0));
            Assert.That(_cache.TryAdd(1, "again"), Is.True);
        }
    }
}
