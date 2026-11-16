/****************************************************
    文件：BattleObjectUtility.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 16:20:00
	功能：统一处理运行时与 EditMode 测试下的对象销毁。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    public static class BattleObjectUtility
    {
        public static void Destroy(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
