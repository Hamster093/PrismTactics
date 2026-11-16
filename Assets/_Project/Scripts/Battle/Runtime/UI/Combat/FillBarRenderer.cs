/****************************************************
    文件：FillBarRenderer.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 15:30:00
	功能：用 MaterialPropertyBlock 驱动 SpriteFill 材质裁剪，不实例化材质。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class FillBarRenderer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer fillRenderer;
        [SerializeField] private float fillDirection;

        private MaterialPropertyBlock _propertyBlock;
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int DirectionId = Shader.PropertyToID("_FillDirection");

        public void SetFill(float value01)
        {
            if (fillRenderer == null)
            {
                return;
            }

            EnsurePropertyBlock();
            fillRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(FillId, Mathf.Clamp01(value01));
            fillRenderer.SetPropertyBlock(_propertyBlock);
        }

        public void SetColor(Color color)
        {
            if (fillRenderer != null)
            {
                fillRenderer.color = color;
            }
        }

        public void SetVisible(bool visible)
        {
            if (fillRenderer != null)
            {
                fillRenderer.enabled = visible;
            }
        }

        private void Awake()
        {
            if (fillRenderer == null)
            {
                fillRenderer = GetComponent<SpriteRenderer>();
            }

            EnsurePropertyBlock();
            fillRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(DirectionId, fillDirection);
            fillRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void EnsurePropertyBlock()
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }
        }
    }
}
