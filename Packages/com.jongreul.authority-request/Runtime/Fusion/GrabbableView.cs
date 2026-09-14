using Jongreul.AuthorityRequest.Grab;
using UnityEngine;

namespace Jongreul.AuthorityRequest.Networking
{
    /// <summary>
    /// 잡을 수 있는 오브젝트의 화면 쪽. 네트워크 오브젝트가 아니라 <see cref="GrabAuthority"/>에 등록된 뷰다.
    /// 서버에서는 놓인 동안 물리로 움직이고, 피어에서는 <see cref="GrabbableReplica"/>가 정한 자세를 그린다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GrabbableView : MonoBehaviour
    {
        [SerializeField] int id;
        [SerializeField] float heldPoseSendInterval = 0.05f;

        Rigidbody _body;
        GrabAuthority _authority;
        GrabbableReplica _replica;
        bool _isServer;
        float _nextPoseSend;

        public int Id => id;
        public GrabbableReplica Replica => _replica;
        public bool IsBound => _authority != null;
        public float Speed => _body != null && !_body.isKinematic ? _body.linearVelocity.magnitude : 0f;
        public bool IsLocallyHeld => _replica != null && _replica.IsLocallyHeld;

        public void SetId(int value) => id = value;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
        }

        internal void Bind(GrabAuthority authority, int localPlayerId, bool isServer)
        {
            _authority = authority;
            _isServer = isServer;
            _replica = new GrabbableReplica(localPlayerId, id, transform.ToPoseData());
            // 피어에서는 물리를 돌리지 않는다. 자세는 서버 상태가 정한다.
            _body.isKinematic = true;
        }

        internal void Unbind()
        {
            _authority = null;
            _replica = null;
        }

        /// <summary>
        /// 멈췄다고 볼 수 있는가. 물리 엔진이 재웠거나, 선속도·각속도가 모두 기준보다 작을 때.
        /// (선속도만 보면 천천히 넘어지는 물체를 모서리로 선 채 멈췄다고 판정할 수 있다.)
        /// </summary>
        public bool IsResting(float maxSpeed, float maxAngularSpeed)
        {
            if (_body.isKinematic || _body.IsSleeping())
                return true;

            return _body.linearVelocity.sqrMagnitude < maxSpeed * maxSpeed &&
                   _body.angularVelocity.sqrMagnitude < maxAngularSpeed * maxAngularSpeed;
        }

        #region 로컬 입력(데모 입력·XR 인터랙터가 호출)

        public void RequestGrab(Hand hand)
        {
            if (_authority != null)
                _authority.RPC_RequestGrab(id, (byte)hand);
        }

        /// <summary>들고 있는 동안 매 프레임 손 자세를 넘긴다. 서버로는 일정 간격으로만 보낸다.</summary>
        public void SetHandPose(Vector3 position, Quaternion rotation)
        {
            if (_authority == null || !IsLocallyHeld)
                return;

            _replica.SetLocalHandPose(PoseConversions.ToPoseData(position, rotation));
            transform.SetPositionAndRotation(position, rotation);

            if (Time.unscaledTime < _nextPoseSend)
                return;
            _nextPoseSend = Time.unscaledTime + heldPoseSendInterval;
            _authority.RPC_HeldPose(id, position, rotation);
        }

        public void Release(Vector3 velocity)
        {
            if (_authority != null)
                _authority.RPC_Release(id, transform.position, transform.rotation, velocity);
        }

        #endregion

        #region 서버 전용

        internal void SetServerKinematic(bool kinematic)
        {
            if (_isServer)
                _body.isKinematic = kinematic;
        }

        internal void BeginServerPhysics(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            if (!_isServer)
                return;

            transform.SetPositionAndRotation(position, rotation);
            _body.isKinematic = false;
            _body.linearVelocity = velocity;
            _body.WakeUp();
        }

        #endregion

        internal void ApplyNetState(GrabbableSnapshot snapshot, float smoothing)
        {
            if (_replica == null)
                return;

            if (!_replica.Apply(snapshot))
                _replica.ApplyStreamedPose(snapshot.Pose);

            // 서버는 놓인 동안 물리 결과가 곧 자세다.
            if (_isServer && !_body.isKinematic)
                return;

            _replica.Step(smoothing);
            if (!_replica.IsLocallyHeld)
                _replica.Pose.ApplyTo(transform);
        }
    }
}
