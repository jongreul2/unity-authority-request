namespace Jongreul.AuthorityRequest.Grab
{
    /// <summary>
    /// 피어 한 명이 보는 오브젝트. 서버 방송(<see cref="GrabbableSnapshot"/>)을 권위로 따른다.
    /// 내가 들고 있으면 내 손 자세, 남이 들고 있거나 서버 물리가 움직이는 중이면 받은 자세로 보간,
    /// 정지 방송을 받으면 그 자세로 정확히 맞춘다.
    /// </summary>
    public sealed class GrabbableReplica
    {
        PoseData _target;

        public GrabbableReplica(int localPlayerId, int objectId, PoseData initialPose)
        {
            LocalPlayerId = localPlayerId;
            ObjectId = objectId;
            Pose = initialPose;
            _target = initialPose;
        }

        public int LocalPlayerId { get; }
        public int ObjectId { get; }
        public GrabbableState State { get; private set; } = GrabbableState.Resting;
        public int OwnerId { get; private set; } = GrabIds.None;
        public int Generation { get; private set; }

        /// <summary>화면에 그리는 자세.</summary>
        public PoseData Pose { get; private set; }

        public bool IsLocallyHeld => State == GrabbableState.Held && OwnerId == LocalPlayerId;

        /// <summary>서버 방송 적용. 세대가 오르지 않은 방송(중복·순서 역전)은 버린다.</summary>
        public bool Apply(GrabbableSnapshot snapshot)
        {
            if (snapshot.ObjectId != ObjectId || snapshot.Generation <= Generation)
                return false;

            Generation = snapshot.Generation;
            State = snapshot.State;
            OwnerId = snapshot.OwnerId;

            if (State == GrabbableState.Resting)
            {
                // 정지는 보간하지 않는다. 모든 피어가 같은 자세로 멈춰야 한다.
                Pose = snapshot.Pose;
                _target = snapshot.Pose;
            }
            else if (!IsLocallyHeld)
            {
                _target = snapshot.Pose;
            }

            return true;
        }

        /// <summary>남이 들고 있거나 서버 물리가 움직이는 동안 들어오는 자세.</summary>
        public void ApplyStreamedPose(PoseData pose)
        {
            if (State != GrabbableState.Resting && !IsLocallyHeld)
                _target = pose;
        }

        /// <summary>내가 들고 있을 때는 내 손 자세를 그대로 쓴다.</summary>
        public void SetLocalHandPose(PoseData pose)
        {
            if (!IsLocallyHeld)
                return;

            Pose = pose;
            _target = pose;
        }

        /// <summary>보간 한 스텝. t는 이번 스텝에 목표까지 좁힐 비율(0~1).</summary>
        public void Step(float t)
        {
            if (State == GrabbableState.Resting || IsLocallyHeld)
                return;

            Pose = PoseData.Lerp(Pose, _target, t);
        }
    }
}
