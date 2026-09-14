using Jongreul.AuthorityRequest.Purchase;
using NUnit.Framework;

namespace Jongreul.AuthorityRequest.Tests.Purchase
{
    public class PurchaseLedgerTests
    {
        const int Player = 1;

        ManualClock _clock;
        PurchaseLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            var prices = new DictionaryPriceTable().Add("hat", 300).Add("cape", 500);
            _ledger = new PurchaseLedger(prices, _clock, replayTtlSeconds: 60);
            _ledger.SetBalance(Player, 1000);
        }

        [Test]
        public void Purchase_ChargesServerPrice()
        {
            PurchaseResult result = _ledger.Purchase(Player, "hat", requestId: 1);

            Assert.That(result.Status, Is.EqualTo(PurchaseStatus.Success));
            Assert.That(result.Price, Is.EqualTo(300));
            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(700));
            Assert.That(_ledger.Owns(Player, "hat"), Is.True);
        }

        [Test]
        public void SameRequestIdTwice_ChargesOnce_SecondIsReplay()
        {
            PurchaseResult first = _ledger.Purchase(Player, "hat", 1);
            PurchaseResult second = _ledger.Purchase(Player, "hat", 1);

            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(700));
            Assert.That(second.Status, Is.EqualTo(PurchaseStatus.Success));
            Assert.That(second.Replayed, Is.True);
            Assert.That(second.BalanceAfter, Is.EqualTo(first.BalanceAfter));
            Assert.That(_ledger.Executed, Is.EqualTo(1));
        }

        [Test]
        public void NewRequestId_ForOwnedItem_IsAlreadyOwned()
        {
            _ledger.Purchase(Player, "hat", 1);
            PurchaseResult result = _ledger.Purchase(Player, "hat", 2);

            Assert.That(result.Status, Is.EqualTo(PurchaseStatus.AlreadyOwned));
            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(700));
        }

        [Test]
        public void InsufficientFunds_DoesNotCharge()
        {
            _ledger.SetBalance(Player, 100);
            PurchaseResult result = _ledger.Purchase(Player, "cape", 1);

            Assert.That(result.Status, Is.EqualTo(PurchaseStatus.InsufficientFunds));
            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(100));
            Assert.That(_ledger.Owns(Player, "cape"), Is.False);
        }

        [Test]
        public void UnknownItem_IsRejected()
        {
            PurchaseResult result = _ledger.Purchase(Player, "sword", 1);

            Assert.That(result.Status, Is.EqualTo(PurchaseStatus.UnknownItem));
            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(1000));
        }

        [Test]
        public void RequestIdReusedForDifferentItem_IsConflict()
        {
            _ledger.Purchase(Player, "hat", 1);
            PurchaseResult result = _ledger.Purchase(Player, "cape", 1);

            Assert.That(result.Status, Is.EqualTo(PurchaseStatus.RequestIdConflict));
            Assert.That(_ledger.Owns(Player, "cape"), Is.False);
            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(700));
        }

        [Test]
        public void SameRequestId_DifferentPlayers_BothExecute()
        {
            _ledger.SetBalance(2, 1000);
            _ledger.Purchase(Player, "hat", 5);
            PurchaseResult other = _ledger.Purchase(2, "hat", 5);

            Assert.That(other.Status, Is.EqualTo(PurchaseStatus.Success));
            Assert.That(other.Replayed, Is.False);
            Assert.That(_ledger.GetBalance(2), Is.EqualTo(700));
        }

        [Test]
        public void FailedResult_IsReplayedForSameRequestId()
        {
            _ledger.SetBalance(Player, 100);
            _ledger.Purchase(Player, "hat", 1);
            _ledger.SetBalance(Player, 1000);

            PurchaseResult retry = _ledger.Purchase(Player, "hat", 1);

            Assert.That(retry.Status, Is.EqualTo(PurchaseStatus.InsufficientFunds));
            Assert.That(retry.Replayed, Is.True);
            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(1000));
        }

        [Test]
        public void AfterReplayTtl_OwnershipStillPreventsDoubleCharge()
        {
            _ledger.Purchase(Player, "hat", 1);
            _clock.Advance(60);

            PurchaseResult late = _ledger.Purchase(Player, "hat", 1);

            Assert.That(late.Replayed, Is.False);
            Assert.That(late.Status, Is.EqualTo(PurchaseStatus.AlreadyOwned));
            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(700));
        }

        [Test]
        public void ForgetPlayer_ClearsBalanceAndOwnership()
        {
            _ledger.Purchase(Player, "hat", 1);

            _ledger.ForgetPlayer(Player);

            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(0));
            Assert.That(_ledger.Owns(Player, "hat"), Is.False);
            Assert.That(_ledger.GetOwned(Player).Count, Is.EqualTo(0));
        }

        [Test]
        public void ManyRetriesOfSameRequest_ChargeExactlyOnce()
        {
            for (int i = 0; i < 50; i++)
                _ledger.Purchase(Player, "cape", 9);

            Assert.That(_ledger.GetBalance(Player), Is.EqualTo(500));
            Assert.That(_ledger.Executed, Is.EqualTo(1));
            Assert.That(_ledger.Replays, Is.EqualTo(49));
        }
    }
}
