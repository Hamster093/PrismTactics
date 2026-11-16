/****************************************************
    文件：UIManager.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 12:00:00
	功能：UI 面板单例管理：扫描、注册、打开、关闭与刷新。
*****************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class UIManager : MonoBehaviour
    {
        private readonly Dictionary<Type, PanelBase> _panels = new Dictionary<Type, PanelBase>();

        public static UIManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ScanPanels();
        }

        public void ScanPanels()
        {
            PanelBase[] panels = GetComponentsInChildren<PanelBase>(true);
            for (int i = 0; i < panels.Length; i++)
            {
                Register(panels[i]);
            }
        }

        public void Register(PanelBase panel)
        {
            if (panel == null)
            {
                return;
            }

            _panels[panel.GetType()] = panel;
        }

        public T Get<T>() where T : PanelBase
        {
            if (_panels.TryGetValue(typeof(T), out PanelBase panel))
            {
                return panel as T;
            }

            T found = GetComponentInChildren<T>(true);
            if (found != null)
            {
                Register(found);
            }

            return found;
        }

        public void Open<T>(object data = null) where T : PanelBase
        {
            T panel = Get<T>();
            if (panel == null)
            {
                Debug.LogWarning("[UI] 找不到面板：" + typeof(T).Name);
                return;
            }

            panel.OnOpen(data);
            panel.OnRefresh(data);
        }

        public void Close<T>() where T : PanelBase
        {
            T panel = Get<T>();
            panel?.OnClose();
        }

        public void CloseAll()
        {
            foreach (PanelBase panel in _panels.Values)
            {
                panel.OnClose();
            }
        }

        public bool IsOpen<T>() where T : PanelBase
        {
            T panel = Get<T>();
            return panel != null && panel.IsOpen;
        }
    }
}
