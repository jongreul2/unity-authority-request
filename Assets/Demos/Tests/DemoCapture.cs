using System.Collections;
using System.IO;
using UnityEngine;

namespace Jongreul.AuthorityRequest.Demos.Tests
{
    /// <summary>
    /// 데모 화면을 PNG 프레임으로 저장한다(README GIF용). 카메라를 렌더 텍스처로 돌려
    /// 배치 모드(-nographics 없이)에서도 화면 없이 캡처된다.
    /// </summary>
    public static class DemoCapture
    {
        public const int Width = 1280;
        public const int Height = 720;

        public static string FramesDirectory(string name)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "artifacts", "frames", name);
        }

        /// <summary>seconds 동안 fps 간격으로 프레임을 저장한다.</summary>
        public static IEnumerator Record(Camera camera, string name, float seconds, float fps)
        {
            string directory = FramesDirectory(name);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
            Directory.CreateDirectory(directory);

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = target;

            float interval = 1f / fps;
            int count = Mathf.CeilToInt(seconds * fps);
            float next = Time.realtimeSinceStartup;

            for (int frame = 0; frame < count; frame++)
            {
                while (Time.realtimeSinceStartup < next)
                    yield return null;
                next += interval;

                camera.Render();
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply(false);
                RenderTexture.active = previousActive;

                File.WriteAllBytes(Path.Combine(directory, $"{frame:D4}.png"), pixels.EncodeToPNG());
            }

            camera.targetTexture = previousTarget;
            Object.Destroy(target);
            Object.Destroy(pixels);
            Debug.Log($"[DemoCapture] {count} frames → {directory}");
        }
    }
}
