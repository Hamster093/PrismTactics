/****************************************************
    文件：BattleHotUpdateVisualService.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/3 0:37:59
	功能：AOT 侧热更视觉服务：按地址加载 Prefab，生成/销毁热更技能视觉对象。
*****************************************************/

using PrismTactics.Battle;
using PrismTactics.Combat;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class BattleHotUpdateVisualService : IHotUpdateVisualService
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        public GameObject Spawn(string address, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = AssetService.Load<GameObject>(address);
            if (prefab == null) return null;

            GameObject instance = Object.Instantiate(prefab, position, rotation);
            _spawned.Add(instance);
            return instance;
        }

        public void Despawn(GameObject visual)
        {
            if (visual == null) return;
            _spawned.Remove(visual);
            Object.Destroy(visual);
        }

        public void ClearAll()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.Destroy(_spawned[i]);
            _spawned.Clear();
        }
    }
}
