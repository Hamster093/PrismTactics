/****************************************************
    文件：GridTypes.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：格子基础类型：地形与可序列化坐标。
*****************************************************/

using System;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 地形类型枚举。
    /// 注意：Ground 是唯一可走地形（见 BattleMap.IsWalkable / GridPathfinder），
    ///       HighGround 不可走但可站立（有独立地表高度），Void 是越界/虚空哨兵值。
    ///       本枚举没有显式赋值，序列化/网络传输时依赖"顺序不变"的隐式契约——插值或调整顺序会破坏旧存档/旧配置。
    /// </summary>
    public enum TerrainType
    {
        Ground,//普通地面
        HighGround,//高台
        Void//虚空
    }
    /// <summary>
    /// 可序列化的格子坐标：用于 JSON 配置（MapCellDefinition / RouteDefinition 等）。
    ///
    /// 为什么不用 Vector2Int：Unity 的 Vector2Int 字段名是 x/y，可序列化，
    /// 但作为配置数据结构时用自定义 struct 更可控（不受 Unity 版本/序列化规则变化影响），
    /// 且能显式提供 ToVector2Int / From 转换。
    ///
    /// 注意：字段是 public 且非 readonly，是 Unity 序列化/JSON 反序列化的要求；
    ///       作为值类型，默认构造得到 (0, 0)，与"未配置"无法区分——配置缺失时会被当成原点格。
    /// </summary>
    [Serializable]
    public struct GridPoint
    {
        public int x;
        public int y;

        public GridPoint(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
        /// <summary>转成 Vector2Int，用于运行时格子运算。</summary>
        public Vector2Int ToVector2Int()
        {
            return new Vector2Int(x, y);
        }
        /// <summary>
        /// 从 Vector2Int 构造 GridPoint，用于反向转换（运行时 → 配置）。
        /// 注意：命名是 From 而非构造函数重载，避免与 (int, int) 构造混淆；
        ///       这是工厂方法，不是隐式转换——调用方需显式写 GridPoint.From(v)。
        /// </summary>
        public static GridPoint From(Vector2Int value)
        {
            return new GridPoint(value.x, value.y);
        }
    }
    /// <summary>
    /// 格子朝向工具：GridFacing 枚举与方向向量、旋转、中文名之间的转换。
    ///
    /// 职责边界：
    /// - 本类只管"朝向 ↔ 向量 / 旋转 / 显示名"的纯函数转换，不持有状态、不依赖场景；
    /// - GridFacing 枚举本身定义在别处（PrismTactics.Combat），本类只做转换。
    ///
    /// 设计取舍：
    /// - 全部用 switch 而非字典/数组：方向只有 4 个，switch 编译成跳转表，比字典查找快且无分配；
    /// - 每个方法都用 default 兜底到 Right：枚举若传入非法值（如强转 99），不会抛异常而是静默当 Right。
    ///   这是防御性写法，但也意味着非法朝向不会被发现——如果希望严格校验，应改为抛异常或返回可空。
    /// </summary>
    public static class GridFacingUtility
    {
        #region 朝向转换
        /// <summary>
        /// 朝向 → 四方向单位向量。
        /// 注意：default 分支返回 Vector2Int.right，即"未知朝向当右"；非法枚举值不会报错。
        /// </summary>
        public static Vector2Int ToDirection(GridFacing facing)
        {
            switch (facing)
            {
                case GridFacing.Up:
                    return Vector2Int.up;
                case GridFacing.Left:
                    return Vector2Int.left;
                case GridFacing.Down:
                    return Vector2Int.down;
                default:
                    return Vector2Int.right;
            }
        }
        /// <summary>
        /// 顺时针旋转 90 度：右→下→左→上→右。
        /// 注意：default 分支返回 Right，非法枚举值会被"旋转"成 Right，而不是保持原值或报错。
        /// 注意：与 ToDirection 的 default 语义不同——这里 default 是"旋转结果"，不是"方向向量"。
        /// </summary>
        public static GridFacing RotateClockwise(GridFacing facing)
        {
            switch (facing)
            {
                case GridFacing.Right:
                    return GridFacing.Down;
                case GridFacing.Down:
                    return GridFacing.Left;
                case GridFacing.Left:
                    return GridFacing.Up;
                default:
                    return GridFacing.Right;
            }
        }
        /// <summary>
        /// 朝向 → 中文显示名（上/左/下/右）。
        /// 注意：中文硬编码在代码里，未走本地化表；若后续要做多语言，这里需要改为查表。
        /// 注意：default 分支返回"右"，非法枚举值显示为"右"，不会暴露配置错误。
        /// </summary>
        public static string ToChinese(GridFacing facing)
        {
            switch (facing)
            {
                case GridFacing.Up:
                    return "上";
                case GridFacing.Left:
                    return "左";
                case GridFacing.Down:
                    return "下";
                default:
                    return "右";
            }
        }
        #endregion
    }
}
