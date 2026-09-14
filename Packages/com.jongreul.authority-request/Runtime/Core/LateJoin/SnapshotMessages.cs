using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.LateJoin
{
    /// <summary>스냅샷 한 칸.</summary>
    public readonly struct SlotEntry<T>
    {
        public readonly int Slot;
        public readonly T Value;

        public SlotEntry(int slot, T value)
        {
            Slot = slot;
            Value = value;
        }
    }

    /// <summary>슬롯 하나의 변경. 버전은 전체 상태 기준으로 1씩 증가한다.</summary>
    public readonly struct SlotDelta<T>
    {
        public readonly long Version;
        public readonly int Slot;
        public readonly T Value;

        public SlotDelta(long version, int slot, T value)
        {
            Version = version;
            Slot = slot;
            Value = value;
        }

        public override string ToString() => $"Delta(v{Version}, slot={Slot}, {Value})";
    }

    /// <summary>받는 쪽이 준비됐을 때 보내는 스냅샷 요청.</summary>
    public readonly struct SnapshotRequest
    {
        public readonly int ClientId;
        public readonly long RequestId;

        public SnapshotRequest(int clientId, long requestId)
        {
            ClientId = clientId;
            RequestId = requestId;
        }

        public override string ToString() => $"SnapshotRequest(client={ClientId}, req={RequestId})";
    }

    /// <summary>스냅샷 응답. 기본값과 다른 슬롯만 담는다.</summary>
    public sealed class SnapshotResponse<T>
    {
        public SnapshotResponse(int clientId, long requestId, long version, IReadOnlyList<SlotEntry<T>> entries)
        {
            ClientId = clientId;
            RequestId = requestId;
            Version = version;
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public int ClientId { get; }
        public long RequestId { get; }

        /// <summary>스냅샷을 만든 시점의 상태 버전.</summary>
        public long Version { get; }

        public IReadOnlyList<SlotEntry<T>> Entries { get; }
    }
}
