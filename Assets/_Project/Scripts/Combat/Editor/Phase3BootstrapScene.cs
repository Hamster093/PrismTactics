/****************************************************
    文件：Phase3BootstrapScene.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/1 15:15:00
    功能：生成/打开 Boot 场景，并把 Bootstrap 放到 Build Settings 第一个。
*****************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using PrismTactics.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// Boot 场景内容很少：一个相机 + 一个 BootstrapController，进度和错误都用 OnGUI 画。
    /// </summary>
    public static class Phase3BootstrapScene
    {
        private const string ScenePath = "Assets/_Project/Scenes/Bootstrap.unity";

        [MenuItem("Tools/PrismTactics/3 运行/3.2 生成 Bootstrap 场景", false, 25)]
        public static void GenerateFromMenu()
        {
            Generate();
            EditorUtility.DisplayDialog("M3.1", "Bootstrap 场景已生成，并设为 Build Settings 第一个。", "好");
        }

        [MenuItem("Tools/PrismTactics/3 运行/3.1 打开 Bootstrap 场景", false, 26)]
        public static void OpenFromMenu()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                Generate();
            }

            EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.Phase3BootstrapScene.GenerateFromCli</summary>
        public static void GenerateFromCli()
        {
            int exitCode = 0;
            try
            {
                Generate();
                Debug.Log("[M3.1] Bootstrap 场景已生成：" + ScenePath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[M3.1] 生成 Bootstrap 场景失败：" + exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static void Generate()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<AudioListener>();

            var bootstrapObject = new GameObject("Bootstrap");
            bootstrapObject.AddComponent<BootstrapController>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureFirstBuildScene();
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFirstBuildScene()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(scene => scene.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
