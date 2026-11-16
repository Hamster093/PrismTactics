/****************************************************
    文件：M0SceneBuilder.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：在 Unity 编辑器中生成 M0 战斗演示场景。
*****************************************************/

using System.IO;
using PrismTactics.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    public static class M0SceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/M0_Battle.unity";

        [MenuItem("Tools/PrismTactics/9 其他/9.1 场景生成/9.1.2 M0 战斗场景")]
        public static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(0f, 10f, -12f);
            camera.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject bootstrapGo = new GameObject("M0 Battle Bootstrap");
            bootstrapGo.AddComponent<M0BattleBootstrap>();

            string absoluteDirectory = Path.Combine(Application.dataPath, "_Project/Scenes");
            Directory.CreateDirectory(absoluteDirectory);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            Selection.activeGameObject = bootstrapGo;
            Debug.Log("[M0] 已生成场景：" + ScenePath);
        }
    }
}
