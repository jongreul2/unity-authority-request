using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Jongreul.AuthorityRequest.Grab;
using Jongreul.AuthorityRequest.Networking;
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
    /// Fusion 2 Single 모드에서 그랩 흐름을 실제 RPC·[Networked]·서버 물리로 검증한다.
    /// 잡기 → 양손 한도로 두 번째 거부 → 놓기 → 서버 물리가 바닥에서 멈춤 → 정지 자세 확정.
    /// </summary>
    public class GrabSingleModeTests
    {
        const string PrefabPath = "Assets/Demos/Fusion/AuthorityRequestNetwork.prefab";
        const float TimeoutSeconds = 10f;

        readonly List<GameObject> _created = new List<GameObject>();
        NetworkRunner _runner;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runner != null)
            {
                Task shutdown = _runner.Shutdown();
                while (!shutdown.IsCompleted)
                    yield return null;
                if (_runner != null)
                    Object.Destroy(_runner.gameObject);
            }

            foreach (GameObject go in _created)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            _created.Clear();
        }

        GrabbableView CreateCube(int id, Vector3 position)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Cube {id}";
            cube.transform.position = position;
            cube.transform.localScale = Vector3.one * 0.3f;
            cube.AddComponent<Rigidbody>();
            var view = cube.AddComponent<GrabbableView>();
            view.SetId(id);
            _created.Add(cube);
            return view;
        }

        [UnityTest]
        public IEnumerator GrabRejectSecond_ReleaseSettlesAtServerRestPose()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _created.Add(floor);
            GrabbableView first = CreateCube(1, new Vector3(0, 1, 0));
            GrabbableView second = CreateCube(2, new Vector3(1, 1, 0));

            var runnerGo = new GameObject("Runner");
            _runner = runnerGo.AddComponent<NetworkRunner>();
            Task<StartGameResult> start = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                SessionName = "grab-test",
            });
            while (!start.IsCompleted)
                yield return null;
            Assert.That(start.Result.Ok, Is.True, start.Result.ToString());

#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PrefabPath);
#else
            NetworkObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var authority = _runner.Spawn(prefab).GetComponent<GrabAuthority>();
            yield return null;
            Assert.That(authority.Views.Count, Is.EqualTo(2), "씬의 GrabbableView를 찾아야 한다");

            var results = new List<GrabResult>();
            authority.GrabResultReceived += results.Add;

            // 1. 잡기
            first.RequestGrab(Hand.Right);
            yield return WaitFor("첫 그랩 결과", () => results.Count >= 1 && first.IsLocallyHeld,
                () => $"results={results.Count} {Describe(authority, first)}");
            Assert.That(results[0].Status, Is.EqualTo(GrabStatus.Accepted));

            // 2. 기본 정책(양손 합쳐 1개, 초과 거부): 두 번째는 거부
            second.RequestGrab(Hand.Left);
            yield return WaitFor("두 번째 그랩 결과", () => results.Count >= 2, () => $"results={results.Count}");
            Assert.That(results[1].Status, Is.EqualTo(GrabStatus.LimitReached));
            Assert.That(authority.Arbiter.GetOwner(second.Id), Is.EqualTo(GrabIds.None));

            // 3. 손으로 옮긴 뒤 놓는다. 서버 물리가 바닥까지 떨어뜨리고 멈춤을 판정해야 한다.
            first.SetHandPose(new Vector3(0, 1.5f, 0.5f), Quaternion.Euler(0, 45, 0));
            yield return null;
            first.Release(Vector3.zero);

            GrabbableSnapshot snapshot = default;
            yield return WaitFor("서버 정지 확정", () =>
                authority.Arbiter.TryGetSnapshot(first.Id, out snapshot) &&
                snapshot.State == GrabbableState.Resting, () => Describe(authority, first));

            Assert.That(snapshot.Pose.Py, Is.LessThan(0.5f), "바닥 근처에서 멈춰야 한다");
            yield return null; // Render 한 번

            Assert.That(first.Replica.State, Is.EqualTo(GrabbableState.Resting));
            // 네트워크 복제 구조체를 거치므로 부동소수 양자화 여지를 1 mm로 둔다.
            Assert.That(first.Replica.Pose.DistanceTo(snapshot.Pose), Is.LessThan(0.001f), "표시 자세 = 서버 정지 자세");
            Assert.That(first.transform.position.y, Is.EqualTo(snapshot.Pose.Py).Within(0.001f));
            Assert.That(first.Replica.IsLocallyHeld, Is.False);

            // 4. 놓은 뒤에는 두 번째를 잡을 수 있다.
            second.RequestGrab(Hand.Left);
            yield return WaitFor("놓은 뒤 두 번째 그랩", () => results.Count >= 3, () => $"results={results.Count}");
            Assert.That(results[2].Status, Is.EqualTo(GrabStatus.Accepted));
        }

        /// <summary>실시간 기준으로 기다린다. 배치 모드는 프레임이 매우 빨라 프레임 수로 재면 Fusion 틱·물리가 따라오지 못한다.</summary>
        static IEnumerator WaitFor(string step, System.Func<bool> condition, System.Func<string> describe = null)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, $"시간 초과: {step} {describe?.Invoke()}");
        }

        string Describe(GrabAuthority authority, GrabbableView view)
        {
            string server = authority.Arbiter.TryGetSnapshot(view.Id, out GrabbableSnapshot s) ? s.ToString() : "없음";
            return $"| 서버 {server} | 뷰 위치 {view.transform.position} 속도 {view.Speed:0.###} " +
                   $"| 레플리카 {view.Replica.State} owner={view.Replica.OwnerId} gen={view.Replica.Generation}";
        }
    }
}
