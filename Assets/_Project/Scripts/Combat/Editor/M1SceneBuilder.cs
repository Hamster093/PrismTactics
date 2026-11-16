/****************************************************
    文件：M1SceneBuilder.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：在 Unity 编辑器中生成 M1/M3 战斗演示场景。
*****************************************************/

using System.IO;
using PrismTactics.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    public static class M1SceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/M1_Battle.unity";
        private const string M0ScenePath = "Assets/_Project/Scenes/M0_Battle.unity";

        [MenuItem("Tools/PrismTactics/9 其他/9.1 场景生成/9.1.1 M1 战斗场景")]
        public static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            camera.orthographic = false;
            camera.fieldOfView = 60f;
            camera.transform.position = new Vector3(5.9f, 7.23f, -1.97f);
            camera.transform.rotation = Quaternion.Euler(66.95f, -3.29f, -3.13f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject bootstrapGo = new GameObject("M1 Battle Bootstrap");
            bootstrapGo.AddComponent<M1BattleBootstrap>();

            string absoluteDirectory = Path.Combine(Application.dataPath, "_Project/Scenes");
            Directory.CreateDirectory(absoluteDirectory);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(M0ScenePath, true)
            };
            EditorBuildSettings.scenes = scenes.ToArray();

            Selection.activeGameObject = bootstrapGo;
            Debug.Log("[M1] 已生成场景：" + ScenePath);
        }
    }
}
