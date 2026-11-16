/****************************************************
    文件：SpineCharacterPreview.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 14:35:00
	功能：把 SpineDemo 场景离线渲染成 PNG，用于不进入播放模式也能检查渲染结果。
*****************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using PrismTactics.Battle;
using Spine.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrismTactics.EditorTools
{
    public static class SpineCharacterPreview
    {
        private const string DemoScenePath = "Assets/_Project/Scenes/SpineDemo.unity";
        private const string OutputDirectory = "Library/SpinePreview";
        private const string RequestFile = "Library/SpinePreview.request";
        private const string ResultFile = "Library/SpinePreview.result.txt";

        private const int Width = 1600;
        private const int Height = 900;

        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.8 渲染 Spine 演示截图")]
        public static void RenderFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Spine] 请先退出播放模式再渲染截图。");
                return;
            }

            RenderAndReport();
        }

        // 请求文件随时可能出现（不一定会伴随一次域重载），所以这里除了域重载时试一次，
        // 还在 update 里低频轮询，保证任何时刻丢进去的请求都会被处理。
        private const int PollIntervalFrames = 120;

        [InitializeOnLoadMethod]
        private static void RenderOnRequest()
        {
            EditorApplication.delayCall += TryRenderFromRequest;
            EditorApplication.update -= PollRenderRequest;
            EditorApplication.update += PollRenderRequest;
        }

        private static void PollRenderRequest()
        {
            if (++pollFrameCounter < PollIntervalFrames)
            {
                return;
            }

            pollFrameCounter = 0;
            TryRenderFromRequest();
        }

        private static int pollFrameCounter;

        private static void TryRenderFromRequest()
        {
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string request = Path.Combine(Directory.GetCurrentDirectory(), RequestFile);
            if (!File.Exists(request))
            {
                return;
            }

            try
            {
                File.Delete(request);
            }
            catch (Exception)
            {
                // 文件被占用，下一轮轮询再试
                return;
            }

            RenderAndReport();
        }

        private static void RenderAndReport()
        {
            var report = new System.Text.StringBuilder();
            int exitCode = 0;

            try
            {
                Render(report);
            }
            catch (Exception exception)
            {
                exitCode = 1;
                report.AppendLine("EXCEPTION: " + exception);
                Debug.LogError("[Spine] 渲染失败：" + exception);
            }

            report.AppendLine("EXIT=" + exitCode);

            try
            {
                File.WriteAllText(
                    Path.Combine(Directory.GetCurrentDirectory(), ResultFile),
                    report.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Spine] 写结果文件失败：" + exception.Message);
            }
        }

        private static void CapturePng(
            Camera camera,
            RenderTexture renderTexture,
            string outputDirectory,
            string fileName)
        {
            camera.Render();

            RenderTexture.active = renderTexture;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();

            string filePath = Path.Combine(outputDirectory, fileName);
            File.WriteAllBytes(filePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void Render(System.Text.StringBuilder report)
        {
            string outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), OutputDirectory);
            Directory.CreateDirectory(outputDirectory);

            Scene scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive);
            RenderTexture renderTexture = null;

            try
            {
                Camera camera = null;
                var skeletons = new List<SkeletonAnimation>();

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (camera == null)
                    {
                        camera = root.GetComponentInChildren<Camera>(true);
                    }

                    skeletons.AddRange(root.GetComponentsInChildren<SkeletonAnimation>(true));
                }

                report.AppendLine("CAMERA=" + (camera != null));
                report.AppendLine("SKELETONS=" + skeletons.Count);

                if (camera == null)
                {
                    throw new InvalidOperationException("演示场景里没有相机");
                }

                // 编辑模式下 ExecuteAlways 不一定马上刷新，这里手动推进一次动画并重建网格。
                for (int i = 0; i < skeletons.Count; i++)
                {
                    SkeletonAnimation skeleton = skeletons[i];
                    if (skeleton == null)
                    {
                        continue;
                    }

                    SpineUnitView view = skeleton.GetComponent<SpineUnitView>();
                    string idleName = view != null ? view.IdleAnimation : "Idle";

                    skeleton.Initialize(true, true);
                    skeleton.state.SetAnimation(0, idleName, true);
                    skeleton.Update(0.35f);
                    skeleton.LateUpdate();

                    Spine.Animation attack = skeleton.skeleton.Data.FindAnimation("Attack");
                    report.AppendLine(
                        "SKELETON=" + skeleton.gameObject.name
                        + " anim=" + skeleton.AnimationName
                        + " valid=" + skeleton.valid
                        + " attackDuration=" + (attack != null ? attack.Duration.ToString("F2") : "MISSING"));
                }

                renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                renderTexture.Create();
                camera.targetTexture = renderTexture;

                CapturePng(camera, renderTexture, outputDirectory, "demo_idle.png");

                // 第二张：让每个角色都停在攻击动画的 45% 处，检查攻击姿势是否正常。
                for (int i = 0; i < skeletons.Count; i++)
                {
                    SkeletonAnimation skeleton = skeletons[i];
                    if (skeleton == null)
                    {
                        continue;
                    }

                    Spine.Animation attack = skeleton.skeleton.Data.FindAnimation("Attack");
                    if (attack == null)
                    {
                        continue;
                    }

                    skeleton.state.SetAnimation(0, attack, false);
                    skeleton.Update(attack.Duration * 0.45f);
                    skeleton.LateUpdate();
                }

                CapturePng(camera, renderTexture, outputDirectory, "demo_attack.png");

                Debug.Log("[Spine] 已渲染演示截图：" + outputDirectory);
            }
            finally
            {
                if (renderTexture != null)
                {
                    RenderTexture.active = null;
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }

                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
