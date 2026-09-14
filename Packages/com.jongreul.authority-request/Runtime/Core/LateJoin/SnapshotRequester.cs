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
        /// <summary>스냅샷 적용 완료. 변경을 버전 순서대로 바로 적용한다.</summary>
        Synced,
    }

    /// <summary>
    /// 받는 쪽이 동기화 책임을 진다. 준비가 되면 스냅샷을 요청하고,
    /// 요청 중에 들어온 변경은 버퍼에 두었다가 스냅샷 적용 뒤 버전 순서대로 이어 붙인다.
    /// 버전에 빈칸이 생기면(변경 유실·순서 역전) 스냅샷을 다시 요청해 맞춘다.
    /// </summary>
    public sealed class SnapshotRequester<T>
    {
        /// <summary>요청 전·요청 중 버퍼 상한. 넘치면 가장 오래된 것부터 버린다(버린 만큼은 스냅샷이 메운다).</summary>
        public const int MaxBufferedDeltas = 4096;

        readonly T[] _values;
        readonly T _defaultValue;
        readonly IEqualityComparer<T> _comparer;
        readonly SortedDictionary<long, SlotDelta<T>> _buffer = new SortedDictionary<long, SlotDelta<T>>();

        long _requestCounter;
        long _activeRequestId;

        public SnapshotRequester(int clientId, int slotCount, T defaultValue = default,
            IEqualityComparer<T> comparer = null)
        {
            if (slotCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(slotCount));

            ClientId = clientId;
            _values = new T[slotCount];
            _defaultValue = defaultValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;

            for (int i = 0; i < slotCount; i++)
                _values[i] = defaultValue;
        }

        public int ClientId { get; }
        public int SlotCount => _values.Length;
        public SyncState State { get; private set; } = SyncState.Idle;

        /// <summary>적용을 마친 상태 버전. 첫 스냅샷 전에는 0.</summary>
        public long Version { get; private set; }

        public int BufferedCount => _buffer.Count;
        public int SnapshotsApplied { get; private set; }
        public int SnapshotsIgnored { get; private set; }
        public int DeltasApplied { get; private set; }
        public int DeltasIgnored { get; private set; }
        public int GapsDetected { get; private set; }

        /// <summary>서버로 보낼 스냅샷 요청. 직접 요청과 빈칸 복구 요청 모두 여기로 나간다.</summary>
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
            State = SyncState.Requesting;
            RequestReady?.Invoke(new SnapshotRequest(ClientId, _activeRequestId));
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

            // 요청 중에 모인 변경 중 스냅샷 이후 것만 버전 순서대로 이어 붙인다.
            long version = snapshot.Version;
            bool gap = false;
            foreach (SlotDelta<T> delta in _buffer.Values)
            {
                if (delta.Version <= version)
                {
                    DeltasIgnored++;
                    continue;
                }

                if (delta.Version != version + 1)
                {
                    gap = true;
                    break;
                }

                next[delta.Slot] = delta.Value;
                version = delta.Version;
                DeltasApplied++;
            }

            _buffer.Clear();
            Commit(next);
            Version = version;
            SnapshotsApplied++;
            State = SyncState.Synced;

            if (gap)
            {
                GapsDetected++;
                RequestSnapshot();
            }
            else
            {
                Synced?.Invoke();
            }

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
                // 중간 변경을 못 받았다. 스냅샷으로 다시 맞춘다.
                GapsDetected++;
                Buffer(delta);
                RequestSnapshot();
                return;
            }

            SetValue(delta.Slot, delta.Value);
            Version = delta.Version;
            DeltasApplied++;
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
