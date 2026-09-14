namespace Jongreul.AuthorityRequest.Gate
{
    /// <summary>응답 게이트 상태. Pending은 서버 응답(또는 타임아웃)으로만 끝난다.</summary>
    public enum GateState
    {
        Ready,
        Pending,
        Cooldown,
    }

    /// <summary>입력 한 번에 대한 게이트의 처리 결과.</summary>
    public enum GateInputResult
    {
        Sent,
        RejectedPending,
        RejectedCooldown,
    }

    /// <summary>상태 전이 원인.</summary>
    public enum GateTransitionReason
    {
        RequestSent,
        ServerSuccess,
        ServerFail,
        CooldownElapsed,
        PendingTimeout,
    }

    /// <summary>게이트가 응답을 적용하지 않고 버린 이유.</summary>
    public enum GateIgnoreReason
    {
        /// <summary>이미 적용한 응답과 같은 시퀀스.</summary>
        Duplicate,
        /// <summary>현재 대기 중인 요청보다 오래된 요청의 응답(타임아웃 뒤 늦게 도착, 순서 역전).</summary>
        Stale,
        /// <summary>보낸 적 없는 시퀀스.</summary>
        Unknown,
    }

    /// <summary>클라이언트 → 서버 요청. 쿨타임 같은 판정 값은 싣지 않는다.</summary>
    public readonly struct GateRequest
    {
        public readonly int ActionId;
        public readonly long Sequence;

        public GateRequest(int actionId, long sequence)
        {
            ActionId = actionId;
            Sequence = sequence;
        }

        public override string ToString() => $"Request(action={ActionId}, seq={Sequence})";
    }

    /// <summary>서버 → 클라이언트 응답. 쿨타임 길이는 서버가 정한다.</summary>
    public readonly struct GateResponse
    {
        public readonly int ActionId;
        public readonly long Sequence;
        public readonly bool Success;
        public readonly double CooldownSeconds;
        public readonly string FailReason;

        GateResponse(int actionId, long sequence, bool success, double cooldownSeconds, string failReason)
        {
            ActionId = actionId;
            Sequence = sequence;
            Success = success;
            CooldownSeconds = cooldownSeconds;
            FailReason = failReason;
        }

        public static GateResponse Succeeded(int actionId, long sequence, double cooldownSeconds) =>
            new GateResponse(actionId, sequence, true, cooldownSeconds, null);

        public static GateResponse Failed(int actionId, long sequence, string reason) =>
            new GateResponse(actionId, sequence, false, 0, reason);

        public override string ToString() => Success
            ? $"Success(action={ActionId}, seq={Sequence}, cooldown={CooldownSeconds:0.###}s)"
            : $"Fail(action={ActionId}, seq={Sequence}, reason={FailReason})";
    }

    /// <summary>상태 전이 기록.</summary>
    public readonly struct GateTransition
    {
        public readonly GateState From;
        public readonly GateState To;
        public readonly GateTransitionReason Reason;
        public readonly long Sequence;

        public GateTransition(GateState from, GateState to, GateTransitionReason reason, long sequence)
        {
            From = from;
            To = to;
            Reason = reason;
            Sequence = sequence;
        }

        public override string ToString() => $"{From} -> {To} ({Reason}, seq={Sequence})";
    }
}
