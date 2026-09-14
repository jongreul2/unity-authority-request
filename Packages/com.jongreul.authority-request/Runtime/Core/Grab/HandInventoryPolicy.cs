using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.Grab
{
    /// <summary>플레이어 한 명이 양손에 쥔 오브젝트와 잡은 순서.</summary>
    public sealed class HandInventory
    {
        readonly int[] _byHand = { GrabIds.None, GrabIds.None };
        readonly List<int> _order = new List<int>(2);

        public int Get(Hand hand) => _byHand[(int)hand];
        public int Count => _order.Count;

        /// <summary>가장 먼저 잡은 오브젝트. 없으면 <see cref="GrabIds.None"/>.</summary>
        public int Oldest => _order.Count > 0 ? _order[0] : GrabIds.None;

        public IReadOnlyList<int> HeldInOrder => _order;

        public bool Contains(int objectId) => _order.Contains(objectId);

        internal void Put(Hand hand, int objectId)
        {
            if (_byHand[(int)hand] != GrabIds.None)
                throw new InvalidOperationException($"{hand} 손이 비어 있지 않다.");

            _byHand[(int)hand] = objectId;
            _order.Add(objectId);
        }

        internal bool Remove(int objectId)
        {
            if (!_order.Remove(objectId))
                return false;

            for (int i = 0; i < _byHand.Length; i++)
            {
                if (_byHand[i] == objectId)
                    _byHand[i] = GrabIds.None;
            }

            return true;
        }
    }

    /// <summary>그랩 요청 하나에 대한 손 정책의 판정.</summary>
    public readonly struct HandDecision
    {
        public readonly GrabStatus Status;

        /// <summary>승인 전에 먼저 놓아야 할 오브젝트. 없으면 <see cref="GrabIds.None"/>.</summary>
        public readonly int ReleaseFirst;

        HandDecision(GrabStatus status, int releaseFirst)
        {
            Status = status;
            ReleaseFirst = releaseFirst;
        }

        public static HandDecision Accept() => new HandDecision(GrabStatus.Accepted, GrabIds.None);
        public static HandDecision AcceptAfterReleasing(int objectId) => new HandDecision(GrabStatus.Accepted, objectId);
        public static HandDecision Reject(GrabStatus reason) => new HandDecision(reason, GrabIds.None);
    }

    /// <summary>
    /// 양손 보유 제한. 기본은 "양손 합쳐 1개". 한도를 넘는 그랩을 거부할지, 먼저 쥔 것을 놓을지 고른다.
    /// </summary>
    public sealed class HandInventoryPolicy
    {
        public HandInventoryPolicy(int maxHeldTotal = 1, HeldLimitPolicy whenFull = HeldLimitPolicy.RejectNew)
        {
            if (maxHeldTotal < 1 || maxHeldTotal > 2)
                throw new ArgumentOutOfRangeException(nameof(maxHeldTotal), "손은 두 개다. 1 또는 2.");

            MaxHeldTotal = maxHeldTotal;
            WhenFull = whenFull;
        }

        public int MaxHeldTotal { get; }
        public HeldLimitPolicy WhenFull { get; }

        public HandDecision Decide(HandInventory inventory, Hand hand)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            int inHand = inventory.Get(hand);
            if (inHand != GrabIds.None)
            {
                return WhenFull == HeldLimitPolicy.RejectNew
                    ? HandDecision.Reject(GrabStatus.HandOccupied)
                    : HandDecision.AcceptAfterReleasing(inHand);
            }

            if (inventory.Count >= MaxHeldTotal)
            {
                return WhenFull == HeldLimitPolicy.RejectNew
                    ? HandDecision.Reject(GrabStatus.LimitReached)
                    : HandDecision.AcceptAfterReleasing(inventory.Oldest);
            }

            return HandDecision.Accept();
        }
    }
}
