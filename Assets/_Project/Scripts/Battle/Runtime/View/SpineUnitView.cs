/****************************************************
    文件：SpineUnitView.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 14:20:00
	功能：Spine 二维单位表现层：待机循环播放，攻击时播放攻击动画并自动回到待机。
*****************************************************/

using PrismTactics.Combat;
using Spine;
using Spine.Unity;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 给 <see cref="SkeletonAnimation"/> 套一层待机/攻击的简单状态：
    /// 待机动画循环播放；播放攻击动画后自动接回待机动画，无需外部再调用。
    /// 若绑定了 <see cref="CombatEntity"/>，会跟随其攻击循环（前摇开始即播攻击）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpineUnitView : MonoBehaviour
    {
        [SerializeField] private SkeletonAnimation skeletonAnimation;
        [SerializeField] private CombatEntity combatEntity;
        [SerializeField] private string idleAnimation = "Idle";
        [SerializeField] private string attackAnimation = "Attack";
        [SerializeField] private bool playIdleOnEnable = true;
        [SerializeField] private string[] hiddenProjectileSlots =
        {
            "Attack_object_1",
            "Attack_object_2",
            "Attack_object_3",
            "attack",
            "attack_2",
            "attack_3"
        };

        private AttackCycleState _lastAttackState = AttackCycleState.Ready;
        private bool _attackHitTriggered;
        private readonly List<CombatEntity> _multiTargets = new List<CombatEntity>();
        private float _volleyTimer;
        private bool _projectileFired;
        private bool _visualBound;
        private float _visualYaw = 180f;
        private float _appliedYaw = float.NaN;
        private Skeleton _cachedSlotSkeleton;
        private Slot[] _cachedHiddenSlots;
        private bool _useProjectile;
        private float _projectileSpeed = 4f;
        private string[] _hiddenSlotNames;
        private static readonly string[] DefaultHiddenProjectileSlots =
        {
            "Attack_object_1",
            "Attack_object_2",
            "Attack_object_3",
            "attack",
            "attack_2",
            "attack_3"
        };

        public SkeletonAnimation SkeletonAnimation => skeletonAnimation;
        public string IdleAnimation => idleAnimation;
        public string AttackAnimation => attackAnimation;

        /// <summary>
        /// 运行时给实例绑定战斗实体，攻击前摇会自动触发攻击动画。
        /// </summary>
        public void Bind(CombatEntity entity)
        {
            combatEntity = entity;

            if (skeletonAnimation == null)
            {
                skeletonAnimation = GetComponent<SkeletonAnimation>();
            }

            if (entity != null)
            {
                _visualBound = true;
                _visualYaw = GetVisualYaw(entity.Facing);
                _appliedYaw = float.NaN;
                ApplyVisualYaw();
            }

            if (skeletonAnimation == null || skeletonAnimation.Skeleton == null)
            {
                return;
            }

            skeletonAnimation.UpdateComplete -= HandleSkeletonUpdated;
            skeletonAnimation.UpdateComplete += HandleSkeletonUpdated;

            Spine.Animation attack = skeletonAnimation.Skeleton.Data.FindAnimation(attackAnimation);
            if (attack != null && entity != null)
            {
                // 至少保证间隔足够播完 Attack，不覆盖配置里更长的基础间隔。
                entity.EnsureAttackIntervalAtLeast(
                    Mathf.CeilToInt(attack.Duration * CombatConstants.TickRate));

                if (entity.Config is OperatorConfig operatorConfig)
                {
                    _useProjectile = operatorConfig.useProjectile;
                    _projectileSpeed = operatorConfig.projectileSpeed;
                    if (_useProjectile)
                    {
                        entity.ConfigureProjectileDamage(true);
                        entity.SetAttackHitNormalizedTime(
                            operatorConfig.attackHitNormalizedTime);
                    }
                    else
                    {
                        entity.ConfigureAnimationDrivenDamage(
                            true,
                            operatorConfig.attackHitNormalizedTime);
                    }
                }
            }

            if (_useProjectile && skeletonAnimation != null && skeletonAnimation.Skeleton != null)
            {
                _hiddenSlotNames = hiddenProjectileSlots != null && hiddenProjectileSlots.Length > 0
                    ? hiddenProjectileSlots
                    : DefaultHiddenProjectileSlots;
            }
        }

        private static float GetVisualYaw(GridFacing facing)
        {
            // 2D 立绘只做左右翻转；上下方向保持朝左。
            // Spine 原始资源默认朝左，因此朝右需要转 180°。
            return facing == GridFacing.Right ? 180f : 0f;
        }

        /// <summary>当前是否正在播放攻击动画。</summary>
        public bool IsAttacking
        {
            get
            {
                if (skeletonAnimation == null || string.IsNullOrEmpty(attackAnimation))
                {
                    return false;
                }

                return skeletonAnimation.AnimationName == attackAnimation;
            }
        }

        private void Awake()
        {
            if (skeletonAnimation == null)
            {
                skeletonAnimation = GetComponent<SkeletonAnimation>();
            }
        }

        private void OnEnable()
        {
            _lastAttackState = ReadAttackState();
            _attackHitTriggered = false;

            if (playIdleOnEnable)
            {
                PlayIdle();
            }
        }

        private void Update()
        {
            if (combatEntity == null)
            {
                return;
            }

            AttackCycleState state = ReadAttackState();

            // 多目标技能期间：不播普攻动画、不走命中帧，按固定间隔直接发射冰锥。
            if (combatEntity.TimedMultiTarget > 1 && combatEntity.SkillVolleyInterval > 0f)
            {
                UpdateSkillVolley();
                UpdateVisualFacing();
                return;
            }

            _volleyTimer = 0f;

            // 只在“进入前摇”的那一帧触发，避免每帧重复设置动画。
            if (state == AttackCycleState.Windup && _lastAttackState != AttackCycleState.Windup)
            {
                _attackHitTriggered = false;
                _projectileFired = false;
                combatEntity.BeginAnimationAttack();
                PlayAttack();
            }

            _lastAttackState = state;

            UpdateVisualFacing();

            if (_useProjectile)
            {
                UpdateProjectileLaunch();
            }
            else if (combatEntity.AnimationDrivenDamage)
            {
                UpdateAnimationDrivenHit();
            }
        }

        /// <summary>多目标技能：按固定间隔向范围内随机敌人发射冰锥，不依赖普攻动画帧。</summary>
        private void UpdateSkillVolley()
        {
            _volleyTimer -= Time.deltaTime;
            if (_volleyTimer > 0f)
            {
                return;
            }

            _volleyTimer = Mathf.Max(0.05f, combatEntity.SkillVolleyInterval);
            _multiTargets.Clear();
            combatEntity.GetRandomTargets(combatEntity.TimedMultiTarget, _multiTargets);
            for (int i = 0; i < _multiTargets.Count; i++)
            {
                ProjectileManager.Spawn(
                    combatEntity,
                    _multiTargets[i],
                    _projectileSpeed,
                    combatEntity.ProjectileTint);
            }
        }

        private void UpdateProjectileLaunch()
        {
            if (_projectileFired || skeletonAnimation == null)
            {
                return;
            }

            Spine.AnimationState state = GetAnimationState();
            TrackEntry entry = state != null ? state.GetCurrent(0) : null;
            if (entry == null || entry.Animation == null)
            {
                return;
            }

            if (entry.Animation.Name != attackAnimation)
            {
                return;
            }

            float launchTime = entry.Animation.Duration
                               * Mathf.Clamp01(combatEntity.AttackHitNormalizedTime);
            if (entry.TrackTime < launchTime)
            {
                return;
            }

            _projectileFired = true;

            // 多目标技能：一次攻击向范围内随机多个敌人各发一发冰锥（带技能染色）。
            int multiTarget = combatEntity.TimedMultiTarget;
            if (multiTarget > 1)
            {
                _multiTargets.Clear();
                combatEntity.GetRandomTargets(multiTarget, _multiTargets);
                for (int i = 0; i < _multiTargets.Count; i++)
                {
                    ProjectileManager.Spawn(
                        combatEntity,
                        _multiTargets[i],
                        _projectileSpeed,
                        combatEntity.ProjectileTint);
                }

                return;
            }

            CombatEntity target = combatEntity.CommittedAnimationTarget
                                  ?? combatEntity.CurrentTarget;
            if (target != null && target.IsAlive)
            {
                ProjectileManager.Spawn(
                    combatEntity,
                    target,
                    _projectileSpeed,
                    combatEntity.ProjectileTint);
            }
        }

        private void LateUpdate()
        {
            // 敌人根节点会随路线转向，视觉子节点必须在根节点移动后重新锁定世界朝向。
            ApplyVisualYaw();
            HideProjectileSlots();
        }

        private void HandleSkeletonUpdated(ISkeletonAnimation animated)
        {
            // 必须在 Spine 应用动画之后、生成 Mesh 之前清 attachment。
            HideProjectileSlots();
        }

        private void OnDisable()
        {
            if (skeletonAnimation != null)
            {
                skeletonAnimation.UpdateComplete -= HandleSkeletonUpdated;
            }
        }

        private void HideProjectileSlots()
        {
            if (!_useProjectile
                || _hiddenSlotNames == null
                || skeletonAnimation == null
                || skeletonAnimation.Skeleton == null)
            {
                return;
            }

            Skeleton skeleton = skeletonAnimation.Skeleton;

            // Skeleton 重建后 Slot 引用会失效，只在引用变化时重新查找一次。
            if (_cachedHiddenSlots == null || _cachedSlotSkeleton != skeleton)
            {
                _cachedSlotSkeleton = skeleton;
                _cachedHiddenSlots = new Slot[_hiddenSlotNames.Length];
                for (int i = 0; i < _hiddenSlotNames.Length; i++)
                {
                    _cachedHiddenSlots[i] = skeleton.FindSlot(_hiddenSlotNames[i]);
                }
            }

            for (int i = 0; i < _cachedHiddenSlots.Length; i++)
            {
                Slot slot = _cachedHiddenSlots[i];
                if (slot != null && slot.Attachment != null)
                {
                    slot.Attachment = null;
                }
            }
        }

        private void ApplyVisualYaw()
        {
            if (!_visualBound)
            {
                return;
            }

            // 朝向只在左右切换时变化；每帧重复写 transform 会带来无意义的层级脏标记。
            if (!float.IsNaN(_appliedYaw) && Mathf.Approximately(_appliedYaw, _visualYaw))
            {
                return;
            }

            _appliedYaw = _visualYaw;
            transform.rotation = Quaternion.Euler(0f, _visualYaw, 0f);
        }

        private void UpdateVisualFacing()
        {
            if (combatEntity == null || combatEntity.CurrentTarget == null)
            {
                return;
            }

            int deltaX = combatEntity.CurrentTarget.GridPosition.x
                         - combatEntity.GridPosition.x;
            if (deltaX == 0)
            {
                // 上下方向保持上一次的左右朝向，避免 2D 立绘旋转到几乎不可见。
                return;
            }

            _visualYaw = deltaX < 0 ? 0f : 180f;
        }

        private void UpdateAnimationDrivenHit()
        {
            if (_attackHitTriggered || skeletonAnimation == null)
            {
                return;
            }

            Spine.AnimationState state = GetAnimationState();
            TrackEntry entry = state != null ? state.GetCurrent(0) : null;
            if (entry == null || entry.Animation == null)
            {
                return;
            }

            if (entry.Animation.Name != attackAnimation)
            {
                return;
            }

            float hitTime = entry.Animation.Duration
                            * Mathf.Clamp01(combatEntity.AttackHitNormalizedTime);
            if (entry.TrackTime < hitTime)
            {
                return;
            }

            _attackHitTriggered = true;

            // 攻击已经开始就播完；目标离开范围也继续结算，只要目标仍存活。
            combatEntity.PerformAttackFromAnimation();
        }

        /// <summary>播放待机动画（循环）。</summary>
        public void PlayIdle()
        {
            Spine.AnimationState state = GetAnimationState();
            if (state == null || string.IsNullOrEmpty(idleAnimation))
            {
                return;
            }

            state.SetAnimation(0, idleAnimation, true);
        }

        /// <summary>播放一次攻击动画，结束后自动回到待机。</summary>
        public void PlayAttack()
        {
            Spine.AnimationState state = GetAnimationState();
            if (state == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(attackAnimation))
            {
                return;
            }

            state.SetAnimation(0, attackAnimation, false);

            if (!string.IsNullOrEmpty(idleAnimation))
            {
                // 参数 0 表示紧接在攻击动画之后，避免出现空白帧。
                state.AddAnimation(0, idleAnimation, true, 0f);
            }
        }

        /// <summary>播放任意动画名，回到待机由调用方决定。</summary>
        public void Play(string animationName, bool loop)
        {
            Spine.AnimationState state = GetAnimationState();
            if (state == null || string.IsNullOrEmpty(animationName))
            {
                return;
            }

            state.SetAnimation(0, animationName, loop);
        }

        private Spine.AnimationState GetAnimationState()
        {
            if (skeletonAnimation == null)
            {
                skeletonAnimation = GetComponent<SkeletonAnimation>();
            }

            return skeletonAnimation != null ? skeletonAnimation.state : null;
        }

        private AttackCycleState ReadAttackState()
        {
            if (combatEntity == null || combatEntity.AttackCycle == null)
            {
                return AttackCycleState.Ready;
            }

            return combatEntity.AttackCycle.State;
        }
    }
}
