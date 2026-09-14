using System;

namespace Jongreul.AuthorityRequest.Gate
{
    /// <summary>
    /// 게이트가 요청을 보내고 응답을 받는 전송 계층.
    /// 응답은 지연·중복·순서 역전된 채로 올 수 있다고 가정한다.
    /// </summary>
    public interface IGateServer
    {
        event Action<GateResponse> ResponseReceived;

        void Send(GateRequest request);
    }
}
