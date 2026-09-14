using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Jongreul.AuthorityRequest.Demos.Editor
{
    /// <summary>
    /// 데모 씬을 코드로 다시 만든다. 씬에는 카메라·이벤트 시스템·부트스트랩 컴포넌트만 둔다.
    /// 배치 실행: -executeMethod Jongreul.AuthorityRequest.Demos.Editor.DemoSceneBuilder.BuildAll
    /// </summary>
    public static class DemoSceneBuilder
    {
        static readonly (string Path, Type Bootstrap)[] Scenes =
        {
            ("Assets/Demos/Gate/GateDemo.unity", typeof(GateDemo)),
        };

        [MenuItem("Tools/Authority Request/Rebuild Demo Scenes")]
        public static void BuildAll()
        {
            var buildScenes = new List<EditorBuildSettingsScene>();
            foreach ((string path, Type bootstrap) in Scenes)
            {
                Build(path, bootstrap);
                buildScenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[DemoSceneBuilder] {Scenes.Length} scene(s) rebuilt");
        }

        static void Build(string path, Type bootstrap)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = DemoUi.Background;
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            new GameObject(bootstrap.Name, bootstrap);

            EditorSceneManager.SaveScene(scene, path);
        }
    }
}
