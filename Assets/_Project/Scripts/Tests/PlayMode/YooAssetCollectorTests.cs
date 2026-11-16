/****************************************************
    文件：YooAssetCollectorTests.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/31 15:50:00
    功能：M3.1 验证：BattleRes 运行时预加载把配置交给 BattleConfigLoader，并能按地址取到真实配置。
*****************************************************/

using System.Collections;
using NUnit.Framework;
using PrismTactics.Battle;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;

namespace PrismTactics.Tests.PlayMode
{
    /// <summary>
    /// 通过 ContentService 加载内容，断言包、缓存和真实配置都能取到。
    /// 测试显式驱动加载，不依赖 Boot 场景。
    /// </summary>
    public sealed class YooAssetCollectorTests
    {
        private const string PackageName = "BattleRes";

        [UnityTest]
        public IEnumerator ContentService_LoadAsync_PreloadsConfigs()
        {
            yield return ContentService.LoadAsync();

            Assert.That(ContentService.IsLoaded, Is.True, ContentService.LastError);
            Assert.That(YooAssets.Initialized, Is.True, "YooAssets 没有初始化");

            ResourcePackage package = YooAssets.TryGetPackage(PackageName);
            Assert.That(package, Is.Not.Null, $"找不到资源包 {PackageName}");

            Assert.That(BattleConfigLoader.HasPreloadedConfigs, Is.True, "配置没有走 YooAsset 缓存");
            Assert.That(BattleConfigLoader.PreloadedConfigCount, Is.GreaterThanOrEqualTo(6),
                "全局配置预加载数量不足，检查 GameContent/Configs 是否已同步");

            Assert.That(package.CheckLocationValid("levels"), Is.True, "清单里找不到 levels 地址");
            Assert.That(package.CheckLocationValid("map_M1_Perf60"), Is.True, "清单里找不到多地图配置");

            LevelLibrary levels = BattleConfigLoader.Load<LevelLibrary>("levels");
            Assert.That(levels, Is.Not.Null);
            Assert.That(levels.levels, Is.Not.Null.And.Count.GreaterThan(0));

            AssetHandle handle = package.LoadAssetAsync<TextAsset>("levels");
            yield return handle;

            Assert.That(handle.Status, Is.EqualTo(EOperationStatus.Succeed), handle.LastError);
            TextAsset textAsset = handle.GetAssetObject<TextAsset>();
            Assert.That(textAsset, Is.Not.Null);
            StringAssert.Contains("levels", textAsset.text);

            handle.Release();
        }
    }
}
