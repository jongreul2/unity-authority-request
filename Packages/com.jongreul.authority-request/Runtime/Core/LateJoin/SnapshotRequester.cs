using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.LateJoin
{
    public enum SyncState
    {
        /// <summary>아직 요청 전. 들어오는 변경은 버퍼에 모은다.</summary>
        Idle,
        /// <summary>스냅샷을 기다리는 중. 들어오는 변경은 버퍼에 모은다.</summary>
        Requesting,
        /// <summary>스냅샷 적용 완료. 변경을 버전 순서대로 적용한다.</summary>
        Synced,
    }

    /// <summary>
    /// 받는 쪽이 동기화 책임을 진다. 준비가 되면 스냅샷을 요청하고,
    /// 요청 중에 들어온 변경은 버퍼에 두었다가 스냅샷 적용 뒤 버전 순서대로 이어 붙인다.
    /// 버전에 빈칸이 생기면 유예 시간 동안 기다리고(순서 역전이면 곧 메워진다),
    /// 그래도 안 메워지면(유실) 스냅샷을 다시 요청한다.
    /// </summary>
    public sealed class SnapshotRequester<T>
    {
        /// <summary>요청 전·요청 중 버퍼 상한. 넘치면 가장 오래된 것부터 버린다(버린 만큼은 스냅샷이 메운다).</summary>
        public const int MaxBufferedDeltas = 4096;

        public const double DefaultGapGraceSeconds = 0.3;
        public const double DefaultRequestTimeoutSeconds = 2.0;

        readonly T[] _values;
        readonly T _defaultValue;
        readonly IEqualityComparer<T> _comparer;
        readonly SortedDictionary<long, SlotDelta<T>> _buffer = new SortedDictionary<long, SlotDelta<T>>();
        readonly IClock _clock;
        readonly double _gapGraceSeconds;
        readonly double _requestTimeoutSeconds;

        long _requestCounter;
        long _activeRequestId;
        double _requestedAt;
        double _gapSince = -1;

        /// <param name="clock">
        /// 없으면 빈칸을 보는 즉시 재요청하고 요청 타임아웃도 없다.
        /// 있으면 <paramref name="gapGraceSeconds"/> 동안 빈칸이 메워지길 기다리고, <see cref="Tick"/>에서 타임아웃을 판정한다.
        /// </param>
        public SnapshotRequester(int clientId, int slotCount, T defaultValue = default,
            IEqualityComparer<T> comparer = null, IClock clock = null,
            double gapGraceSeconds = DefaultGapGraceSeconds,
            double requestTimeoutSeconds = DefaultRequestTimeoutSeconds)
        {
            if (slotCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(slotCount));

            ClientId = clientId;
            _values = new T[slotCount];
            _defaultValue = defaultValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;
            _clock = clock;
            _gapGraceSeconds = gapGraceSeconds;
            _requestTimeoutSeconds = requestTimeoutSeconds;

            for (int i = 0; i < slotCount; i++)
                _values[i] = defaultValue;
        }

        public int ClientId { get; }
        public int SlotCount => _values.Length;
        public SyncState State { get; private set; } = SyncState.Idle;

        /// <summary>적용을 마친 상태 버전. 첫 스냅샷 전에는 0.</summary>
        public long Version { get; private set; }

        /// <summary>빈칸이 메워지길 기다리는 중인가.</summary>
        public bool HasPendingGap => _gapSince >= 0;

        public int BufferedCount => _buffer.Count;
        public int SnapshotsApplied { get; private set; }
        public int SnapshotsIgnored { get; private set; }
        public int DeltasApplied { get; private set; }
        public int DeltasIgnored { get; private set; }

        /// <summary>동기화된 뒤 버전 순서를 건너뛰어 도착한 변경 수(순서 역전·유실 모두 포함).</summary>
        public int DeltasOutOfOrder { get; private set; }

        /// <summary>빈칸 때문에 스냅샷을 다시 요청한 수.</summary>
        public int GapsDetected { get; private set; }

        public int RequestTimeouts { get; private set; }

        /// <summary>서버로 보낼 스냅샷 요청. 직접 요청·빈칸 복구·타임아웃 재시도 모두 여기로 나간다.</summary>
        public event Action<SnapshotRequest> RequestReady;

        /// <summary>값이 실제로 바뀐 슬롯.</summary>
        public event Action<int, T> SlotChanged;

        /// <summary>스냅샷 적용이 끝나 Synced가 됐을 때.</summary>
        public event Action Synced;

        public T Get(int slot)
        {
            CheckSlot(slot);
            return _values[slot];
        }

        /// <summary>
        /// 스냅샷을 요청한다. 몇 번을 불러도 마지막 요청의 응답만 적용되므로 결과가 같다(멱등).
        /// </summary>
        public void RequestSnapshot()
        {
            _activeRequestId = ++_requestCounter;
            _requestedAt = _clock?.Now ?? 0;
            _gapSince = -1;
            State = SyncState.Requesting;
            RequestReady?.Invoke(new SnapshotRequest(ClientId, _activeRequestId));
        }

        /// <summary>시간 경과 처리: 빈칸 유예 만료, 스냅샷 요청 타임아웃. 시계가 없으면 아무것도 하지 않는다.</summary>
        public void Tick()
        {
            if (_clock == null)
                return;

            double now = _clock.Now;
            if (State == SyncState.Synced && _gapSince >= 0 && now - _gapSince >= _gapGraceSeconds)
            {
                GapsDetected++;
                RequestSnapshot();
            }
            else if (State == SyncState.Requesting && _requestTimeoutSeconds > 0 &&
                     now - _requestedAt >= _requestTimeoutSeconds)
            {
                // 요청이나 응답이 유실됐다. 새 요청 ID로 다시 보낸다(이전 응답이 늦게 와도 버려진다).
                RequestTimeouts++;
                RequestSnapshot();
            }
        }

        /// <summary>스냅샷 응답 적용. 마지막 요청에 대한 응답이 아니면 버린다.</summary>
        public bool ApplySnapshot(SnapshotResponse<T> snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (snapshot.ClientId != ClientId || State != SyncState.Requesting ||
                snapshot.RequestId != _activeRequestId || snapshot.Version < Version)
            {
                SnapshotsIgnored++;
                return false;
            }

            // 스냅샷에 없는 슬롯은 기본값이다.
            var next = new T[_values.Length];
            for (int i = 0; i < next.Length; i++)
                next[i] = _defaultValue;

            foreach (SlotEntry<T> entry in snapshot.Entries)
            {
                CheckSlot(entry.Slot);
                next[entry.Slot] = entry.Value;
            }

            // 요청 중에 모인 변경 중 스냅샷 이후 것만 버전 순서대로 이어 붙인다. 빈칸 뒤의 것은 남겨 둔다.
            long version = snapshot.Version;
            var remaining = new List<SlotDelta<T>>();
            foreach (SlotDelta<T> delta in _buffer.Values)
            {
                if (delta.Version <= version)
                {
                    DeltasIgnored++;
                    continue;
                }

                if (remaining.Count == 0 && delta.Version == version + 1)
                {
                    next[delta.Slot] = delta.Value;
                    version = delta.Version;
                    DeltasApplied++;
                    continue;
                }

                remaining.Add(delta);
            }

            _buffer.Clear();
            Commit(next);
            Version = version;
            SnapshotsApplied++;
            State = SyncState.Synced;
            _gapSince = -1;

            if (remaining.Count > 0)
            {
                if (!UsesGrace)
                {
                    GapsDetected++;
                    RequestSnapshot();
                    return true;
                }

                foreach (SlotDelta<T> delta in remaining)
                    _buffer[delta.Version] = delta;
                _gapSince = _clock.Now;
            }

            Synced?.Invoke();
            return true;
        }

        /// <summary>서버가 방송한 변경 하나.</summary>
        public void ApplyDelta(SlotDelta<T> delta)
        {
            CheckSlot(delta.Slot);

            if (delta.Version <= Version)
            {
                // 이미 반영한 변경(중복 전송·스냅샷에 포함된 변경)
                DeltasIgnored++;
                return;
            }

            if (State != SyncState.Synced)
            {
                Buffer(delta);
                return;
            }

            if (delta.Version != Version + 1)
            {
                // 앞 버전이 아직 안 왔다. 순서 역전이면 곧 메워지고, 유실이면 유예 뒤 스냅샷으로 맞춘다.
                DeltasOutOfOrder++;
                Buffer(delta);
                if (!UsesGrace)
                {
                    GapsDetected++;
                    RequestSnapshot();
                }
                else if (_gapSince < 0)
                {
                    _gapSince = _clock.Now;
                }

                return;
            }

            SetValue(delta.Slot, delta.Value);
            Version = delta.Version;
            DeltasApplied++;
            DrainBuffered();
        }

        bool UsesGrace => _clock != null && _gapGraceSeconds > 0;

        /// <summary>버퍼에서 다음 버전부터 이어지는 변경을 적용한다.</summary>
        void DrainBuffered()
        {
            while (_buffer.TryGetValue(Version + 1, out SlotDelta<T> next))
            {
                _buffer.Remove(next.Version);
                SetValue(next.Slot, next.Value);
                Version = next.Version;
                DeltasApplied++;
            }

            if (_buffer.Count == 0)
                _gapSince = -1;
            else if (_gapSince >= 0)
                _gapSince = _clock.Now; // 진전이 있었으니 남은 빈칸에 유예를 새로 준다
        }

        void Buffer(SlotDelta<T> delta)
        {
            _buffer[delta.Version] = delta;
            if (_buffer.Count <= MaxBufferedDeltas)
                return;

            using (SortedDictionary<long, SlotDelta<T>>.KeyCollection.Enumerator oldest = _buffer.Keys.GetEnumerator())
            {
                oldest.MoveNext();
                _buffer.Remove(oldest.Current);
            }
        }

        void Commit(T[] next)
        {
            for (int i = 0; i < next.Length; i++)
                SetValue(i, next[i]);
        }

        void SetValue(int slot, T value)
        {
            if (_comparer.Equals(_values[slot], value))
                return;

            _values[slot] = value;
            SlotChanged?.Invoke(slot, value);
        }

        void CheckSlot(int slot)
        {
            if ((uint)slot >= (uint)_values.Length)
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}
