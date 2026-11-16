/****************************************************
    文件：PerfCaptureHelper.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/31 11:45:00
    功能：性能截图辅助菜单：打开基准场景、切换优化前/后的 HUD 对照状态。
*****************************************************/

using PrismTactics.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 只服务于“手动拍 Profiler 前后对比截图”，不参与运行时逻辑。
    /// </summary>
    public static class PerfCaptureHelper
    {
        private const string ScenePath = "Assets/_Project/Scenes/M1_Perf60.unity";

        [MenuItem("Tools/PrismTactics/9 其他/9.2 性能/9.2.1 打开基准场景")]
        public static void OpenBenchmarkScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Selection.activeObject = FindRunner()?.gameObject;

            Debug.Log("[性能截图] 已打开 M1_Perf60。下一步：Window > Analysis > Profiler，" +
                      "选择 Play Mode，关闭 Deep Profile，然后点 Play。");
        }

        [MenuItem("Tools/PrismTactics/9 其他/9.2 性能/9.2.2 切到优化后")]
        public static void UseOptimizedHud()
        {
            SetLegacyPolling(false);
        }

        [MenuItem("Tools/PrismTactics/9 其他/9.2 性能/9.2.3 切到优化前")]
        public static void UseLegacyHud()
        {
            SetLegacyPolling(true);
        }

        private static void SetLegacyPolling(bool legacy)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[性能截图] 请先停止 Play，再切换前后对照状态。");
                return;
            }

            PerformanceBenchmarkRunner runner = FindRunner();
            if (runner == null)
            {
                Debug.LogWarning("[性能截图] 当前场景没有 PerformanceBenchmarkRunner，请先执行“1. 打开基准场景”。");
                return;
            }

            var serialized = new SerializedObject(runner);
            serialized.FindProperty("_legacyHudPolling").boolValue = legacy;
            serialized.FindProperty("_showOverlay").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Scene scene = runner.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);

            string label = legacy ? "优化前（旧版每帧轮询）" : "优化后（事件驱动）";
            Debug.Log($"[性能截图] 已切换到 {label}。直接点 Play 即可；截完图后如果要提交场景，记得保存。");
        }

        private static PerformanceBenchmarkRunner FindRunner()
        {
            PerformanceBenchmarkRunner[] runners =
                Object.FindObjectsOfType<PerformanceBenchmarkRunner>(true);
            return runners.Length > 0 ? runners[0] : null;
        }
    }
}
