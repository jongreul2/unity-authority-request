using System;
using System.Collections.Generic;
using Fusion;
using Jongreul.AuthorityRequest.Grab;
using UnityEngine;

namespace Jongreul.AuthorityRequest.Networking
{
    /// <summary>오브젝트 하나의 복제 상태. 세대가 오르면 상태 변경, 같으면 자세만 갱신.</summary>
    public struct GrabbableNetState : INetworkStruct
    {
        public int Generation;
        public byte State;
        public PlayerRef Owner;
        public byte Hand;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>
    /// Fusion 2 그랩 동기화. 판정은 State Authority의 <see cref="GrabArbiter"/>가 하고,
    /// 결과는 [Networked] 딕셔너리 하나로 전원에게 복제된다(늦게 들어온 피어도 같은 상태).
    /// 놓인 오브젝트는 서버 물리만 움직이고, 멈추면 서버가 정지 자세를 확정한다.
    /// </summary>
    public sealed class GrabAuthority : NetworkBehaviour, IPlayerLeft
    {
        public const int MaxObjects = 32;

        [SerializeField] GrabbableView[] objects = Array.Empty<GrabbableView>();
        [SerializeField, Range(1, 2)] int maxHeldTotal = 1;
        [SerializeField] HeldLimitPolicy whenFull = HeldLimitPolicy.RejectNew;

        [Header("정지 판정(서버)")]
        [SerializeField] float restSpeed = 0.05f;
        [SerializeField] float restAngularSpeed = 0.1f;
        [SerializeField] int restTicks = 10;
        [SerializeField] float minSettleSeconds = 0.3f;

        [Header("릴리즈 검증(서버)")]
        [SerializeField] float maxReleaseOffset = 0.75f;
        [SerializeField] float maxThrowSpeed = 15f;

        [SerializeField, Range(0.05f, 1f)] float proxySmoothing = 0.35f;

        readonly Dictionary<int, GrabbableView> _views = new Dictionary<int, GrabbableView>();
        readonly Dictionary<int, int> _releaseGeneration = new Dictionary<int, int>();
        readonly Dictionary<int, int> _stillTicks = new Dictionary<int, int>();
        readonly Dictionary<int, float> _releaseTime = new Dictionary<int, float>();

        GrabArbiter _arbiter;
        int _releasingByRpc = GrabIds.None;

        [Networked, Capacity(MaxObjects)]
        NetworkDictionary<int, GrabbableNetState> States { get; }

        /// <summary>서버에서만 존재.</summary>
        public GrabArbiter Arbiter => _arbiter;

        public IReadOnlyCollection<GrabbableView> Views => _views.Values;

        /// <summary>이 피어가 보낸 그랩 요청의 결과.</summary>
        public event Action<GrabResult> GrabResultReceived;

        public override void Spawned()
        {
            // 프리팹으로 스폰하면 씬 오브젝트를 직렬화 참조할 수 없으므로 비어 있으면 씬에서 찾는다.
            // 뷰는 네트워크 오브젝트가 아니라 모든 피어에 같은 씬으로 존재하는 로컬 오브젝트다.
            GrabbableView[] views = objects != null && objects.Length > 0
                ? objects
                : FindObjectsByType<GrabbableView>(FindObjectsSortMode.None);

            foreach (GrabbableView view in views)
            {
                if (view == null)
                    continue;

                if (_views.Count >= MaxObjects)
                {
                    Debug.LogError($"[{nameof(GrabAuthority)}] 오브젝트가 {MaxObjects}개를 넘는다. 나머지는 무시한다.", this);
                    break;
                }

                if (_views.ContainsKey(view.Id))
                {
                    Debug.LogError($"[{nameof(GrabAuthority)}] 같은 Id({view.Id})의 뷰가 둘 이상이다: {view.name}", view);
                    continue;
                }

                _views.Add(view.Id, view);
                view.Bind(this, Runner.LocalPlayer.RawEncoded, HasStateAuthority);
            }

            if (!HasStateAuthority)
                return;

            _arbiter = new GrabArbiter(new HandInventoryPolicy(maxHeldTotal, whenFull));
            _arbiter.Changed += OnArbiterChanged;
            foreach (GrabbableView view in _views.Values)
                _arbiter.RegisterObject(view.Id, view.transform.ToPoseData());
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_arbiter != null)
                _arbiter.Changed -= OnArbiterChanged;

            // 사라진 권위 오브젝트로 RPC를 보내지 않게 뷰 연결을 끊는다.
            foreach (GrabbableView view in _views.Values)
                view.Unbind();
            _views.Clear();
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (HasStateAuthority && _arbiter != null)
                _arbiter.RemovePlayer(player.RawEncoded);
        }

        #region 클라이언트 → 서버
        // HostMode = SourceIsHostPlayer: Host가 부를 때도 info.Source가 Host 플레이어가 되게 한다(기본값은 None).

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public void RPC_RequestGrab(int objectId, byte hand, RpcInfo info = default)
        {
            if (!HasStateAuthority || _arbiter == null)
                return;

            GrabResult result = _arbiter.RequestGrab(info.Source.RawEncoded, objectId, (Hand)hand);
            RPC_GrabResult(info.Source, objectId, hand, (byte)result.Status, result.AutoReleasedObjectId);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable,
            HostMode = RpcHostMode.SourceIsHostPlayer)]
        public void RPC_HeldPose(int objectId, Vector3 position, Quaternion rotation, RpcInfo info = default)
        {
            if (!HasStateAuthority || _arbiter == null)
                return;

            if (_arbiter.ReportHeldPose(info.Source.RawEncoded, objectId, PoseConversions.ToPoseData(position, rotation)))
                WritePose(objectId, position, rotation);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public void RPC_Release(int objectId, Vector3 position, Quaternion rotation, Vector3 velocity,
            RpcInfo info = default)
        {
            if (!HasStateAuthority || _arbiter == null || !_views.TryGetValue(objectId, out GrabbableView view))
                return;

            int player = info.Source.RawEncoded;
            if (_arbiter.GetOwner(objectId) != player || !_arbiter.TryGetSnapshot(objectId, out GrabbableSnapshot held))
                return;

            // 클라이언트 값은 참고만 한다. 마지막으로 받은 손 자세에서 너무 멀면 그 자세를 쓰고, 속도는 상한으로 자른다.
            if (Vector3.Distance(position, held.Pose.Position()) > maxReleaseOffset)
            {
                position = held.Pose.Position();
                rotation = held.Pose.Rotation();
            }

            velocity = Vector3.ClampMagnitude(velocity, maxThrowSpeed);

            // 판정 중 발생하는 Changed 이벤트가 "서버가 직접 놓은 경우"로 처리되지 않게 표시해 둔다.
            ReleaseResult result;
            _releasingByRpc = objectId;
            try
            {
                result = _arbiter.RequestRelease(player, objectId, PoseConversions.ToPoseData(position, rotation));
            }
            finally
            {
                _releasingByRpc = GrabIds.None;
            }

            if (result.Accepted)
                BeginSettling(view, objectId, result.Generation, position, rotation, velocity);
        }

        #endregion

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_GrabResult([RpcTarget] PlayerRef target, int objectId, byte hand, byte status,
            int autoReleasedObjectId)
        {
            GrabResultReceived?.Invoke(new GrabResult((GrabStatus)status, target.RawEncoded, objectId, (Hand)hand,
                autoReleasedObjectId));
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || _arbiter == null)
                return;

            // 놓인 오브젝트: 서버 물리 자세를 흘려보내고, 멈춘 상태가 이어지면 정지를 확정한다.
            foreach (KeyValuePair<int, GrabbableView> pair in _views)
            {
                if (!_releaseGeneration.TryGetValue(pair.Key, out int generation))
                    continue;

                GrabbableView view = pair.Value;
                WritePose(pair.Key, view.transform.position, view.transform.rotation);

                int still = view.IsResting(restSpeed, restAngularSpeed) ? _stillTicks[pair.Key] + 1 : 0;
                _stillTicks[pair.Key] = still;
                // 속도 0으로 놓은 직후는 물리가 아직 한 스텝도 안 돌았을 수 있다. 최소 시간을 함께 본다.
                if (still < restTicks || Runner.SimulationTime - _releaseTime[pair.Key] < minSettleSeconds)
                    continue;

                _releaseGeneration.Remove(pair.Key);
                _arbiter.ReportRest(pair.Key, generation, view.transform.ToPoseData());
            }
        }

        public override void Render()
        {
            foreach (KeyValuePair<int, GrabbableView> pair in _views)
            {
                if (States.TryGet(pair.Key, out GrabbableNetState state))
                    pair.Value.ApplyNetState(ToSnapshot(pair.Key, state), proxySmoothing);
            }
        }

        void OnArbiterChanged(GrabbableSnapshot snapshot)
        {
            // 서버 판정이 곧 복제 상태다.
            States.Set(snapshot.ObjectId, new GrabbableNetState
            {
                Generation = snapshot.Generation,
                State = (byte)snapshot.State,
                Owner = snapshot.OwnerId == GrabIds.None ? PlayerRef.None : PlayerRef.FromEncoded(snapshot.OwnerId),
                Hand = (byte)snapshot.Hand,
                Position = snapshot.Pose.Position(),
                Rotation = snapshot.Pose.Rotation(),
            });

            if (!_views.TryGetValue(snapshot.ObjectId, out GrabbableView view))
                return;

            switch (snapshot.State)
            {
                case GrabbableState.Held:
                    _releaseGeneration.Remove(snapshot.ObjectId);
                    view.SetServerKinematic(true);
                    break;
                case GrabbableState.Resting:
                    view.SetServerKinematic(true);
                    snapshot.Pose.ApplyTo(view.transform);
                    break;
                case GrabbableState.Settling when snapshot.ObjectId != _releasingByRpc:
                    // 정책·퇴장·그랩 금지로 서버가 직접 놓은 경우. 그 자리에서 떨어뜨린다.
                    BeginSettling(view, snapshot.ObjectId, snapshot.Generation, snapshot.Pose.Position(),
                        snapshot.Pose.Rotation(), Vector3.zero);
                    break;
            }
        }

        void BeginSettling(GrabbableView view, int objectId, int generation, Vector3 position, Quaternion rotation,
            Vector3 velocity)
        {
            _releaseGeneration[objectId] = generation;
            _stillTicks[objectId] = 0;
            _releaseTime[objectId] = Runner.SimulationTime;
            view.BeginServerPhysics(position, rotation, velocity);
        }

        void WritePose(int objectId, Vector3 position, Quaternion rotation)
        {
            if (!States.TryGet(objectId, out GrabbableNetState state))
                return;
            state.Position = position;
            state.Rotation = rotation;
            States.Set(objectId, state);
        }

        static GrabbableSnapshot ToSnapshot(int objectId, GrabbableNetState state) =>
            new GrabbableSnapshot(objectId, (GrabbableState)state.State,
                state.Owner == PlayerRef.None ? GrabIds.None : state.Owner.RawEncoded, (Hand)state.Hand,
                PoseConversions.ToPoseData(state.Position, state.Rotation), state.Generation);
    }
}
