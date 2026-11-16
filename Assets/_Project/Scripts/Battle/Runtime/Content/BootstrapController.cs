/****************************************************
    文件：BootstrapController.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/1 15:12:00
    功能：Boot 场景入口：协程加载内容并显示进度/错误，成功后进入 M1_Battle。
*****************************************************/

using PrismTactics.Launcher;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 唯一运行时入口。加载失败时停留在本场景并显示错误与重试，不静默回退。
    /// </summary>
    public sealed class BootstrapController : MonoBehaviour
    {
        #region 常量与字段
        private const string GameScene = "M1_Battle";

        private float _progress;// 当前加载进度，范围 0~1，用于进度条显示
        private string _stage = "准备中";
        private string _error; // 加载错误信息；为 null 表示当前没有错误
        private bool _running;// 是否正在加载，用于防止重复启动加载流程
        private Font _font;// 动态创建的字体，用于 OnGUI 显示中文
        private Texture2D _white;// 1x1 白色纹理，用于绘制纯色背景、面板和进度条

        private string _hotUpdateInfo = "热更：未加载";
        #endregion

        #region Unity 生命周期
        private void Start()
        {
            EnsureWindowSize();// 非编辑器环境固定窗口化，方便录屏和切换窗口
            BeginLoad();// 启动内容加载协程
        }
        #endregion

        #region 窗口尺寸
        private static void EnsureWindowSize()
        {
#if !UNITY_EDITOR
            // 录屏使用固定 1600x900 窗口，窗口可拖动和缩放。
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Debug.Log(
                $"[Window] screen={Screen.width}x{Screen.height}, " +
                $"current={Screen.currentResolution.width}x{Screen.currentResolution.height}, " +
                $"dpi={Screen.dpi}, mode={Screen.fullScreenMode}");
#endif
        }
        #endregion

        #region 加载流程
        /// <summary>
        /// 开始加载流程。
        /// 若已经在加载中则直接返回，避免重复启动。
        /// 重置进度、阶段和错误状态，然后启动 Run 协程。
        /// </summary>
        private void BeginLoad()
        {
            if (_running)
            {
                return;
            }

            _running = true;// 标记加载中
            _error = null;// 清除上一次错误
            _progress = 0f; // 重置进度
            _stage = "准备中";// 重置阶段文本
            StartCoroutine(Run());// 启动异步加载协程
        }
        /// <summary>
        /// 加载主流程协程：
        /// 1. 加载内容资源；
        /// 2. 根据内容版本加载热更；
        /// 3. 记录热更信息；
        /// 4. 内容加载成功则切换到游戏场景，否则停留在本场景显示错误。
        /// </summary>
        private IEnumerator Run()
        {
            yield return LoadContentAndHotUpdate(
                (progress, stage) =>
                {
                    _progress = progress;
                    _stage = stage;
                },
                error => { _error = error; });

            _hotUpdateInfo = HotUpdateRuntime.IsLoaded
                ? $"热更 {HotUpdateRuntime.Version}：{HotUpdateRuntime.BootMessage}"
                : "AOT 基线（未加载热更）";
            Debug.Log("[HotUpdate] " + _hotUpdateInfo);
            _running = false;
            if (ContentService.IsLoaded)
            {
                SceneManager.LoadScene(GameScene);
            }
        }

        /// <summary>
        /// Boot 的加载流程：内容包 + 热更注册。
        /// Boot 场景和性能压测测试共用，保证热更技能处理器（如 frost_nova）已注册。
        /// </summary>
        public static IEnumerator LoadContentAndHotUpdate(
            Action<float, string> onProgress = null,
            Action<string> onContentError = null)
        {
            yield return ContentService.LoadAsync(onProgress, onContentError);

            string hotVersion = ContentService.ActiveRelease != null
                ? ContentService.ActiveRelease.hotUpdateResVersion
                : string.Empty;

            string hotError = null;
            yield return HotUpdateService.LoadAsync(
                hotVersion,
                onProgress,
                error => { hotError = error; });

            if (!string.IsNullOrEmpty(hotError))
            {
                Debug.LogWarning("[HotUpdate] 跳过热更：" + hotError);
            }
        }
        #endregion

        #region UI绘制
        /// <summary>
        /// Unity IMGUI 绘制入口：绘制启动界面、进度条、错误信息和重试按钮。
        /// </summary>
        private void OnGUI()
        {
            EnsureGuiResources();// 确保字体和白色纹理等 GUI 资源已创建
            // 保存当前 GUI 颜色，绘制完成后恢复
            Color oldColor = GUI.color;
            // 绘制全屏深色背景
            GUI.color = new Color(0.04f, 0.05f, 0.07f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _white);
            GUI.color = oldColor;
            // 计算中央面板尺寸：宽度最多 620，左右至少留 20 像素边距
            float width = Mathf.Min(620f, Screen.width - 40f);
            float height = _error == null ? 190f : 260f;// 有错误时面板更高，用于显示错误文本和重试按钮
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);// 计算面板在屏幕中央的位置
            // 绘制面板背景
            GUI.color = new Color(0.11f, 0.12f, 0.16f, 1f);
            GUI.DrawTexture(panel, _white);
            GUI.color = oldColor;
            // 在面板内部留出 24 像素左右、18 像素上下边距，开始 GUILayout 区域
            GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, panel.height - 36f));
            // 标题
            GUILayout.Label("PrismTactics", TitleStyle());
            GUILayout.Space(6f);
            // 当前加载阶段和热更信息
            GUILayout.Label(_stage, BodyStyle());
            GUILayout.Label(_hotUpdateInfo, BodyStyle());
            GUILayout.Space(14f);
            // 绘制进度条
            DrawProgressBar(_progress);
            GUILayout.Space(8f);
            // 显示百分比文本
            GUILayout.Label(Mathf.RoundToInt(_progress * 100f) + "%", BodyStyle());
            // 如果存在错误，显示错误信息与“重试”按钮
            if (_error != null)
            {
                GUILayout.Space(12f);
                GUILayout.Label(_error, ErrorStyle());
                GUILayout.Space(10f);
                // 点击重试按钮后重新开始加载流程
                if (GUILayout.Button("重试", GUILayout.Height(34f)))
                {
                    BeginLoad();
                }
            }
            // 结束 GUILayout 区域
            GUILayout.EndArea();
        }
        /// <summary>
        /// 绘制进度条：先绘制灰色背景，再按进度比例绘制蓝色填充。
        /// </summary>
        /// <param name="value">进度值，0~1</param>
        private void DrawProgressBar(float value)
        {
            var rect = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
            Color oldColor = GUI.color;

            GUI.color = new Color(0.22f, 0.23f, 0.28f, 1f);
            GUI.DrawTexture(rect, _white);

            GUI.color = new Color(0.35f, 0.72f, 0.95f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), _white);

            GUI.color = oldColor;
        }
        /// <summary>
        /// 确保 GUI 所需的字体和白色纹理已创建。
        /// 字体优先使用系统中文字体，保证中文正常显示。
        /// </summary>
        private void EnsureGuiResources()
        {
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 18);
                if (_font != null)
                {
                    GUI.skin.font = _font;
                }
            }

            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
        }
        // 标题样式缓存，懒加载创建
        private GUIStyle _titleStyle;
        /// <summary>
        /// 获取标题样式：26 号加粗、浅色文字。
        /// </summary>
        private GUIStyle TitleStyle()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 26,
                    fontStyle = FontStyle.Bold,
                };
                _titleStyle.normal.textColor = new Color(0.92f, 0.94f, 0.98f);
            }

            return _titleStyle;
        }
        // 正文样式缓存，懒加载创建
        private GUIStyle _bodyStyle;
        /// <summary>
        /// 获取正文样式：16 号、浅灰蓝色文字。
        /// </summary>
        private GUIStyle BodyStyle()
        {
            if (_bodyStyle == null)
            {
                _bodyStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                };
                _bodyStyle.normal.textColor = new Color(0.78f, 0.82f, 0.9f);
            }

            return _bodyStyle;
        }
        // 错误样式缓存，懒加载创建
        private GUIStyle _errorStyle;
        /// <summary>
        /// 获取错误样式：14 号、红色文字，支持自动换行。
        /// </summary>
        private GUIStyle ErrorStyle()
        {
            if (_errorStyle == null)
            {
                _errorStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    wordWrap = true,
                };
                _errorStyle.normal.textColor = new Color(1f, 0.52f, 0.45f);
            }

            return _errorStyle;
        }
        #endregion
    }
}
