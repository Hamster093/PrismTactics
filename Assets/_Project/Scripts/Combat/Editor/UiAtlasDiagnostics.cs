/****************************************************
    文件：UiAtlasDiagnostics.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/10 13:40:00
	功能：编辑器内开关 UI 图集，用于 A/B 对比，避免移动资源文件导致 meta 损坏。
*****************************************************/

using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 图集 A/B 开关：写入 EditorPrefs，运行时由 UiAtlasService 读取。
    /// 好处是比对前后不需要把 .spriteatlas 移出工程（移动会触发重新导入、破坏 meta）。
    /// </summary>
    public static class UiAtlasDiagnostics
    {
        public const string DisabledKey = "PrismTactics.UiAtlasDisabled";

        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.3 禁用 UI 图集（编辑器内）", false, 40)]
        private static void ToggleAtlas()
        {
            bool disabled = EditorPrefs.GetBool(DisabledKey, false);
            EditorPrefs.SetBool(DisabledKey, !disabled);
            Debug.Log("[UIAtlas] 编辑器内图集已" + (!disabled ? "禁用" : "启用") + "，下次 Play 生效。");
        }

        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.3 禁用 UI 图集（编辑器内）", true)]
        private static bool ToggleAtlasValidate()
        {
            Menu.SetChecked("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.3 禁用 UI 图集（编辑器内）",
                EditorPrefs.GetBool(DisabledKey, false));
            return true;
        }
    }
}
