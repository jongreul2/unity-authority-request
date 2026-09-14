using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.Purchase
{
    /// <summary>
    /// 서버 권위 구매 판정. 클라이언트는 (아이템, 요청 ID)만 보낸다.
    /// 가격은 서버 가격표에서 찾고, 같은 요청 ID가 다시 오면 다시 차감하지 않고 이전 결과를 돌려준다.
    /// 캐시가 만료되거나 밀려나도 보유 검사가 한 번 더 막는다(비소모성 아이템 기준).
    /// </summary>
    public sealed class PurchaseLedger
    {
        public const double DefaultReplayTtlSeconds = 120;
        public const int DefaultReplayCapacity = 4096;

        static readonly IReadOnlyCollection<string> NoItems = Array.Empty<string>();

        readonly IPriceTable _prices;
        readonly IdempotencyCache<PurchaseKey, PurchaseResult> _results;
        readonly Dictionary<int, int> _balances = new Dictionary<int, int>();
        readonly Dictionary<int, HashSet<string>> _owned = new Dictionary<int, HashSet<string>>();

        public PurchaseLedger(IPriceTable prices, IClock clock,
            double replayTtlSeconds = DefaultReplayTtlSeconds, int replayCapacity = DefaultReplayCapacity)
        {
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
            _results = new IdempotencyCache<PurchaseKey, PurchaseResult>(clock, replayTtlSeconds, replayCapacity);
        }

        /// <summary>실제로 판정을 실행한 수(재응답 제외).</summary>
        public int Executed { get; private set; }

        /// <summary>보관 결과를 다시 보낸 수.</summary>
        public int Replays { get; private set; }

        public IdempotencyCache<PurchaseKey, PurchaseResult> ReplayCache => _results;

        public event Action<PurchaseResult> Processed;

        public void SetBalance(int playerId, int balance)
        {
            if (balance < 0)
                throw new ArgumentOutOfRangeException(nameof(balance));
            _balances[playerId] = balance;
        }

        public int GetBalance(int playerId) => _balances.TryGetValue(playerId, out int balance) ? balance : 0;

        public bool Owns(int playerId, string itemId) =>
            itemId != null && _owned.TryGetValue(playerId, out HashSet<string> items) && items.Contains(itemId);

        public IReadOnlyCollection<string> GetOwned(int playerId) =>
            _owned.TryGetValue(playerId, out HashSet<string> items) ? items : NoItems;

        /// <summary>
        /// 플레이어의 잔고·보유 목록·보관 결과를 지운다. 세션 슬롯 번호처럼 재사용되는 ID를 키로 쓸 때,
        /// 다음 사람이 이전 사람의 상태나 구매 결과를 물려받지 않게 퇴장 시 호출한다.
        /// </summary>
        public void ForgetPlayer(int playerId)
        {
            _balances.Remove(playerId);
            _owned.Remove(playerId);
            _results.RemoveWhere(key => key.PlayerId == playerId);
        }

        public PurchaseResult Purchase(int playerId, string itemId, long requestId)
        {
            var key = new PurchaseKey(playerId, requestId);

            if (_results.TryGet(key, out PurchaseResult cached))
            {
                Replays++;
                PurchaseResult replay = string.Equals(cached.ItemId, itemId, StringComparison.Ordinal)
                    ? cached.AsReplay()
                    : new PurchaseResult(PurchaseStatus.RequestIdConflict, playerId, requestId, itemId, 0,
                        GetBalance(playerId), replayed: true);
                Processed?.Invoke(replay);
                return replay;
            }

            PurchaseResult result = Execute(playerId, itemId, requestId);
            // 실패 결과도 저장한다. 같은 요청 ID에는 언제나 같은 응답을 준다.
            _results.TryAdd(key, result);
            Processed?.Invoke(result);
            return result;
        }

        PurchaseResult Execute(int playerId, string itemId, long requestId)
        {
            Executed++;
            int balance = GetBalance(playerId);

            if (!_prices.TryGetPrice(itemId, out int price))
                return new PurchaseResult(PurchaseStatus.UnknownItem, playerId, requestId, itemId, 0, balance);

            if (Owns(playerId, itemId))
                return new PurchaseResult(PurchaseStatus.AlreadyOwned, playerId, requestId, itemId, price, balance);

            if (balance < price)
                return new PurchaseResult(PurchaseStatus.InsufficientFunds, playerId, requestId, itemId, price, balance);

            balance -= price;
            _balances[playerId] = balance;
            if (!_owned.TryGetValue(playerId, out HashSet<string> items))
            {
                items = new HashSet<string>(StringComparer.Ordinal);
                _owned.Add(playerId, items);
            }

            items.Add(itemId);
            return new PurchaseResult(PurchaseStatus.Success, playerId, requestId, itemId, price, balance);
        }
    }
}
