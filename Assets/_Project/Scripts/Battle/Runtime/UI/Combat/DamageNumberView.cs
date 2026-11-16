/****************************************************
    文件：DamageNumberView.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 15:30:00
	功能：单个伤害飘字，屏幕空间上飘、缩放、淡出，结束后交给管理器回收。
*****************************************************/

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PrismTactics.Battle
{
    public sealed class DamageNumberView : MonoBehaviour
    {
        [SerializeField] private Text text;
        [SerializeField] private CanvasGroup canvasGroup;

        private RectTransform _rect;
        private Coroutine _routine;

        public event Action<DamageNumberView> OnFinished;

        private void Awake()
        {
            _rect = transform as RectTransform;
            if (text == null)
            {
                text = GetComponent<Text>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }
        }

        public void Play(Vector2 screenPosition, string content, Color color, float duration)
        {
            if (_rect == null)
            {
                _rect = transform as RectTransform;
            }

            if (text == null)
            {
                text = GetComponent<Text>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            Stop();
            _rect.position = screenPosition;
            text.text = content;
            text.color = color;
            canvasGroup.alpha = 1f;
            transform.localScale = Vector3.one * 1.18f;
            _routine = StartCoroutine(Animate(duration));
        }

        public void Stop()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private IEnumerator Animate(float duration)
        {
            float elapsed = 0f;
            Vector2 start = _rect.position;
            Vector2 end = start + Vector2.up * 48f;
            float safeDuration = Mathf.Max(0.1f, duration);

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                _rect.position = Vector2.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
                transform.localScale = Vector3.one * Mathf.Lerp(1.18f, 0.86f, t);
                canvasGroup.alpha = t < 0.55f
                    ? 1f
                    : Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
                yield return null;
            }

            _routine = null;
            OnFinished?.Invoke(this);
        }

        private void OnDisable()
        {
            Stop();
        }
    }
}
