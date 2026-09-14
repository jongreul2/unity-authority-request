namespace Jongreul.AuthorityRequest.Gate
{
    /// <summary>게이트 누적 통계. 데모 표시와 테스트 검증에 쓴다.</summary>
    public sealed class GateStats
    {
        public int InputsReceived { get; internal set; }
        public int RequestsSent { get; internal set; }
        public int InputsRejected { get; internal set; }
        public int Successes { get; internal set; }
        public int Failures { get; internal set; }
        public int Timeouts { get; internal set; }
        public int DuplicatesIgnored { get; internal set; }
        public int StaleIgnored { get; internal set; }
        public int UnknownIgnored { get; internal set; }

        public int ResponsesIgnored => DuplicatesIgnored + StaleIgnored + UnknownIgnored;
    }
}
