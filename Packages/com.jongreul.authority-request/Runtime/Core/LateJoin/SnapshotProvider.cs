using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.LateJoin
{
    /// <summary>
    /// 서버 쪽 상태 보관소. 값이 바뀔 때마다 버전을 올리고 변경 이벤트를 낸다.
    /// 늦게 들어온 클라이언트가 요청하면 기본값과 다른 슬롯만 담은 스냅샷을 만든다.
    /// 새 플레이어가 생길 때마다 먼저 쏘지 않는다. 받는 쪽이 준비된 시점을 보내는 쪽은 모르기 때문이다.
    /// </summary>
    public sealed class SnapshotProvider<T>
    {
        readonly T[] _values;
        readonly T _defaultValue;
        readonly IEqualityComparer<T> _comparer;

        public SnapshotProvider(int slotCount, T defaultValue = default, IEqualityComparer<T> comparer = null)
        {
            if (slotCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(slotCount));

            _values = new T[slotCount];
            _defaultValue = defaultValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;

            for (int i = 0; i < slotCount; i++)
                _values[i] = defaultValue;
        }

        public int SlotCount => _values.Length;
        public T DefaultValue => _defaultValue;
        public long Version { get; private set; }

        /// <summary>기본값과 다른 슬롯 수 = 스냅샷 한 번의 크기.</summary>
        public int NonDefaultCount { get; private set; }

        public int SnapshotsServed { get; private set; }

        /// <summary>이미 동기화된 클라이언트에게 보낼 변경.</summary>
        public event Action<SlotDelta<T>> Changed;

        public T Get(int slot)
        {
            CheckSlot(slot);
            return _values[slot];
        }

        /// <summary>값을 바꾼다. 같은 값이면 버전도 이벤트도 없다.</summary>
        public bool Set(int slot, T value)
        {
            CheckSlot(slot);
            T previous = _values[slot];
            if (_comparer.Equals(previous, value))
                return false;

            bool wasDefault = _comparer.Equals(previous, _defaultValue);
            bool isDefault = _comparer.Equals(value, _defaultValue);
            if (wasDefault && !isDefault)
                NonDefaultCount++;
            else if (!wasDefault && isDefault)
                NonDefaultCount--;

            _values[slot] = value;
            Version++;
            Changed?.Invoke(new SlotDelta<T>(Version, slot, value));
            return true;
        }

        /// <summary>요청이 도착한 시점의 상태로 스냅샷을 만든다.</summary>
        public SnapshotResponse<T> CreateSnapshot(SnapshotRequest request)
        {
            var entries = new List<SlotEntry<T>>(NonDefaultCount);
            for (int i = 0; i < _values.Length; i++)
            {
                if (!_comparer.Equals(_values[i], _defaultValue))
                    entries.Add(new SlotEntry<T>(i, _values[i]));
            }

            SnapshotsServed++;
            return new SnapshotResponse<T>(request.ClientId, request.RequestId, Version, entries);
        }

        void CheckSlot(int slot)
        {
            if ((uint)slot >= (uint)_values.Length)
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}
