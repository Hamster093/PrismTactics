/****************************************************
    文件：PerfBenchmarkSceneBuilder.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/30 16:00:00
    功能：从 M1_Battle 复制并裁剪出独立的 60 敌人性能基准场景 M1_Perf60。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 只生成基准场景，不改 M1_Battle 原场景。
    /// 生成后会去掉 Meta 流程（选关/选技能/结束面板），让场景一进 Play 就是战斗。
    /// </summary>
    public static class PerfBenchmarkSceneBuilder
    {
        private const string SourceScenePath = "Assets/_Project/Scenes/M1_Battle.unity";
        private const string TargetScenePath = "Assets/_Project/Scenes/M1_Perf60.unity";
        private const string PerfMapId = "M1_Perf60";
        private const string BossScenePath = "Assets/_Project/Scenes/M1_BossStress.unity";
        private const string BossMapId = "BossStress";

        private static readonly Vector2Int[] DefaultDeployCells =
        {
            new Vector2Int(5, 3),
            new Vector2Int(7, 1),
            new Vector2Int(3, 3),
            new Vector2Int(4, 1)
        };

        [MenuItem("Tools/PrismTactics/9 其他/9.1 场景生成/9.1.3 60 敌人基准场景")]
        public static void BuildFromMenu()
        {
            Build();
        }

        /// <summary>生成伤害数字压测场景：1 个高血量木桩 + 1 个 15 次/秒的速射干员。</summary>
        [MenuItem("Tools/PrismTactics/9 其他/9.1 场景生成/9.1.4 伤害数字压测场景")]
        public static void BuildBossStressFromMenu()
        {
            BuildInternal(BossScenePath, BossMapId, true);
        }

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.PerfBenchmarkSceneBuilder.BuildFromCli</summary>
        public static void BuildFromCli()
        {
            Build();
        }

        public static void Build()
        {
            BuildInternal(TargetScenePath, PerfMapId, false);
        }

        public static void BuildInternal(string targetScenePath, string mapId, bool damageNumberStress)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(targetScenePath) != null)
            {
                AssetDatabase.DeleteAsset(targetScenePath);
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, targetScenePath))
            {
                throw new System.InvalidOperationException(
                    $"复制场景失败：{SourceScenePath} -> {targetScenePath}");
            }

            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.OpenScene(targetScenePath, OpenSceneMode.Single);

            ConfigureBootstrap(mapId);
            RemoveMetaFlow();
            RemoveMetaPanels();
            PrepareHud();
            CreateRunner(damageNumberStress);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuildSettings(targetScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Perf] 已生成场景 {targetScenePath}，地图 {mapId}，并加入 Build Settings。");
        }

        private static void ConfigureBootstrap(string mapIdValue)
        {
            M1BattleBootstrap bootstrap = Object.FindObjectOfType<M1BattleBootstrap>();
            if (bootstrap == null)
            {
                throw new System.InvalidOperationException("M1_Battle 场景里找不到 M1BattleBootstrap。");
            }

            var serialized = new SerializedObject(bootstrap);
            SerializedProperty mapId = serialized.FindProperty("_mapId");
            SerializedProperty autoBuild = serialized.FindProperty("_autoBuild");
            mapId.stringValue = mapIdValue;
            autoBuild.boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RemoveMetaFlow()
        {
            // MetaFlowController 挂在 Canvas 上，只删组件，保留 Canvas/HUD。
            MetaFlowController[] controllers = Object.FindObjectsOfType<MetaFlowController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                Object.DestroyImmediate(controllers[i]);
            }

            BattleEndPanel[] endPanels = Object.FindObjectsOfType<BattleEndPanel>(true);
            for (int i = 0; i < endPanels.Length; i++)
            {
                Object.DestroyImmediate(endPanels[i].gameObject);
            }
        }

        private static void RemoveMetaPanels()
        {
            PanelBase[] panels = Object.FindObjectsOfType<PanelBase>(true);
            for (int i = 0; i < panels.Length; i++)
            {
                PanelBase panel = panels[i];
                if (panel is LevelSelectPanel
                    || panel is SkillSelectPanel
                    || panel is BackpackPanel
                    || panel is BattleDropPanel)
                {
                    Object.DestroyImmediate(panel.gameObject);
                }
            }
        }

        private static void PrepareHud()
        {
            Transform hud = FindByName("HUD");
            if (hud == null)
            {
                // 现在的 M1_Battle 是"空场景 + Bootstrap"，HUD 由 M1BattleBootstrap 运行时创建，
                // 场景里没有 HUD 属于正常情况，直接跳过。
                return;
            }

            hud.gameObject.SetActive(true);
            for (int i = 0; i < hud.childCount; i++)
            {
                hud.GetChild(i).gameObject.SetActive(true);
            }

            // 详情/操作面板默认关闭，由基准驱动器部署后打开。
            Transform detail = FindByName("OperatorDetailPanel");
            if (detail != null)
            {
                detail.gameObject.SetActive(false);
            }

            Transform action = FindByName("OperatorActionPanel");
            if (action != null)
            {
                action.gameObject.SetActive(false);
            }
        }

        private static void CreateRunner(bool damageNumberStress)
        {
            var runnerObject = new GameObject("Performance Benchmark");
            var runner = runnerObject.AddComponent<PerformanceBenchmarkRunner>();

            var serialized = new SerializedObject(runner);
            serialized.FindProperty("_label").stringValue = damageNumberStress ? "boss_stress" : "baseline";
            serialized.FindProperty("_sampleSeconds").floatValue = damageNumberStress ? 15f : 20f;
            serialized.FindProperty("_autoRun").boolValue = true;
            serialized.FindProperty("_autoActivateSkills").boolValue = !damageNumberStress;
            serialized.FindProperty("_targetFrameRate").intValue = 60;
            serialized.FindProperty("_selectedOperatorIndex").intValue = 0;
            // 压测固定前 4 个 AOT 干员：第 5 个（黑骑士）依赖热更技能，会把 GC 波动带进对比
            serialized.FindProperty("_operatorLimit").intValue = 4;
            serialized.FindProperty("_damageNumberStress").boolValue = damageNumberStress;
            serialized.FindProperty("_stressOperatorCount").intValue = 6;

            SerializedProperty cells = serialized.FindProperty("_deployCells");
            cells.arraySize = DefaultDeployCells.Length;
            for (int i = 0; i < DefaultDeployCells.Length; i++)
            {
                SerializedProperty element = cells.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("x").intValue = DefaultDeployCells[i].x;
                element.FindPropertyRelative("y").intValue = DefaultDeployCells[i].y;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddToBuildSettings(string targetScenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == targetScenePath)
                {
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(targetScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Transform FindByName(string name)
        {
            Transform[] transforms = Object.FindObjectsOfType<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == name)
                {
                    return transforms[i];
                }
            }

            return null;
        }
    }
}
