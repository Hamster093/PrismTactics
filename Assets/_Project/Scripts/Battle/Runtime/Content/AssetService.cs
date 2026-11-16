/****************************************************
    文件：AssetService.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/2 12:53:00
    功能：YooAsset 资源服务：按地址加载并缓存句柄，区分必需资源与可选资源。
*****************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace PrismTactics.Battle
{
    public static class AssetService
    {
        private static ResourcePackage _package;
        private static readonly Dictionary<string, AssetHandle> Handles = new Dictionary<string, AssetHandle>();

        public static void Initialize(ResourcePackage package)
        {
            _package = package;
        }

        /// <summary>
        /// 必需资源：找不到会报 Error，用于配置、地图、单位目录这类缺了就玩不了的资源。
        /// </summary>
        public static T Load<T>(string address) where T : UnityEngine.Object
        {
            T asset = TryLoad<T>(address);
            if (asset == null)
            {
                // EditMode 测试没有 Boot 流程，拿不到 ResourcePackage；这里只警告，不打断测试。
                if (Application.isPlaying)
                {
                    Debug.LogError("[Asset] 加载失败：" + address);
                }
                else
                {
                    Debug.LogWarning("[Asset] 非 Play 模式没有内容包，跳过加载：" + address);
                }
            }

            return asset;
        }

        /// <summary>
        /// 可选资源：找不到或类型不匹配时安静返回 null，用于头像、技能图标这类允许缺图的 UI。
        /// </summary>
        public static T TryLoad<T>(string address) where T : UnityEngine.Object
        {
            if (Handles.TryGetValue(address, out AssetHandle cached))
            {
                if (cached.IsValid)
                {
                    return cached.GetAssetObject<T>();
                }

                cached.Release();
                Handles.Remove(address);
            }

            if (_package == null || !_package.CheckLocationValid(address))
            {
                return null;
            }

            try
            {
                AssetHandle handle = _package.LoadAssetAsync<T>(address);
                handle.WaitForAsyncComplete();
                if (handle.Status != EOperationStatus.Succeed)
                {
                    handle.Release();
                    return null;
                }

                Handles[address] = handle;
                return handle.GetAssetObject<T>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void ReleaseAll()
        {
            foreach (AssetHandle handle in Handles.Values)
            {
                handle.Release();
            }

            Handles.Clear();
            UiAtlasService.ClearCache();
            _package = null;
        }

        /// <summary>
        /// 加载 Prefab 并取根节点上的组件。YooAsset 的主资源类型是 GameObject，
        /// 不能直接 LoadAssetAsync&lt;MonoBehaviour&gt;，所以统一走这里。
        /// </summary>
        public static T LoadComponent<T>(string address) where T : Component
        {
            GameObject prefab = Load<GameObject>(address);
            return prefab != null ? prefab.GetComponent<T>() : null;
        }
    }
}
