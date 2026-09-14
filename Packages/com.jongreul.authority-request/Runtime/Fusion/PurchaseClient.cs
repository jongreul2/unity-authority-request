using System;
using Fusion;
using Jongreul.AuthorityRequest.Purchase;
using UnityEngine;

namespace Jongreul.AuthorityRequest.Networking
{
    /// <summary>
    /// 클라이언트 쪽 구매 호출. 요청 ID는 세션마다 무작위 시작값에서 1씩 올린다
    /// (PlayerRef가 재사용돼도 이전 사람의 요청 ID와 겹치지 않게).
    /// </summary>
    public sealed class PurchaseClient : MonoBehaviour
    {
        [SerializeField] PurchaseAuthority authority;

        int _nextRequestId;

        public PurchaseAuthority Authority => authority;
        public int LastRequestId { get; private set; }
        public PurchaseResult? LastResult { get; private set; }

        public event Action<PurchaseResult> ResultReceived;

        void Awake()
        {
            _nextRequestId = UnityEngine.Random.Range(1, int.MaxValue / 2);
        }

        void OnEnable()
        {
            if (authority != null)
                authority.ResultReceived += OnResult;
        }

        void OnDisable()
        {
            if (authority != null)
                authority.ResultReceived -= OnResult;
        }

        public void Bind(PurchaseAuthority target)
        {
            if (authority != null)
                authority.ResultReceived -= OnResult;
            authority = target;
            if (authority != null && isActiveAndEnabled)
                authority.ResultReceived += OnResult;
        }

        /// <summary>새 요청 ID로 구매 요청. 요청 ID를 돌려준다.</summary>
        public int Purchase(string itemId)
        {
            int requestId = _nextRequestId++;
            LastRequestId = requestId;
            Send(itemId, requestId);
            return requestId;
        }

        /// <summary>같은 요청 ID로 다시 보냄(응답 유실 뒤 재시도 흉내). 서버는 한 번만 차감한다.</summary>
        public void Resend(string itemId, int requestId) => Send(itemId, requestId);

        void Send(string itemId, int requestId)
        {
            if (authority == null || authority.Object == null)
            {
                Debug.LogWarning($"[{nameof(PurchaseClient)}] 권위 오브젝트가 아직 없다.", this);
                return;
            }

            authority.RPC_RequestPurchase(new NetworkString<_32>(itemId), requestId);
        }

        void OnResult(PurchaseResult result)
        {
            LastResult = result;
            ResultReceived?.Invoke(result);
        }
    }
}
