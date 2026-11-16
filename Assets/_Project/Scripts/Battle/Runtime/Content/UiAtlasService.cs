/****************************************************
    文件：UiAtlasService.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/09 18:10:00
	功能：UI 图集加载与缓存，优先从图集取 Sprite，未命中时回退单图地址。
*****************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 图集访问入口：全局只加载一次 SpriteAtlas，按 Sprite 名取图并缓存。
    /// 图集缺失时回退到单图地址，保证没打图集的环境也能跑。
    /// </summary>
    public static class UiAtlasService
    {
        // 图集地址：GameContent 相对路径去掉扩展名（YooAsset 地址规则）
        private const string AtlasAddress = "UIHandDrawn/UIHandDrawnAtlas";

        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private static SpriteAtlas _atlas;
        private static bool _warned;
        private static int _loggedTextures;

        /// <summary>图集是否已加载成功。</summary>
        public static bool AtlasLoaded => _atlas != null;

        /// <summary>按 Sprite 名取图；图集未命中时回退 fallbackAddress。</summary>
        public static Sprite Get(string spriteName, string fallbackAddress)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            if (Cache.TryGetValue(spriteName, out Sprite cached))
            {
                return cached;
            }

            EnsureAtlas();
            Sprite sprite = _atlas != null ? _atlas.GetSprite(spriteName) : null;
            // 单图打进 SpriteAtlas 后，Unity 常把 Sprite 名写成 "<name>_0"。
            if (sprite == null && _atlas != null)
            {
                sprite = _atlas.GetSprite(spriteName + "_0");
            }

            if (sprite == null && !string.IsNullOrEmpty(fallbackAddress))
            {
                sprite = AssetService.TryLoad<Sprite>(fallbackAddress);
            }

            if (sprite != null)
            {
                Cache[spriteName] = sprite;
                // 诊断用：打印前 3 个图集的贴图名，确认是否真的走了图集纹理
                if (_loggedTextures < 3)
                {
                    _loggedTextures++;
                    Debug.Log($"[UIAtlas] {spriteName} → texture={sprite.texture.name}");
                }
            }

            return sprite;
        }

        /// <summary>按资源路径取图：用文件名查图集，路径本身作为回退地址。</summary>
        public static Sprite GetByPath(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return null;
            }

            int index = resourcePath.LastIndexOf('/');
            string name = index >= 0 ? resourcePath.Substring(index + 1) : resourcePath;
            return Get(name, resourcePath);
        }

        /// <summary>首次访问时加载图集；失败只警告一次，之后每次仍会重试。</summary>
        private static void EnsureAtlas()
        {
            if (_atlas != null)
            {
                return;
            }

#if UNITY_EDITOR
            // A/B 对比开关：菜单 Tools/PrismTactics/诊断/禁用 UI 图集
            if (UnityEditor.EditorPrefs.GetBool("PrismTactics.UiAtlasDisabled", false))
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[UIAtlas] 已被诊断开关禁用，走单图回退。");
                }

                return;
            }
#endif
            _atlas = AssetService.TryLoad<SpriteAtlas>(AtlasAddress);
            if (_atlas != null)
            {
                // 诊断用：确认图集里确实有 Sprite
                Debug.Log($"[UIAtlas] 图集已加载，spriteCount={_atlas.spriteCount}");
                return;
            }

            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[UIAtlas] 图集未加载，回退单图加载：" + AtlasAddress);
            }
        }

        /// <summary>资源包释放时清空缓存，避免持有已释放的图集引用。</summary>
        public static void ClearCache()
        {
            Cache.Clear();
            _atlas = null;
            _warned = false;
            _loggedTextures = 0;
        }

        /// <summary>当前缓存数量，供性能诊断面板显示。</summary>
        public static int CachedCount => Cache.Count;
    }
}
