using System;
using System.Collections.Generic;

namespace Jongreul.AuthorityRequest.Purchase
{
    /// <summary>
    /// 요청 ID → 처리 결과를 TTL 동안 보관한다. 같은 요청이 다시 오면 다시 실행하지 않고 이전 결과를 돌려주기 위함.
    /// TTL이 모두 같으므로 저장 순서가 곧 만료 순서다. 용량을 넘으면 가장 오래된 것부터 내보낸다.
    /// </summary>
    public sealed class IdempotencyCache<TKey, TResult>
    {
        readonly struct Entry
        {
            public readonly TKey Key;
            public readonly TResult Result;
            public readonly double ExpiresAt;

            public Entry(TKey key, TResult result, double expiresAt)
            {
                Key = key;
                Result = result;
                ExpiresAt = expiresAt;
            }
        }

        readonly IClock _clock;
        readonly Dictionary<TKey, LinkedListNode<Entry>> _index;
        readonly LinkedList<Entry> _byAge = new LinkedList<Entry>();

        public IdempotencyCache(IClock clock, double ttlSeconds, int capacity, IEqualityComparer<TKey> comparer = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (!(ttlSeconds > 0) || double.IsInfinity(ttlSeconds))
                throw new ArgumentOutOfRangeException(nameof(ttlSeconds));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            TtlSeconds = ttlSeconds;
            Capacity = capacity;
            _index = new Dictionary<TKey, LinkedListNode<Entry>>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public double TtlSeconds { get; }
        public int Capacity { get; }

        /// <summary>만료 전에 용량 때문에 내보낸 수. 0이 아니면 용량이 부족하다는 신호다.</summary>
        public int Evictions { get; private set; }

        public int Count
        {
            get
            {
                PurgeExpired();
                return _index.Count;
            }
        }

        public bool TryGet(TKey key, out TResult result)
        {
            PurgeExpired();
            if (_index.TryGetValue(key, out LinkedListNode<Entry> node))
            {
                result = node.Value.Result;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>처음 저장만 성공한다. 이미 살아 있는 키면 false(먼저 저장된 결과가 이긴다).</summary>
        public bool TryAdd(TKey key, TResult result)
        {
            PurgeExpired();
            if (_index.ContainsKey(key))
                return false;

            while (_index.Count >= Capacity)
                EvictOldest();

            LinkedListNode<Entry> node = _byAge.AddLast(new Entry(key, result, _clock.Now + TtlSeconds));
            _index.Add(key, node);
            return true;
        }

        /// <summary>만료된 항목을 지우고 지운 수를 돌려준다. 만료 시각 = 저장 시각 + TTL(그 순간부터 없음).</summary>
        public int PurgeExpired()
        {
            double now = _clock.Now;
            int removed = 0;
            while (_byAge.First != null && _byAge.First.Value.ExpiresAt <= now)
            {
                _index.Remove(_byAge.First.Value.Key);
                _byAge.RemoveFirst();
                removed++;
            }

            return removed;
        }

        public void Clear()
        {
            _index.Clear();
            _byAge.Clear();
        }

        void EvictOldest()
        {
            LinkedListNode<Entry> oldest = _byAge.First;
            _index.Remove(oldest.Value.Key);
            _byAge.RemoveFirst();
            Evictions++;
        }
    }
}
