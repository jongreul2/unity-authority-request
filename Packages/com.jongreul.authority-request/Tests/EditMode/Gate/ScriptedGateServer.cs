using System;
using System.Collections.Generic;
using Jongreul.AuthorityRequest.Gate;

namespace Jongreul.AuthorityRequest.Tests.Gate
{
    /// <summary>테스트가 응답 시점과 내용을 직접 정하는 서버 대역.</summary>
    sealed class ScriptedGateServer : IGateServer
    {
        public readonly List<GateRequest> Requests = new List<GateRequest>();

        /// <summary>설정하면 Send 안에서 즉시(동기) 응답한다.</summary>
        public Func<GateRequest, GateResponse?> AutoReply;

        public event Action<GateResponse> ResponseReceived;

        public GateRequest Last => Requests[Requests.Count - 1];

        public int SubscriberCount => ResponseReceived?.GetInvocationList().Length ?? 0;

        public void Send(GateRequest request)
        {
            Requests.Add(request);
            GateResponse? reply = AutoReply?.Invoke(request);
            if (reply.HasValue)
                ResponseReceived?.Invoke(reply.Value);
        }

        public void Respond(GateResponse response) => ResponseReceived?.Invoke(response);
    }
}
