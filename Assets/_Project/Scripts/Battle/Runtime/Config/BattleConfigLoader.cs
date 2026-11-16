/****************************************************
    文件：BattleConfigLoader.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
    功能：优先读取启动时由 YooAsset 预加载的配置缓存，未命中时回退 GameContent/Configs 文件。
*****************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PrismTactics.Battle
{
    public static class BattleConfigLoader
    {
        // 启动预加载的配置文本；键是 YooAsset 地址（当前等于不带后缀的文件名）。
        private static Dictionary<string, string> _preloadedTexts;

        /// <summary>编辑器工具与 EditMode 测试的配置目录（也是 YooAsset 收集源）。</summary>
        public static string ConfigDirectory
        {
            get { return Path.Combine(Application.dataPath, "GameContent/Configs"); }
        }

        /// <summary>是否已由 YooAsset 接管配置；false 时 Load 走文件回退。</summary>
        public static bool HasPreloadedConfigs
        {
            get { return _preloadedTexts != null; }
        }

        /// <summary>已预加载的配置数量，用于启动日志和测试断言。</summary>
        public static int PreloadedConfigCount
        {
            get { return _preloadedTexts != null ? _preloadedTexts.Count : 0; }
        }

        /// <summary>
        /// 启动器把所有配置文本一次性交给这里；之后的 Load 不再碰磁盘。
        /// 传空会退回文件读取，便于编辑器工具和旧流程继续工作。
        /// </summary>
        public static void SetPreloadedConfigs(IDictionary<string, string> configs)
        {
            if (configs == null || configs.Count == 0)
            {
                _preloadedTexts = null;
                return;
            }

            _preloadedTexts = new Dictionary<string, string>(configs.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in configs)
            {
                if (!string.IsNullOrEmpty(pair.Key))
                {
                    _preloadedTexts[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>退出播放/切换内容版本时清空缓存，避免读到上一份配置。</summary>
        public static void ClearPreloadedConfigs()
        {
            _preloadedTexts = null;
        }

        /// <summary>配置按类型分目录；地址仍是文件名，保持代码侧 id 不变。</summary>
        public static string GetConfigFolder(string configId)
        {
            if (string.IsNullOrEmpty(configId))
            {
                return string.Empty;
            }

            if (string.Equals(configId, "levels", StringComparison.Ordinal)
                || configId.StartsWith("battle", StringComparison.Ordinal))
            {
                return "Core";
            }

            if (configId.StartsWith("map", StringComparison.Ordinal))
            {
                return "Map";
            }

            if (configId.StartsWith("waves", StringComparison.Ordinal))
            {
                return "Waves";
            }

            if (string.Equals(configId, "operators", StringComparison.Ordinal)
                || string.Equals(configId, "enemies", StringComparison.Ordinal))
            {
                return "Units";
            }

            if (string.Equals(configId, "skills", StringComparison.Ordinal)
                || string.Equals(configId, "relics", StringComparison.Ordinal))
            {
                return "Skills";
            }

            if (string.Equals(configId, "items", StringComparison.Ordinal))
            {
                return "Items";
            }

            return string.Empty;
        }

        /// <summary>编辑器工具/EditMode 测试按目录读取配置的绝对路径。</summary>
        public static string GetConfigFilePath(string configId)
        {
            return Path.Combine(ConfigDirectory, GetConfigFolder(configId), configId + ".json");
        }

        /// <summary>启动预加载命中或按需懒加载成功后，把文本放进缓存。</summary>
        public static void AddPreloadedText(string configId, string text)
        {
            if (string.IsNullOrEmpty(configId) || text == null)
            {
                return;
            }

            if (_preloadedTexts == null)
            {
                _preloadedTexts = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            _preloadedTexts[configId] = text;
        }

        public static T Load<T>(string configId) where T : class
        {
            if (_preloadedTexts != null && _preloadedTexts.TryGetValue(configId, out string preloadedText))
            {
                return Parse<T>(configId, preloadedText);
            }

            if (Application.isPlaying)
            {
                if (_preloadedTexts == null)
                {
                    // 运行时不再偷偷读文件：没有缓存说明 Boot 加载失败或有人绕过了 Boot。
                    Debug.LogError("配置缓存未加载，禁止在 Play Mode 直接读文件：" + configId
                                   + "（请从 Bootstrap 场景启动）");
                    return null;
                }

                // 地图/波次等非全局配置按需从 BattleRes 加载，避免几万张图全量进内存。
                string lazyText = ContentService.TryLoadConfigText(configId, out string lazyError);
                if (lazyText == null)
                {
                    Debug.LogError("按需加载配置失败：" + configId + "（" + lazyError + "）");
                    return null;
                }

                _preloadedTexts[configId] = lazyText;
                Debug.Log("[Config] 按需加载：" + configId);
                return Parse<T>(configId, lazyText);
            }

            string path = GetConfigFilePath(configId);
            if (!File.Exists(path))
            {
                Debug.LogWarning("配置文件不存在，将使用内置默认值：" + path);
                return null;
            }

            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogError("读取配置失败：" + path + "\n" + exception.Message);
                return null;
            }
        }

        private static T Parse<T>(string configId, string text) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(text);
            }
            catch (Exception exception)
            {
                Debug.LogError("解析配置失败：" + configId + "\n" + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// 地图 ID 对应的文件名：M1_Test 使用旧的 map.json，其余使用 map_<id>.json。
        /// </summary>
        public static string GetMapConfigId(string mapId)
        {
            return IsDefaultMap(mapId) ? "map" : "map_" + Sanitize(mapId);
        }

        public static string GetWavesConfigId(string mapId)
        {
            return IsDefaultMap(mapId) ? "waves" : "waves_" + Sanitize(mapId);
        }

        public static string GetBattleConfigId(string mapId)
        {
            return IsDefaultMap(mapId) ? "battle" : "battle_" + Sanitize(mapId);
        }

        public static MapDefinition LoadMap(string mapId)
        {
            MapDefinition map = Load<MapDefinition>(GetMapConfigId(mapId));
            if (map != null)
            {
                return map;
            }

            // 自定义地图文件缺失时退回旧 map.json，避免场景直接跑不起来。
            return Load<MapDefinition>("map");
        }

        public static WaveLibrary LoadWaves(string mapId)
        {
            WaveLibrary waves = Load<WaveLibrary>(GetWavesConfigId(mapId));
            return waves ?? Load<WaveLibrary>("waves");
        }

        public static BattleSettings LoadBattle(string mapId)
        {
            BattleSettings settings = Load<BattleSettings>(GetBattleConfigId(mapId));
            return settings ?? Load<BattleSettings>("battle");
        }

        private static bool IsDefaultMap(string mapId)
        {
            return string.IsNullOrWhiteSpace(mapId)
                   || string.Equals(mapId, "M1_Test", StringComparison.OrdinalIgnoreCase);
        }

        private static string Sanitize(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                return "default";
            }

            var builder = new System.Text.StringBuilder(mapId.Length);
            for (int i = 0; i < mapId.Length; i++)
            {
                char character = mapId[i];
                builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            }

            return builder.ToString();
        }
    }
}
