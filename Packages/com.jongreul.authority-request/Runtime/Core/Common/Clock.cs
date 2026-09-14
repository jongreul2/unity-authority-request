using System;

namespace Jongreul.AuthorityRequest
{
    /// <summary>시간(초)을 주는 시계. 로직이 UnityEngine.Time에 묶이지 않도록 주입한다.</summary>
    public interface IClock
    {
        double Now { get; }
    }

    /// <summary>손으로 돌리는 시계. 테스트와 시뮬레이션용.</summary>
    public sealed class ManualClock : IClock
    {
        public double Now { get; private set; }

        public ManualClock(double start = 0)
        {
            Now = start;
        }

        public void Advance(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds), "시계는 뒤로 갈 수 없다.");
            Now += seconds;
        }
    }
}
