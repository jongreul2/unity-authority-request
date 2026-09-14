using System;
using Fusion;
using Jongreul.AuthorityRequest.Purchase;
using UnityEngine;

namespace Jongreul.AuthorityRequest.Networking
{
    /// <summary>네트워크로 복제되는 보유 목록 한 칸.</summary>
    public struct OwnedItem : INetworkStruct
    {
        public PlayerRef Player;
        public NetworkString<_32> ItemId;
    }

    /// <summary>
    /// Fusion 2 서버 권위 구매. 클라이언트는 (아이템, 요청 ID)만 RPC로 보낸다(가격 없음).
    /// 판정은 State Authority(Dedicated Server 또는 Host)만 하고, 같은 요청 ID는 이전 결과를 다시 보낸다.
    /// 잔고·보유 목록은 [Networked]로 복제되어 늦게 들어온 피어도 같은 값을 본다.
    /// 결과는 요청한 플레이어에게만 대상 RPC로 돌려준다.
    /// </summary>
    public sealed class PurchaseAuthority : NetworkBehaviour, IPlayerJoined, IPlayerLeft
    {
        public const int MaxPlayers = 16;
        public const int MaxOwnedItems = 128;

        [SerializeField] PriceTableAsset priceTable;
        [SerializeField] int startingBalance = 1000;
        [SerializeField] float replayTtlSeconds = 120f;

        PurchaseLedger _ledger;

        [Networked, Capacity(MaxPlayers)]
        public NetworkDictionary<PlayerRef, int> Balances { get; }

        [Networked, Capacity(MaxOwnedItems)]
        public NetworkLinkedList<OwnedItem> Owned { get; }

        /// <summary>서버에서만 존재. 테스트·디버그용.</summary>
        public PurchaseLedger Ledger => _ledger;

        /// <summary>이 피어에 도착한 구매 결과. 대상 RPC라 요청한 플레이어에게만 온다.</summary>
        public event Action<PurchaseResult> ResultReceived;

        public void Configure(PriceTableAsset table, int balance)
        {
            priceTable = table;
            startingBalance = balance;
        }

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            if (priceTable == null)
            {
                Debug.LogError($"[{nameof(PurchaseAuthority)}] 가격표가 비어 있다.", this);
                return;
            }

            _ledger = new PurchaseLedger(priceTable, new UnityClock(), replayTtlSeconds);
            // 이 오브젝트보다 먼저 들어온 플레이어는 PlayerJoined가 오지 않는다.
            foreach (PlayerRef player in Runner.ActivePlayers)
                RegisterPlayer(player);
        }

        public void PlayerJoined(PlayerRef player)
        {
            if (!HasStateAuthority || _ledger == null)
                return;
            RegisterPlayer(player);
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (!HasStateAuthority || _ledger == null)
                return;

            // PlayerRef는 재사용된다. 나간 플레이어의 잔고·보유 목록을 다음 사람이 물려받지 않게 지운다.
            _ledger.ForgetPlayer(player.RawEncoded);
            Balances.Remove(player);
            for (int i = Owned.Count - 1; i >= 0; i--)
            {
                if (Owned.Get(i).Player == player)
                    Owned.Remove(Owned.Get(i));
            }
        }

        /// <summary>클라이언트 → 서버. 가격은 싣지 않는다.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestPurchase(NetworkString<_32> itemId, int requestId, RpcInfo info = default)
        {
            if (!HasStateAuthority || _ledger == null)
                return;

            PlayerRef buyer = info.Source;
            if (!buyer.IsRealPlayer)
                return;

            PurchaseResult result = _ledger.Purchase(buyer.RawEncoded, itemId.ToString(), requestId);
            if (result.Succeeded && !result.Replayed)
            {
                Balances.Set(buyer, result.BalanceAfter);
                Owned.Add(new OwnedItem { Player = buyer, ItemId = itemId });
            }

            RPC_PurchaseResult(buyer, (int)result.Status, requestId, itemId, result.Price, result.BalanceAfter,
                result.Replayed);
        }

        /// <summary>서버 → 요청한 플레이어 한 명.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_PurchaseResult([RpcTarget] PlayerRef target, int status, int requestId,
            NetworkString<_32> itemId, int price, int balanceAfter, NetworkBool replayed)
        {
            var result = new PurchaseResult((PurchaseStatus)status, target.RawEncoded, requestId, itemId.ToString(),
                price, balanceAfter, replayed);
            ResultReceived?.Invoke(result);
        }

        public int GetBalance(PlayerRef player) => Balances.TryGet(player, out int balance) ? balance : 0;

        public bool Owns(PlayerRef player, string itemId)
        {
            foreach (OwnedItem item in Owned)
            {
                if (item.Player == player && item.ItemId.ToString() == itemId)
                    return true;
            }

            return false;
        }

        void RegisterPlayer(PlayerRef player)
        {
            if (!player.IsRealPlayer || Balances.ContainsKey(player))
                return;

            _ledger.SetBalance(player.RawEncoded, startingBalance);
            Balances.Set(player, startingBalance);
        }
    }
}
