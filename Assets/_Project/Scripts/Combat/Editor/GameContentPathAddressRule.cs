/****************************************************
    文件：GameContentPathAddressRule.cs
    作者：DADI
    日期：2026/11/2 01:30:00
    功能：YooAsset 地址规则：去掉 GameContent 前缀和扩展名，保持旧 Resources 路径。
*****************************************************/

using System.IO;
using UnityEditor;
using YooAsset.Editor;

namespace PrismTactics.EditorTools
{
    [DisplayName("地址: GameContent 相对路径")]
    public class GameContentPathAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data)
        {
            const string root = "Assets/GameContent/";
            string path = data.AssetPath.Replace('\\', '/');
            if (path.StartsWith(root))
            {
                path = path.Substring(root.Length);
            }

            return Path.ChangeExtension(path, null);
        }
    }
}