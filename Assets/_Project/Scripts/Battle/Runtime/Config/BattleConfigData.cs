/****************************************************
    文件：BattleConfigData.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：地图、敌人、干员、波次与战斗参数的 JSON 数据结构。
*****************************************************/

using System;
using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    [Serializable]
    public class MapCellDefinition
    {
        public int x;
        public int y;
        public TerrainType terrain;
        public bool deployable = true;
    }

    [Serializable]
    public class SpawnPointDefinition
    {
        public int x;
        public int y;
        public string routeId = "route_default";

        public GridPoint ToPoint()
        {
            return new GridPoint(x, y);
        }
    }

    [Serializable]
    public class RouteDefinition
    {
        public string id = "route_default";
        public List<GridPoint> points = new List<GridPoint>();
    }

    [Serializable]
    public class MapDefinition
    {
        public string mapId = "M1_Test";
        public int width = 12;
        public int height = 6;
        public float groundSurfaceY = 0.12f;
        public float highGroundSurfaceY = 0.42f;
        public GridPoint baseCell = new GridPoint(0, 3);
        public List<MapCellDefinition> cells = new List<MapCellDefinition>();
        public List<SpawnPointDefinition> spawnPoints = new List<SpawnPointDefinition>();
        public List<RouteDefinition> routes = new List<RouteDefinition>();
    }

    [Serializable]
    public class EnemyDefinition
    {
        public int id = 1;
        public string displayName = "普通敌人";
        public float maxHealth = 60f;
        public float attack = 8f;
        public float defense;
        public float magicResist;
        public int attackIntervalTicks = 40;
        public int windupTicks = 12;
        public int damageType;
        public float baseDamage;
        public float attackScale = 1f;
        public float moveSpeed = 1.5f;
        public int blockWeight = 1;
        public int lifeDamage = 1;
        // 进战对象池预热数量：<=1 表示不预热；只对当前关卡波次里出现的敌人生效。
        public int prewarmCount = 10;
        public float colorR = 1f;
        public float colorG = 0.3f;
        public float colorB = 0.2f;
    }

    [Serializable]
    public class EnemyLibrary
    {
        public List<EnemyDefinition> enemies = new List<EnemyDefinition>();
    }

    [Serializable]
    public class OperatorDefinition
    {
        public string id = "guard";
        public string displayName = "禁卫";
        public float maxHealth = 120f;
        public float attack = 18f;
        public float defense = 5f;
        public float magicResist;
        public int attackIntervalTicks = 30;
        public int windupTicks = 9;
        public float attackHitNormalizedTime = 0.6f;
        public bool useProjectile;
        public float projectileSpeed = 4f;
        public int blockCount = 2;
        public int deployCost = 5;
        public bool canDeployOnHighGround = true;
        public bool requiresHighGround;
        public bool isHealer;
        public List<string> skillIds = new List<string>();
        public int targetSelector;
        public int damageType;
        public float baseDamage;
        public float attackScale = 1f;
        public List<GridPoint> rangeCells = new List<GridPoint>();
        public float colorR = 0.2f;
        public float colorG = 0.6f;
        public float colorB = 1f;
    }

    [Serializable]
    public class OperatorLibrary
    {
        public List<OperatorDefinition> operators = new List<OperatorDefinition>();
    }

    [Serializable]
    public class SkillDefinition
    {
        public string id = "skill";
        public string displayName = "技能";
        public string description = string.Empty;
        public string effectKey = string.Empty;
        public int activationType;
        public int spRecoveryType;
        public int spCost = 10;
        public int initialSp;
        public float spPerSecond = 1f;
        public int effectType;
        public int durationTicks;
        public int damageType;
        public float damageMultiplier = 1f;
        public int hitCount = 1;
        public int extraHits;
        public float attackPercent;
        public float attackSpeedPercent;
        public float attackIntervalReducePercent;
        public int multiTargetCount;
        public Color projectileTint = Color.white;
        public float healPercent;
        public bool attacksAllInRange;
        public bool requiresEnemy;
        public List<GridPoint> rangeCells = new List<GridPoint>();
    }

    [Serializable]
    public class SkillLibrary
    {
        public List<SkillDefinition> skills = new List<SkillDefinition>();
    }

    [Serializable]
    public class ItemDefinition
    {
        public string id = "item";
        public string displayName = "道具";
        public string description = string.Empty;
        // 效果处理器 key：AOT 内置或热更 DLL 注册
        public string effectKey = string.Empty;
        // 每场战斗的可用次数
        public int charges = 1;
        // 作用半径：1 表示 3x3
        public int radius = 1;
        public float healPercent = 0.3f;
        public float durationSeconds = 20f;
        public float slowPercent = 0.5f;
        public float colorR = 0.25f;
        public float colorG = 0.85f;
        public float colorB = 0.45f;
    }

    [Serializable]
    public class ItemLibrary
    {
        public List<ItemDefinition> items = new List<ItemDefinition>();
    }

    [Serializable]
    public class WaveSpawnDefinition
    {
        public int enemyId = 1;
        public string routeId = "route_default";
        public int count = 1;
        public float spawnTimeSeconds;
        public float intervalSeconds;
    }

    [Serializable]
    public class WaveDefinition
    {
        public int waveNumber = 1;
        public float delaySeconds = 1f;
        public List<WaveSpawnDefinition> spawns = new List<WaveSpawnDefinition>();
    }

    [Serializable]
    public class WaveLibrary
    {
        public List<WaveDefinition> waves = new List<WaveDefinition>();
    }

    [Serializable]
    public class BattleSettings
    {
        public int initialLife = 3;
        public int startingCost = 10;
        public float costRegenPerSecond = 1f;
        public int maxCost = 99;
    }

    public enum BattleState
    {
        Running,
        Victory,
        Defeat
    }

    public static class BattleConfigIds
    {
        public const string Map = "map";
        public const string Enemies = "enemies";
        public const string Operators = "operators";
        public const string Skills = "skills";
        public const string Items = "items";
        public const string Waves = "waves";
        public const string Battle = "battle";
    }
}
