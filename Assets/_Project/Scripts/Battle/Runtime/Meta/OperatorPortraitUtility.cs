/****************************************************
    文件：OperatorPortraitUtility.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 18:00:00
	功能：按干员数字 ID 加载统一的 UI 立绘资源。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    public static class OperatorPortraitUtility
    {
        public static string GetPortraitName(string operatorId)
        {
            switch (operatorId)
            {
                case "1":
                    return "operator_frost_queen";
                case "2":
                    return "operator_flame_mage";
                case "3":
                    return "operator_white_knight";
                case "4":
                    return "operator_red_knight";
                case "5":
                    return "operator_frost_star";
                default:
                    return "operator_frost_queen";
            }
        }

        public static Sprite Load(string operatorId)
        {
            return AssetService.Load <Sprite>(
                "UIHandDrawn/OperatorPortraits/" + GetPortraitName(operatorId));
        }

        /// <summary>
        /// 选角/技能面板使用裁掉透明边的版本；缺失时回退到完整立绘。
        /// 裁切版已收进 UI 图集，走图集取名可与其他 UI 图标合批。
        /// </summary>
        public static Sprite LoadCropped(string operatorId)
        {
            string name = GetPortraitName(operatorId);
            string path = "UIHandDrawn/OperatorPortraits/Cropped/" + name;
            Sprite cropped = UiAtlasService.Get(name, path);
            return cropped != null ? cropped : Load(operatorId);
        }
    }
}
