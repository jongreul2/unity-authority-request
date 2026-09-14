using UnityEngine;

namespace Jongreul.AuthorityRequest
{
    /// <summary>Time.timeScale의 영향을 받지 않는 실시간 시계.</summary>
    public sealed class UnityClock : IClock
    {
        public double Now => Time.unscaledTimeAsDouble;
    }
}
