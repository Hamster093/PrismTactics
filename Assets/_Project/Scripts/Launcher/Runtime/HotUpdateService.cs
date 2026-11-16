using System;
using System.Collections;
using System.Reflection;
using HybridCLR;
using UnityEngine;
using YooAsset;

namespace PrismTactics.Launcher
{
    /// <summary>
    /// 热更加载服务：负责按顺序初始化 YooAsset 包、更新清单、可选补充 AOT 元数据、加载热更 DLL 并注册入口。
    ///
    /// 核心流程：
    ///   IsLoaded 检查 -> 包初始化参数（编辑器模拟 / 运行时离线）-> 创建/获取包 -> 初始化包
    ///   -> 解析清单版本 -> 更新清单 -> 可选 AOT 元数据 -> 加载 DLL -> 反射入口并 OnRegister。
    ///
    /// 职责边界：
    /// - 本类只负责“把热更 DLL 加载进内存并注册入口”；
    /// - 热更业务逻辑由 IHotUpdateEntry 实现，本类不关心具体业务。
    ///
    /// 平台差异：
    /// - UNITY_EDITOR 使用 EditorSimulateMode，版本通过 RequestPackageVersionAsync 读取；
    /// - 运行时使用 OfflinePlayMode + 内置文件系统，版本由外部 packageVersion 传入。
    /// </summary>
    public static class HotUpdateService
    {
        #region 常量与字段
        public const string PackageName = "HotUpdateRes";/// <summary>YooAsset 热更资源包名；必须与构建/发布配置一致。</summary>
        public const string DllAddress = "PrismTactics.HotUpdate.dll";/// <summary>热更 DLL 在 YooAsset 中的资源地址/文件名；硬编码，需与打包产物一致。</summary>
        public const string AotMetadataAddress = "mscorlib.dll"; /// <summary>AOT 补充元数据资源地址；硬编码为 mscorlib.dll，缺失时流程可继续。</summary>
        #endregion

        #region 公开接口
        /// <summary>
        /// 异步加载热更资源并注册入口。
        ///
        /// 执行顺序：
        /// 1. 已加载则直接返回；
        /// 2. 按平台准备包初始化参数；
        /// 3. 初始化 YooAssets 并创建/获取包；
        /// 4. 初始化包；
        /// 5. 解析清单版本（编辑器请求版本，运行时用 packageVersion）；
        /// 6. 更新清单；
        /// 7. 可选加载 AOT 元数据；
        /// 8. 加载热更 DLL；
        /// 9. 反射查找 IHotUpdateEntry 入口并注册；
        /// 10. 完成回调。
        ///
        /// 前置条件：HotUpdateRuntime 尚未加载；若已加载则静默 yield break，不触发进度/错误回调。
        /// 副作用：会初始化 YooAssets、创建/初始化包、加载程序集、注册 HotUpdateRuntime，并调用 entry.OnRegister。
        /// 错误处理：错误通过 onError 回调返回；协程本身不向外抛异常。
        /// </summary>
        /// <param name="packageVersion">运行时清单版本；编辑器模拟模式下不使用，改为向包请求版本。</param>
        /// <param name="onProgress">进度回调，参数为硬编码进度值和阶段描述。</param>
        /// <param name="onError">错误回调；准备、初始化、加载、注册失败时调用。</param>
        public static IEnumerator LoadAsync(
            string packageVersion,
            Action<float, string> onProgress,
            Action<string> onError)
        {
            // 1. 已加载则直接返回；注意：这里不回调 onProgress/onError，调用方需接受静默幂等返回。
            if (HotUpdateRuntime.IsLoaded)
            {
                yield break;
            }

            ResourcePackage package = null;
            InitializeParameters initParameters = null;
            string prepareError = null;

            try
            {
                // 2. 按平台准备包初始化参数；硬编码进度 0.05 仅用于 UI 反馈，不代表真实耗时比例。
                onProgress?.Invoke(0.05f, "初始化热更包");
#if UNITY_EDITOR
                // 编辑器模拟模式：从 SimulateBuild 得到包根目录；版本稍后通过 RequestPackageVersionAsyn
                PackageInvokeBuildResult build = EditorSimulateModeHelper.SimulateBuild(PackageName);
                if (build == null || string.IsNullOrEmpty(build.PackageRootDirectory))
                {
                    prepareError = "HotUpdateRes 模拟构建失败";
                }
                else
                {
                    initParameters = new EditorSimulateModeParameters
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                            build.PackageRootDirectory),
                    };
                }
#else
                // 运行时离线模式：资源来自内置文件系统；packageVersion 由 release.json 等外部配置传入。
                initParameters = new OfflinePlayModeParameters
                {
                    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                };
#endif
                if (prepareError == null)
                {
                    // 3. 初始化 YooAssets 并创建/获取包；YooAssets 是全局入口，先保证初始化，避免重复创建同名包。
                    if (!YooAssets.Initialized)
                    {
                        YooAssets.Initialize();
                    }

                    package = YooAssets.TryGetPackage(PackageName);
                    if (package == null)
                    {
                        package = YooAssets.CreatePackage(PackageName);
                    }
                }
            }
            catch (Exception exception)
            {
                // 初始化阶段异常转为错误回调，不向协程外抛出。
                prepareError = exception.Message;
            }

            if (prepareError != null || package == null)
            {
                onError?.Invoke(prepareError ?? "HotUpdateRes 创建失败");
                yield break;
            }
            // 4. 初始化包；失败则终止后续加载。
            InitializationOperation initOperation = package.InitializeAsync(initParameters);
            yield return initOperation;
            if (initOperation.Status != EOperationStatus.Succeed)
            {
                onError?.Invoke("初始化失败：" + initOperation.Error);
                yield break;
            }
            // 5. 解析清单版本：编辑器模拟模式向包请求版本；运行时使用外部传入的 packageVersion。
            string manifestVersion;
#if UNITY_EDITOR
            // 编辑器模拟模式：版本从包请求获得，忽略外部传入的 packageVersion。
            RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync(false);
            yield return versionOperation;
            if (versionOperation.Status != EOperationStatus.Succeed)
            {
                onError?.Invoke("读取版本失败：" + versionOperation.Error);
                yield break;
            }
            manifestVersion = versionOperation.PackageVersion;
#else
            // 运行时：清单版本由外部 release.json 提供；空版本无法更新清单。
            manifestVersion = packageVersion;
            if (string.IsNullOrWhiteSpace(manifestVersion))
            {
                onError?.Invoke("release.json 缺少 hotUpdateResVersion");
                yield break;
            }
#endif
            // 6. 更新清单；硬编码进度 0.4 仅用于 UI 反馈。
            onProgress?.Invoke(0.4f, "更新热更清单");
            UpdatePackageManifestOperation manifestOperation =
                package.UpdatePackageManifestAsync(manifestVersion);
            yield return manifestOperation;
            if (manifestOperation.Status != EOperationStatus.Succeed)
            {
                onError?.Invoke("更新清单失败：" + manifestOperation.Error);
                yield break;
            }

            // 可选：有 AOT 元数据就先补，缺了也能继续跑简单 DLL。
            if (package.CheckLocationValid(AotMetadataAddress))
            {
                AssetHandle metaHandle = package.LoadAssetAsync<TextAsset>(AotMetadataAddress);
                yield return metaHandle;
                if (metaHandle.Status == EOperationStatus.Succeed)
                {
                    var code = RuntimeApi.LoadMetadataForAOTAssembly(
                        metaHandle.GetAssetObject<TextAsset>().bytes,
                        HomologousImageMode.SuperSet);
                    // 返回码只打日志，不据此中断流程。
                    Debug.Log("[HotUpdate] AOT 元数据：" + code);
                }
                // 无论成功失败都释放句柄；成功时才读取 bytes。
                metaHandle.Release();
            }
            // 8. 加载热更 DLL；硬编码进度 0.75 仅用于 UI 反馈。
            onProgress?.Invoke(0.75f, "加载热更 DLL");
            // 以 TextAsset 承载 DLL 字节，资源地址由 DllAddress 硬编码指定。
            AssetHandle dllHandle = package.LoadAssetAsync<TextAsset>(DllAddress);
            yield return dllHandle;
            if (dllHandle.Status != EOperationStatus.Succeed)
            {
                onError?.Invoke("DLL 读取失败：" + dllHandle.LastError);
                yield break;
            }

            byte[] bytes = dllHandle.GetAssetObject<TextAsset>().bytes;
            dllHandle.Release();

            try
            {
                // 9. 内存加载 DLL，不走磁盘；入口类型全名硬编码，必须与热更工程一致。
                Assembly assembly = Assembly.Load(bytes);
                Type type = assembly.GetType("PrismTactics.HotUpdate.HotUpdateEntry");
                if (type == null || !typeof(IHotUpdateEntry).IsAssignableFrom(type))
                {
                    onError?.Invoke("DLL 里没有 IHotUpdateEntry 入口");
                    yield break;
                }
                // 入口要求无参构造；构造异常由外层 catch 转为错误回调。
                var entry = (IHotUpdateEntry)Activator.CreateInstance(type);
                HotUpdateRuntime.Register(entry);
                try
                {
                    entry.OnRegister();
                }
                catch (Exception exception)
                {
                    // Register 后 OnRegister 失败必须 Reset，避免运行时持有半初始化入口。
                    HotUpdateRuntime.Reset();
                    onError?.Invoke("热更注册失败：" + exception.Message);
                    yield break;
                }
                // 10. 完成回调；硬编码进度 1 表示完成，仅用于 UI 反馈。
                onProgress?.Invoke(1f, "热更完成");
                Debug.Log("[HotUpdate] " + entry.SelfCheck());
            }
            catch (Exception exception)
            {
                onError?.Invoke("加载 DLL 失败：" + exception.Message);
            }
        }
#endregion
    }
}