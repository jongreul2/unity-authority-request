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
        [SerializeField] float restSpeed = 0.05f;
        [SerializeField] int restTicks = 10;
        [SerializeField, Range(0.05f, 1f)] float proxySmoothing = 0.35f;

        readonly Dictionary<int, GrabbableView> _views = new Dictionary<int, GrabbableView>();
        readonly Dictionary<int, int> _releaseGeneration = new Dictionary<int, int>();
        readonly Dictionary<int, int> _stillTicks = new Dictionary<int, int>();

        GrabArbiter _arbiter;

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
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (HasStateAuthority && _arbiter != null)
                _arbiter.RemovePlayer(player.RawEncoded);
        }

        #region 클라이언트 → 서버

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestGrab(int objectId, byte hand, RpcInfo info = default)
        {
            if (!HasStateAuthority || _arbiter == null)
                return;

            GrabResult result = _arbiter.RequestGrab(info.Source.RawEncoded, objectId, (Hand)hand);
            RPC_GrabResult(info.Source, objectId, hand, (byte)result.Status, result.AutoReleasedObjectId);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable)]
        public void RPC_HeldPose(int objectId, Vector3 position, Quaternion rotation, RpcInfo info = default)
        {
            if (!HasStateAuthority || _arbiter == null)
                return;

            if (_arbiter.ReportHeldPose(info.Source.RawEncoded, objectId, PoseConversions.ToPoseData(position, rotation)))
                WritePose(objectId, position, rotation);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Release(int objectId, Vector3 position, Quaternion rotation, Vector3 velocity,
            RpcInfo info = default)
        {
            if (!HasStateAuthority || _arbiter == null)
                return;

            ReleaseResult result = _arbiter.RequestRelease(info.Source.RawEncoded, objectId,
                PoseConversions.ToPoseData(position, rotation));
            if (!result.Accepted || !_views.TryGetValue(objectId, out GrabbableView view))
                return;

            _releaseGeneration[objectId] = result.Generation;
            _stillTicks[objectId] = 0;
            view.BeginServerPhysics(position, rotation, velocity);
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

            // 놓인 오브젝트: 서버 물리 자세를 흘려보내고, 충분히 느려진 상태가 이어지면 정지를 확정한다.
            foreach (KeyValuePair<int, GrabbableView> pair in _views)
            {
                if (!_releaseGeneration.TryGetValue(pair.Key, out int generation))
                    continue;

                GrabbableView view = pair.Value;
                WritePose(pair.Key, view.transform.position, view.transform.rotation);

                int still = view.Speed < restSpeed ? _stillTicks[pair.Key] + 1 : 0;
                _stillTicks[pair.Key] = still;
                if (still < restTicks)
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

            if (snapshot.State == GrabbableState.Held)
            {
                _releaseGeneration.Remove(snapshot.ObjectId);
                view.SetServerKinematic(true);
            }
            else if (snapshot.State == GrabbableState.Resting)
            {
                view.SetServerKinematic(true);
                snapshot.Pose.ApplyTo(view.transform);
            }
            else if (!_releaseGeneration.ContainsKey(snapshot.ObjectId))
            {
                // 정책·퇴장으로 서버가 직접 놓은 경우. 그 자리에서 떨어뜨린다.
                ReleaseResult pending = new ReleaseResult(ReleaseStatus.Accepted, snapshot.ObjectId, snapshot.Generation);
                _releaseGeneration[snapshot.ObjectId] = pending.Generation;
                _stillTicks[snapshot.ObjectId] = 0;
                view.BeginServerPhysics(snapshot.Pose.Position(), snapshot.Pose.Rotation(), Vector3.zero);
            }
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
