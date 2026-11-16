/****************************************************
    文件：ContentService.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/1 16:12:00
    功能：按发布清单加载 BattleRes：显式版本优先，失败时回退 previous，并报告进度与错误。
*****************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 由 Boot 场景用协程驱动；只认 release.json，失败时整包回退，不再静默读旧文件。
    /// </summary>
    public static class ContentService
    {
        #region 常量与公开状态
        /// <summary>战斗资源包名，必须与 YooAsset Collector 一致。</summary>
        public const string PackageName = "BattleRes";

        /// <summary>内容是否已加载完成。</summary>
        public static bool IsLoaded { get; private set; }

        /// <summary>最近一次失败原因；成功后清空。</summary>
        public static string LastError { get; private set; }

        /// <summary>当前生效的发布记录；UI/日志用。</summary>
        public static ContentReleaseEntry ActiveRelease { get; private set; }
        #endregion

        #region 私有状态字段
        private static bool _loading;// 是否正在执行加载流程，用于防止并发加载
        private static bool _attemptSucceeded; // 本次“单个发布版本尝试”是否成功（TryLoadRelease 的返回值载体）
        private static string _attemptError;// 本次“单个发布版本尝试”的失败原因
        private static ResourcePackage _activePackage;// 当前激活的 YooAsset 资源包，供 TryLoadConfigText 懒加载配置使用
        #endregion

        #region 主加载流程
        /// <summary>
        /// 读取 release.json，按 latest → previous 顺序尝试；显式 --content-release 时只试指定版本。
        /// </summary>
        public static IEnumerator LoadAsync(Action<float, string> onProgress = null, Action<string> onError = null)
        {
            // 已经加载完成：直接回调进度满值并结束
            if (IsLoaded)
            {
                onProgress?.Invoke(1f, "内容已就绪");
                yield break;
            }
            // 已有加载在进行：等待其结束，再根据结果回调错误
            if (_loading)
            {
                while (_loading)
                {
                    yield return null;
                }

                if (!IsLoaded)
                {
                    onError?.Invoke(LastError);
                }

                yield break;
            }
            // 进入加载状态并重置上一次的状态
            _loading = true;
            LastError = null;
            ActiveRelease = null;
            // 1. 读取发布清单
            onProgress?.Invoke(0.02f, "读取发布清单");
            ContentReleaseCatalog catalog = ContentReleaseStore.Load(out string catalogError);
            if (catalog == null)
            {
                Fail("读取发布清单", catalogError, onError);
                yield break;
            }
            // 2. 校验 App 版本兼容性
            if (!ContentReleaseStore.IsAppVersionCompatible(catalog, out string versionError))
            {
                Fail("检查版本兼容", versionError, onError);
                yield break;
            }
            // 3. 计算候选发布版本列表（latest/previous，或显式指定的单个版本）
            List<string> candidates = ContentReleaseStore.GetCandidateReleaseIds(catalog, out bool explicitRequest);
            if (candidates.Count == 0)
            {
                Fail("选择发布版本", "release.json 没有 latest/previous", onError);
                yield break;
            }
            // 4. 依次尝试每个候选版本：成功即停止，失败则回退到下一个
            for (int i = 0; i < candidates.Count; i++)
            {
                string releaseId = candidates[i];
                ContentReleaseEntry entry = catalog.Find(releaseId);
                if (entry == null)
                {
                    // 清单里找不到该发布记录，记录后继续尝试下一个
                    _attemptError = $"发布记录不存在：{releaseId}";
                    Debug.LogWarning("[Content] " + _attemptError);
                    continue;
                }

                onProgress?.Invoke(0.05f, $"加载发布 {entry.releaseId}");
                _attemptSucceeded = false;
                _attemptError = null;

                // 单次尝试：初始化包、更新清单、预加载全局配置
                yield return TryLoadRelease(entry, i, onProgress);

                if (_attemptSucceeded)
                {
                    // 成功：记录生效版本，标记已加载，回调完成
                    ActiveRelease = entry;
                    IsLoaded = true;
                    _loading = false;
                    Debug.Log($"[Content] 内容加载完成：release={entry.releaseId}，" +
                              $"BattleRes={entry.battleResVersion}，HotUpdateRes={entry.hotUpdateResVersion}");
                    onProgress?.Invoke(1f, "完成");
                    yield break;
                }
                // 失败：记录错误并清理 YooAsset，准备尝试下一个候选
                Debug.LogWarning($"[Content] 发布 {entry.releaseId} 加载失败：{_attemptError}");
                LastError = _attemptError;
                CleanupYooAssets();
            }
            // 所有候选都失败：根据是否为显式指定给出不同错误信息
            string finalError = explicitRequest
                ? "指定的内容版本不可用：" + candidates[0] + "；" + LastError
                : "latest 与 previous 都不可用：" + LastError;
            Fail("加载内容", finalError, onError);
        }

        /// <summary>退出播放或切换内容版本时重置；测试也会用。</summary>
        public static void Reset()
        {
            IsLoaded = false;
            _loading = false;
            _attemptSucceeded = false;
            _attemptError = null;
            LastError = null;
            ActiveRelease = null;
            CleanupYooAssets();
        }
        #endregion

        #region 单次发布加载
        /// <summary>
        /// 尝试加载指定发布版本：
        /// 初始化 YooAsset 包 → 更新到记录的清单版本 → 预加载全局配置。
        /// 成功时置 _attemptSucceeded = true；失败时写 _attemptError。
        /// </summary>
        private static IEnumerator TryLoadRelease(
            ContentReleaseEntry entry,
            int attemptIndex,
            Action<float, string> onProgress)
        {
            ResourcePackage package = null;
            InitializeParameters initParameters = null;
            string prepareError = null;
            // 1. 准备初始化参数与资源包对象
            try
            {
                onProgress?.Invoke(0.08f, "准备资源清单");
#if UNITY_EDITOR
                // 编辑器模拟模式始终用 Simulate 清单，但发布记录仍参与 schema/回退校验。
                PackageInvokeBuildResult buildResult = EditorSimulateModeHelper.SimulateBuild(PackageName);
                if (buildResult == null || string.IsNullOrEmpty(buildResult.PackageRootDirectory))
                {
                    prepareError = "EditorSimulateBuild 没有返回包目录";
                }
                else
                {
                    initParameters = new EditorSimulateModeParameters
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                            buildResult.PackageRootDirectory),
                    };
                }
#else
                // 运行时使用离线播放模式，读取内置资源
                initParameters = new OfflinePlayModeParameters
                {
                    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                };
#endif
                if (prepareError == null)
                {
                    // YooAsset 全局只初始化一次
                    if (!YooAssets.Initialized)
                    {
                        YooAssets.Initialize();
                    }
                    // 复用已存在的包，否则创建新包
                    package = YooAssets.TryGetPackage(PackageName);
                    if (package == null)
                    {
                        package = YooAssets.CreatePackage(PackageName);
                    }
                    _activePackage = package;
                    
                }
            }
            catch (Exception exception)
            {
                prepareError = "准备失败：" + exception.Message;
            }
            // 准备阶段失败：记录错误并结束本次尝试
            if (prepareError != null || package == null || initParameters == null)
            {
                _attemptError = prepareError ?? "资源包创建失败";
                yield break;
            }
            // 2. 初始化资源包
            onProgress?.Invoke(0.12f, $"初始化 {entry.battleResVersion}");
            InitializationOperation initOperation;
            try
            {
                initOperation = package.InitializeAsync(initParameters);
            }
            catch (Exception exception)
            {
                _attemptError = "初始化失败：" + exception.Message;
                yield break;
            }

            yield return initOperation;
            if (initOperation.Status != EOperationStatus.Succeed)
            {
                _attemptError = "初始化失败：" + initOperation.Error;
                yield break;
            }
            // 3. 确定要使用的清单版本
            string manifestVersion;
#if UNITY_EDITOR
            // 编辑器模拟模式：从模拟包读取版本号
            onProgress?.Invoke(0.2f, "读取模拟清单版本");
            RequestPackageVersionOperation versionOperation;
            try
            {
                versionOperation = package.RequestPackageVersionAsync(false);
            }
            catch (Exception exception)
            {
                _attemptError = "读取包版本失败：" + exception.Message;
                yield break;
            }

            yield return versionOperation;
            if (versionOperation.Status != EOperationStatus.Succeed)
            {
                _attemptError = "读取包版本失败：" + versionOperation.Error;
                yield break;
            }

            manifestVersion = versionOperation.PackageVersion;
#else
            // 回滚的关键：用清单里钉住的版本，而不是本地 version 文件里的“最新”。
            manifestVersion = entry.battleResVersion;
            if (string.IsNullOrWhiteSpace(manifestVersion))
            {
                _attemptError = "发布记录缺少 battleResVersion";
                yield break;
            }
#endif
            // 4. 更新资源清单到指定版本
            onProgress?.Invoke(0.3f, $"更新清单 {manifestVersion}");
            UpdatePackageManifestOperation manifestOperation;
            try
            {
                manifestOperation = package.UpdatePackageManifestAsync(manifestVersion);
            }
            catch (Exception exception)
            {
                _attemptError = "更新清单失败：" + exception.Message;
                yield break;
            }

            yield return manifestOperation;
            if (manifestOperation.Status != EOperationStatus.Succeed)
            {
                _attemptError = $"更新清单 {manifestVersion} 失败：" + manifestOperation.Error;
                yield break;
            }
            // 5. 统计需要预加载的全局配置数量
            AssetInfo[] assetInfos = package.GetAllAssetInfos();
            int total = 0;
            for (int i = 0; i < assetInfos.Length; i++)
            {
                if (IsGlobalConfig(assetInfos[i]))
                {
                    total++;
                }
            }

            if (total == 0)
            {
                _attemptError = "BattleRes 清单里没有全局配置";
                yield break;
            }
            // 6. 逐个加载全局配置文本
            var configs = new Dictionary<string, string>(StringComparer.Ordinal);
            int loaded = 0;
            for (int i = 0; i < assetInfos.Length; i++)
            {
                AssetInfo assetInfo = assetInfos[i];
                if (!IsGlobalConfig(assetInfo))
                {
                    continue;
                }
                // 进度区间 0.35 ~ 0.95，按已加载数量均分
                onProgress?.Invoke(0.35f + 0.6f * loaded / total,
                    $"预加载配置 {loaded + 1}/{total}：{assetInfo.Address}");

                AssetHandle handle;
                try
                {
                    handle = package.LoadAssetAsync<TextAsset>(assetInfo.Address);
                }
                catch (Exception exception)
                {
                    _attemptError = "加载 " + assetInfo.Address + " 失败：" + exception.Message;
                    yield break;
                }

                yield return handle;
                if (handle.Status != EOperationStatus.Succeed)
                {
                    _attemptError = "加载 " + assetInfo.Address + " 失败：" + handle.LastError;
                    handle.Release();
                    yield break;
                }

                TextAsset textAsset = handle.GetAssetObject<TextAsset>();
                if (textAsset != null)
                {
                    configs[assetInfo.Address] = textAsset.text;
                }

                handle.Release();
                loaded++;
            }
            // 7. 把配置交给 BattleConfigLoader，供后续按需加载
            BattleConfigLoader.SetPreloadedConfigs(configs);
            Debug.Log($"[Content] 全局配置预加载 {configs.Count} 个；地图/波次/每图战斗设置按需加载。");

            // 清单和配置都加载成功，把包交给 AssetService 。
            AssetService.Initialize(package);
            _attemptSucceeded = true;
        }
        #endregion

        #region 配置资源判定
        /// <summary>判断资源是否为 JSON 配置文件（按扩展名）。</summary>
        private static bool IsConfigAsset(AssetInfo assetInfo)
        {
            return assetInfo.AssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 启动只预加载全局配置；地图/波次/每图战斗设置按需加载，避免几万张图占内存。
        /// </summary>
        private static bool IsGlobalConfig(AssetInfo assetInfo)
        {
            // 先要求是 json 资源
            if (!IsConfigAsset(assetInfo))
            {
                return false;
            }
            // 只有约定的全局配置地址才纳入启动预加载
            string address = assetInfo.Address;
            return string.Equals(address, "levels", StringComparison.Ordinal)
                   || string.Equals(address, "relics", StringComparison.Ordinal)
                   || string.Equals(address, "operators", StringComparison.Ordinal)
                   || string.Equals(address, "skills", StringComparison.Ordinal)
                   || string.Equals(address, "items", StringComparison.Ordinal)
                   || string.Equals(address, "enemies", StringComparison.Ordinal)
                   || string.Equals(address, "battle", StringComparison.Ordinal);
        }
        #endregion

        #region 配置懒加载
        /// <summary>
        /// 按地址从已初始化的 BattleRes 同步取一段配置文本；供 BattleConfigLoader 懒加载地图/波次。
        /// AssetHandle 实现了 WaitForAsyncComplete，这里阻塞一次读取（文件很小）。
        /// </summary>
        public static string TryLoadConfigText(string configId, out string error)
        {
            error = null;
            // 前置条件：内容已加载且资源包可用
            if (!IsLoaded || _activePackage == null)
            {
                error = "内容尚未加载";
                return null;
            }
            // 校验地址是否存在于清单中
            try
            {
                if (!_activePackage.CheckLocationValid(configId))
                {
                    error = "BattleRes 清单里没有地址";
                    return null;
                }
            }
            catch (Exception exception)
            {
                error = "检查地址失败：" + exception.Message;
                return null;
            }
            // 同步阻塞读取（配置文件很小，避免引入异步复杂度）
            AssetHandle handle = _activePackage.LoadAssetAsync<TextAsset>(configId);
            handle.WaitForAsyncComplete();
            if (handle.Status != EOperationStatus.Succeed)
            {
                error = handle.LastError;
                handle.Release();
                return null;
            }
            // 取出文本并释放 handle
            TextAsset textAsset = handle.GetAssetObject<TextAsset>();
            string text = textAsset != null ? textAsset.text : null;
            handle.Release();
            if (text == null)
            {
                error = "加载结果为空";
            }

            return text;
        }
        #endregion

        #region 清理与失败处理
        /// <summary>
        /// 清理 YooAsset 相关状态：释放 AssetService、清空预加载配置、销毁 YooAssets。
        /// 用于回退重试和整体重置。
        /// </summary>
        private static void CleanupYooAssets()
        {
            AssetService.ReleaseAll();
            _activePackage = null;
            BattleConfigLoader.ClearPreloadedConfigs();
            if (YooAssets.Initialized)
            {
                YooAssets.Destroy();
            }
        }
        /// <summary>
        /// 统一失败处理：结束加载状态、拼接错误信息、打印日志并回调错误。
        /// </summary>
        private static void Fail(string stage, string message, Action<string> onError)
        {
            _loading = false;
            LastError = stage + "失败：" + message;
            Debug.LogError("[Content] " + LastError);
            onError?.Invoke(LastError);
        }
        #endregion
    }
}
