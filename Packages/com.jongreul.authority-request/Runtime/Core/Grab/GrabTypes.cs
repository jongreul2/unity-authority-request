namespace Jongreul.AuthorityRequest.Grab
{
    public static class GrabIds
    {
        /// <summary>플레이어·오브젝트 없음.</summary>
        public const int None = -1;
    }

    public enum Hand
    {
        Left = 0,
        Right = 1,
    }

    public enum HeldLimitPolicy
    {
        /// <summary>한도를 넘는 그랩을 거부한다.</summary>
        RejectNew,
        /// <summary>먼저 잡은 것을 자동으로 놓고 새것을 잡는다(같은 손이면 바꿔 쥐기).</summary>
        ReleaseOldest,
    }

    public enum GrabStatus
    {
        Accepted,
        UnknownObject,
        /// <summary>그랩 금지 상태의 플레이어(예: 상태 이상).</summary>
        Blocked,
        AlreadyHeld,
        HeldByOther,
        HandOccupied,
        LimitReached,
    }

    public enum ReleaseStatus
    {
        Accepted,
        UnknownObject,
        NotOwner,
    }

    /// <summary>오브젝트의 권위 상태.</summary>
    public enum GrabbableState
    {
        /// <summary>정지. 모든 피어가 같은 자세.</summary>
        Resting,
        Held,
        /// <summary>놓인 뒤 서버 물리가 움직이는 중. 다시 잡을 수 있다.</summary>
        Settling,
    }

    public readonly struct GrabResult
    {
        public readonly GrabStatus Status;
        public readonly int PlayerId;
        public readonly int ObjectId;
        public readonly Hand Hand;

        /// <summary>정책 때문에 자동으로 놓인 오브젝트. 없으면 <see cref="GrabIds.None"/>.</summary>
        public readonly int AutoReleasedObjectId;

        public GrabResult(GrabStatus status, int playerId, int objectId, Hand hand, int autoReleasedObjectId = GrabIds.None)
        {
            Status = status;
            PlayerId = playerId;
            ObjectId = objectId;
            Hand = hand;
            AutoReleasedObjectId = autoReleasedObjectId;
        }

        public bool Accepted => Status == GrabStatus.Accepted;

        public override string ToString() => $"Grab {Status}(player={PlayerId}, object={ObjectId}, {Hand})";
    }

    public readonly struct ReleaseResult
    {
        public readonly ReleaseStatus Status;
        public readonly int ObjectId;

        /// <summary>이 릴리즈의 세대. 정지 보고(<c>ReportRest</c>)에 그대로 넘긴다.</summary>
        public readonly int Generation;

        public ReleaseResult(ReleaseStatus status, int objectId, int generation)
        {
            Status = status;
            ObjectId = objectId;
            Generation = generation;
        }

        public bool Accepted => Status == ReleaseStatus.Accepted;
    }

    /// <summary>서버가 모든 피어에게 방송하는 오브젝트 상태. 세대는 상태가 바뀔 때마다 1씩 오른다.</summary>
    public readonly struct GrabbableSnapshot
    {
        public readonly int ObjectId;
        public readonly GrabbableState State;
        public readonly int OwnerId;
        public readonly Hand Hand;
        public readonly PoseData Pose;
        public readonly int Generation;

        public GrabbableSnapshot(int objectId, GrabbableState state, int ownerId, Hand hand, PoseData pose, int generation)
        {
            ObjectId = objectId;
            State = state;
            OwnerId = ownerId;
            Hand = hand;
            Pose = pose;
            Generation = generation;
        }

        public override string ToString() => $"#{ObjectId} {State} owner={OwnerId} gen={Generation} {Pose}";
    }
}
