using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Jongreul.AuthorityRequest.Networking;
using Jongreul.AuthorityRequest.Purchase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Jongreul.AuthorityRequest.Demos.Fusion.Tests
{
    /// <summary>
    /// Fusion 2 Single 모드(App ID 불필요)에서 실제 RPC·[Networked] 경로로 구매 흐름을 검증한다.
    /// 로컬 피어가 State Authority이므로 Host의 자기 요청 경로와 같다.
    /// </summary>
    public class PurchaseSingleModeTests
    {
        const string PrefabPath = "Assets/Demos/Fusion/AuthorityRequestNetwork.prefab";
        const int TimeoutFrames = 300;

        NetworkRunner _runner;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runner == null)
                yield break;

            Task shutdown = _runner.Shutdown();
            while (!shutdown.IsCompleted)
                yield return null;
            if (_runner != null)
                Object.Destroy(_runner.gameObject);
        }

        [UnityTest]
        public IEnumerator SameRequestIdTwice_ChargesOnce_AndReplaysResult()
        {
            var go = new GameObject("Runner");
            _runner = go.AddComponent<NetworkRunner>();
            Task<StartGameResult> start = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                SessionName = "purchase-test",
            });
            while (!start.IsCompleted)
                yield return null;
            Assert.That(start.Result.Ok, Is.True, start.Result.ToString());

#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PrefabPath);
#else
            NetworkObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null, $"{PrefabPath} 없음 — FusionDemoAssetBuilder.BuildAll 먼저 실행");

            NetworkObject spawned = _runner.Spawn(prefab);
            var authority = spawned.GetComponent<PurchaseAuthority>();
            yield return null;

            PlayerRef me = _runner.LocalPlayer;
            Assert.That(authority.GetBalance(me), Is.EqualTo(1000));

            var client = go.AddComponent<PurchaseClient>();
            client.Bind(authority);
            var results = new List<PurchaseResult>();
            client.ResultReceived += results.Add;

            int requestId = client.Purchase("hat");
            yield return WaitFor(() => results.Count >= 1);

            Assert.That(results[0].Status, Is.EqualTo(PurchaseStatus.Success));
            Assert.That(results[0].Replayed, Is.False);
            Assert.That(authority.GetBalance(me), Is.EqualTo(700));
            Assert.That(authority.Owns(me, "hat"), Is.True);

            // 응답이 유실됐다고 보고 같은 요청 ID로 다시 보낸다.
            client.Resend("hat", requestId);
            yield return WaitFor(() => results.Count >= 2);

            Assert.That(results[1].Status, Is.EqualTo(PurchaseStatus.Success));
            Assert.That(results[1].Replayed, Is.True);
            Assert.That(authority.GetBalance(me), Is.EqualTo(700));
            Assert.That(authority.Ledger.Executed, Is.EqualTo(1));
        }

        static IEnumerator WaitFor(System.Func<bool> condition)
        {
            for (int i = 0; i < TimeoutFrames && !condition(); i++)
                yield return null;
            Assert.That(condition(), Is.True, "시간 초과");
        }
    }
}
