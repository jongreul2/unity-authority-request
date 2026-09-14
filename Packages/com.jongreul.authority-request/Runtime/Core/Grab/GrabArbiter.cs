using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.Grab
{
    /// <summary>
    /// 서버 쪽 그랩 판정. 먼저 도착한 요청 한 명만 소유자가 되고, 소유자만 놓을 수 있다.
    /// 놓은 뒤에는 서버 물리가 멈춤을 판정해 정지 자세를 확정·방송한다. 모든 피어가 같은 자세로 멈춘다.
    /// </summary>
    public sealed class GrabArbiter
    {
        sealed class ObjectRecord
        {
            public int Owner = GrabIds.None;
            public Hand Hand;
            public GrabbableState State = GrabbableState.Resting;
            public PoseData Pose;
            public int Generation;
        }

        readonly Dictionary<int, ObjectRecord> _objects = new Dictionary<int, ObjectRecord>();
        readonly Dictionary<int, HandInventory> _inventories = new Dictionary<int, HandInventory>();
        readonly HashSet<int> _blocked = new HashSet<int>();

        public GrabArbiter(HandInventoryPolicy policy = null)
        {
            Policy = policy ?? new HandInventoryPolicy();
        }

        public HandInventoryPolicy Policy { get; }

        /// <summary>오브젝트 상태가 바뀔 때마다. 네트워크 계층은 이것을 전원에게 방송한다.</summary>
        public event Action<GrabbableSnapshot> Changed;

        public void RegisterObject(int objectId, PoseData pose)
        {
            if (objectId == GrabIds.None)
                throw new ArgumentOutOfRangeException(nameof(objectId));

            _objects.Add(objectId, new ObjectRecord { Pose = pose });
            Raise(objectId, _objects[objectId]);
        }

        public bool TryGetSnapshot(int objectId, out GrabbableSnapshot snapshot)
        {
            if (_objects.TryGetValue(objectId, out ObjectRecord record))
            {
                snapshot = ToSnapshot(objectId, record);
                return true;
            }

            snapshot = default;
            return false;
        }

        public int GetOwner(int objectId) =>
            _objects.TryGetValue(objectId, out ObjectRecord record) ? record.Owner : GrabIds.None;

        public IReadOnlyList<int> GetHeld(int playerId) => Inventory(playerId).HeldInOrder;

        public bool IsBlocked(int playerId) => _blocked.Contains(playerId);

        public GrabResult RequestGrab(int playerId, int objectId, Hand hand)
        {
            if (!_objects.TryGetValue(objectId, out ObjectRecord record))
                return new GrabResult(GrabStatus.UnknownObject, playerId, objectId, hand);

            if (_blocked.Contains(playerId))
                return new GrabResult(GrabStatus.Blocked, playerId, objectId, hand);

            if (record.Owner == playerId)
                return new GrabResult(GrabStatus.AlreadyHeld, playerId, objectId, hand);

            if (record.Owner != GrabIds.None)
                return new GrabResult(GrabStatus.HeldByOther, playerId, objectId, hand);

            HandInventory inventory = Inventory(playerId);
            HandDecision decision = Policy.Decide(inventory, hand);
            if (decision.Status != GrabStatus.Accepted)
                return new GrabResult(decision.Status, playerId, objectId, hand);

            if (decision.ReleaseFirst != GrabIds.None)
                Release(playerId, decision.ReleaseFirst, _objects[decision.ReleaseFirst].Pose);

            record.Owner = playerId;
            record.Hand = hand;
            record.State = GrabbableState.Held;
            record.Generation++;
            inventory.Put(hand, objectId);
            Raise(objectId, record);

            return new GrabResult(GrabStatus.Accepted, playerId, objectId, hand, decision.ReleaseFirst);
        }

        /// <summary>소유자가 놓음. 서버는 놓은 순간의 자세에서 물리를 이어 받는다.</summary>
        public ReleaseResult RequestRelease(int playerId, int objectId, PoseData releasePose)
        {
            if (!_objects.TryGetValue(objectId, out ObjectRecord record))
                return new ReleaseResult(ReleaseStatus.UnknownObject, objectId, 0);

            if (record.Owner != playerId)
                return new ReleaseResult(ReleaseStatus.NotOwner, objectId, record.Generation);

            Release(playerId, objectId, releasePose);
            return new ReleaseResult(ReleaseStatus.Accepted, objectId, record.Generation);
        }

        /// <summary>소유자가 들고 있는 동안의 자세. 소유자가 아니면 무시한다.</summary>
        public bool ReportHeldPose(int playerId, int objectId, PoseData pose)
        {
            if (!_objects.TryGetValue(objectId, out ObjectRecord record) || record.Owner != playerId)
                return false;

            record.Pose = pose;
            return true;
        }

        /// <summary>
        /// 서버 물리가 멈춤을 판정했을 때 호출. 그 사이 다시 잡혔으면(세대가 다르면) 무시한다.
        /// </summary>
        public bool ReportRest(int objectId, int releaseGeneration, PoseData restPose)
        {
            if (!_objects.TryGetValue(objectId, out ObjectRecord record) ||
                record.State != GrabbableState.Settling || record.Generation != releaseGeneration)
                return false;

            record.State = GrabbableState.Resting;
            record.Pose = restPose;
            record.Generation++;
            Raise(objectId, record);
            return true;
        }

        /// <summary>그랩 금지 설정. 금지되는 순간 들고 있던 것은 모두 놓는다.</summary>
        public void SetGrabBlocked(int playerId, bool blocked)
        {
            if (!blocked)
            {
                _blocked.Remove(playerId);
                return;
            }

            _blocked.Add(playerId);
            ReleaseAll(playerId);
        }

        /// <summary>플레이어 퇴장. 들고 있던 것을 모두 놓는다.</summary>
        public void RemovePlayer(int playerId)
        {
            ReleaseAll(playerId);
            _inventories.Remove(playerId);
            _blocked.Remove(playerId);
        }

        void ReleaseAll(int playerId)
        {
            if (!_inventories.TryGetValue(playerId, out HandInventory inventory))
                return;

            var held = new List<int>(inventory.HeldInOrder);
            foreach (int objectId in held)
                Release(playerId, objectId, _objects[objectId].Pose);
        }

        void Release(int playerId, int objectId, PoseData pose)
        {
            ObjectRecord record = _objects[objectId];
            record.Owner = GrabIds.None;
            record.State = GrabbableState.Settling;
            record.Pose = pose;
            record.Generation++;
            Inventory(playerId).Remove(objectId);
            Raise(objectId, record);
        }

        HandInventory Inventory(int playerId)
        {
            if (!_inventories.TryGetValue(playerId, out HandInventory inventory))
            {
                inventory = new HandInventory();
                _inventories.Add(playerId, inventory);
            }

            return inventory;
        }

        void Raise(int objectId, ObjectRecord record) => Changed?.Invoke(ToSnapshot(objectId, record));

        static GrabbableSnapshot ToSnapshot(int objectId, ObjectRecord record) =>
            new GrabbableSnapshot(objectId, record.State, record.Owner, record.Hand, record.Pose, record.Generation);
    }
}
