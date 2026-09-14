using System;
using System.Collections.Generic;
using Jongreul.AuthorityRequest.Purchase;
using UnityEngine;

namespace Jongreul.AuthorityRequest
{
    /// <summary>서버가 들고 있는 가격표. 클라이언트 요청에는 가격을 싣지 않는다.</summary>
    [CreateAssetMenu(menuName = "Authority Request/Price Table", fileName = "PriceTable")]
    public sealed class PriceTableAsset : ScriptableObject, IPriceTable
    {
        [Serializable]
        public struct Entry
        {
            public string itemId;
            public int price;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();

        Dictionary<string, int> _lookup;

        public IReadOnlyList<Entry> Entries => entries;

        public void SetEntries(IEnumerable<Entry> values)
        {
            entries = new List<Entry>(values);
            _lookup = null;
        }

        public bool TryGetPrice(string itemId, out int price)
        {
            if (_lookup == null)
                BuildLookup();

            if (itemId != null && _lookup.TryGetValue(itemId, out price))
                return true;

            price = 0;
            return false;
        }

        void OnValidate() => _lookup = null;

        void BuildLookup()
        {
            _lookup = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Entry entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.itemId) && entry.price >= 0)
                    _lookup[entry.itemId] = entry.price;
            }
        }
    }
}
