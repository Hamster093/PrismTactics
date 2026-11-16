/****************************************************
    文件：CombatInterfaces.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：战斗核心接口：可受伤、可索敌、攻击循环宿主与格子空间查询。
*****************************************************/

using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Combat
{// 可受伤对象：提供阵营/生命/属性与受伤入口
    public interface IDamageable
    {
        Faction Faction { get; }
        bool IsAlive { get; }
        float CurrentHealth { get; }
        float MaxHealth { get; }
        StatBlock Stats { get; }
        void TakeDamage(in DamageResult result);
    }
    // 可被索敌对象：在 IDamageable 基础上提供判定位置
    public interface ITargetable : IDamageable
    {
        Vector3 TargetPosition { get; }
    }
    // 攻击循环宿主：由攻击循环回调，判断目标并执行攻击
    public interface IAttackCycleOwner
    {
        bool HasValidTarget();
        void PerformAttack();
    }

    /// <summary>
    /// 战斗层只依赖这个接口，不直接依赖 Battle 程序集里的格子实现。
    /// M0 没有格子时可以为空，走世界距离回退。
    /// </summary>
    public interface ICombatSpace
    {
        // 收集 source 攻击范围内的敌人
        void GetTargetsInRange(CombatEntity source, List<CombatEntity> results);
        // 用技能指定的范围覆盖普通攻击范围，收集敌人
        void GetTargetsInRange(CombatEntity source, Vector2Int[] rangeCells, List<CombatEntity> results);
        // 收集 source 范围内的友军，供治疗技能使用
        void GetAlliesInRange(CombatEntity source, Vector2Int[] rangeCells, List<CombatEntity> results);
        // 判断 target 是否在 source 攻击范围内
        bool IsInRange(CombatEntity source, CombatEntity target);
    }
}
