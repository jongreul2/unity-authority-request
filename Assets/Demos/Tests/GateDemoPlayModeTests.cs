using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Jongreul.AuthorityRequest.Demos.Tests
{
    /// <summary>데모 흐름 검증(최소). 실제 프레임 루프 위에서 게이트가 연타를 한 번의 요청으로 줄이는지 본다.</summary>
    public class GateDemoPlayModeTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.Destroy(_root);
        }

        GateDemo CreateDemo()
        {
            _root = new GameObject("GateDemo");
            return _root.AddComponent<GateDemo>();
        }

        [UnityTest]
        public IEnumerator MashingTenTimes_SendsOneRequest_ServerExecutesOnce()
        {
            GateDemo demo = CreateDemo();
            demo.SetLatency(100);

            yield return demo.Mash(0, count: 10, interval: 0.01f);
            yield return new WaitForSecondsRealtime(0.3f);

            Assert.That(demo.Server.RequestsReceived, Is.EqualTo(1));
            Assert.That(demo.Server.ActionsExecuted, Is.EqualTo(1));
            Assert.That(demo.Gates[0].Stats.InputsRejected, Is.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator BypassingGate_SendsEveryTap_ServerStillExecutesOnce()
        {
            GateDemo demo = CreateDemo();
            demo.SetLatency(100);
            demo.SetBypassGate(true);

            yield return demo.Mash(0, count: 10, interval: 0.01f);
            yield return new WaitForSecondsRealtime(0.3f);

            Assert.That(demo.Server.RequestsReceived, Is.EqualTo(10));
            Assert.That(demo.Server.ActionsExecuted, Is.EqualTo(1));
        }
    }
}
