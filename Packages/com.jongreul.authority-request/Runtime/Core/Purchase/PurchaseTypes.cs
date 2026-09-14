using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.Purchase
{
    public enum PurchaseStatus
    {
        Success,
        UnknownItem,
        AlreadyOwned,
        InsufficientFunds,
        /// <summary>같은 요청 ID로 다른 아이템을 요청했다(클라이언트 버그나 조작).</summary>
        RequestIdConflict,
        /// <summary>서버가 지금 처리할 수 없다(복제 용량 부족 등). 차감하지 않았고 결과를 보관하지 않으므로 다시 시도할 수 있다.</summary>
        Unavailable,
    }

    /// <summary>요청 ID는 플레이어마다 따로 센다. 그래서 키는 (플레이어, 요청 ID) 쌍이다.</summary>
    public readonly struct PurchaseKey : IEquatable<PurchaseKey>
    {
        public readonly int PlayerId;
        public readonly long RequestId;

        public PurchaseKey(int playerId, long requestId)
        {
            PlayerId = playerId;
            RequestId = requestId;
        }

        public bool Equals(PurchaseKey other) => PlayerId == other.PlayerId && RequestId == other.RequestId;
        public override bool Equals(object obj) => obj is PurchaseKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(PlayerId, RequestId);
        public override string ToString() => $"(player={PlayerId}, req={RequestId})";
    }

    public readonly struct PurchaseResult
    {
        public readonly PurchaseStatus Status;
        public readonly int PlayerId;
        public readonly long RequestId;
        public readonly string ItemId;
        public readonly int Price;
        public readonly int BalanceAfter;

        /// <summary>새로 실행하지 않고 보관해 둔 결과를 다시 보냈는가.</summary>
        public readonly bool Replayed;

        public PurchaseResult(PurchaseStatus status, int playerId, long requestId, string itemId, int price,
            int balanceAfter, bool replayed = false)
        {
            Status = status;
            PlayerId = playerId;
            RequestId = requestId;
            ItemId = itemId;
            Price = price;
            BalanceAfter = balanceAfter;
            Replayed = replayed;
        }

        public bool Succeeded => Status == PurchaseStatus.Success;

        internal PurchaseResult AsReplay() =>
            new PurchaseResult(Status, PlayerId, RequestId, ItemId, Price, BalanceAfter, replayed: true);

        public override string ToString() =>
            $"{Status}(player={PlayerId}, req={RequestId}, item={ItemId}, price={Price}, balance={BalanceAfter}{(Replayed ? ", replay" : "")})";
    }

    /// <summary>가격표. 가격은 서버가 들고 있고 클라이언트 요청에는 싣지 않는다.</summary>
    public interface IPriceTable
    {
        bool TryGetPrice(string itemId, out int price);
    }

    public sealed class DictionaryPriceTable : IPriceTable
    {
        readonly Dictionary<string, int> _prices = new Dictionary<string, int>(StringComparer.Ordinal);

        public DictionaryPriceTable Add(string itemId, int price)
        {
            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("아이템 ID가 비어 있다.", nameof(itemId));
            if (price < 0)
                throw new ArgumentOutOfRangeException(nameof(price));

            _prices[itemId] = price;
            return this;
        }

        public bool TryGetPrice(string itemId, out int price)
        {
            if (itemId != null && _prices.TryGetValue(itemId, out price))
                return true;

            price = 0;
            return false;
        }
    }
}
