using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Jongreul.AuthorityRequest.Demos.Tests
{
    /// <summary>
    /// README GIF 촬영 스크립트. 일반 테스트 실행에서는 돌지 않는다([Explicit]).
    /// 실행: Unity -batchmode -runTests -testPlatform PlayMode -testCategory Capture (-nographics 없이)
    /// </summary>
    [Explicit, Category("Capture")]
    public class GateDemoCapture
    {
        [UnityTest]
        public IEnumerator RecordGateDemo()
        {
            var cameraGo = new GameObject("Main Camera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            new GameObject("EventSystem", typeof(EventSystem));

            var demo = new GameObject("GateDemo").AddComponent<GateDemo>();
            demo.SetLatency(350);

            demo.StartCoroutine(Script(demo));
            yield return DemoCapture.Record(camera, "gate", seconds: 11f, fps: 12f);
        }

        static IEnumerator Script(GateDemo demo)
        {
            yield return new WaitForSecondsRealtime(0.8f);
            yield return demo.Mash(0, count: 10, interval: 0.05f);   // 연타 10회 → 요청 1회
            yield return new WaitForSecondsRealtime(1.2f);
            yield return demo.Mash(2, count: 6, interval: 0.08f);
            yield return new WaitForSecondsRealtime(1.5f);
            demo.SetBypassGate(true);                                   // 게이트 없이: 매 탭이 요청
            yield return new WaitForSecondsRealtime(0.4f);
            yield return demo.Mash(1, count: 8, interval: 0.06f);
        }
    }
}
