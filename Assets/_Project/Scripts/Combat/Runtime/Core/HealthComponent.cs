/****************************************************
    文件：HealthComponent.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：生命组件：最大/当前生命、受伤、治疗与死亡事件。
*****************************************************/

using System;
using UnityEngine;

namespace PrismTactics.Combat
{// 生命组件：管理单位血量、受伤与治疗
    [DisallowMultipleComponent]
    public sealed class HealthComponent : MonoBehaviour
    {
        // 最大生命
        [SerializeField] private float _maxHealth = 100f;
        // 当前生命
        [SerializeField] private float _currentHealth = 100f;

        public float MaxHealth => _maxHealth;
        public float CurrentHealth => _currentHealth;
        public bool IsAlive => _currentHealth > 0f;// 是否存活

        public event Action<DamageResult> Damaged;// 受伤事件
        public event Action Died;// 死亡事件
        public event Action<float, float> HealthChanged;// 当前/最大生命变化，含治疗
        // 以 maxHealth 满血初始化
        public void Initialize(float maxHealth)
        {
            _maxHealth = Mathf.Max(1f, maxHealth);
            _currentHealth = _maxHealth;
            HealthChanged?.Invoke(_currentHealth, _maxHealth);
        }
        // 承受一次伤害；扣血到 0 时触发死亡事件
        public void TakeDamage(in DamageResult result)
        {
            if (!IsAlive || result.FinalAmount <= 0f)
            {
                return;
            }

            _currentHealth = Mathf.Max(0f, _currentHealth - result.FinalAmount);
            Damaged?.Invoke(result);
            HealthChanged?.Invoke(_currentHealth, _maxHealth);

            if (_currentHealth <= 0f)
            {
                Died?.Invoke();
            }
        }
        // 治疗；不超上限，死亡状态下无效
        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }

            _currentHealth = Mathf.Min(_maxHealth, _currentHealth + amount);
            HealthChanged?.Invoke(_currentHealth, _maxHealth);
        }
    }
}
