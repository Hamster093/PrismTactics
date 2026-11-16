/****************************************************
    文件：MapVisualAssetBuilder.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 13:00:00
	功能：生成地图格子 Prefab、材质与 MapVisualConfig 示例资产。
*****************************************************/

using System.IO;
using PrismTactics.Battle;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    public static class MapVisualAssetBuilder
    {
        private const string RootFolder = "Assets/GameContent/MapVisual";
        private const string PrefabFolder = RootFolder + "/Prefabs";
        private const string MaterialFolder = RootFolder + "/Materials";
        private const string ConfigPath = "Assets/GameContent/MapVisualConfig.asset";

        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.5 生成地图视觉资源")]
        public static void Build()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            Material groundMaterial = CreateMaterial(
                "MapGround",
                new Color(0.16f, 0.18f, 0.21f));
            Material highMaterial = CreateMaterial(
                "MapHighGround",
                new Color(0.3f, 0.32f, 0.36f));
            Material baseMaterial = CreateMaterial(
                "MapBase",
                new Color(0.18f, 0.45f, 0.9f));
            Material spawnMaterial = CreateMaterial(
                "MapSpawn",
                new Color(0.9f, 0.22f, 0.18f));
            Material backgroundMaterial = CreateMaterial(
                "MapBackground",
                new Color(0.04f, 0.05f, 0.07f));

            GameObject ground = CreateCellPrefab(
                "MapCell_Ground",
                0.1f,
                groundMaterial);
            GameObject high = CreateCellPrefab(
                "MapCell_HighGround",
                0.4f,
                highMaterial);
            GameObject baseCell = CreateCellPrefab(
                "MapCell_Base",
                0.1f,
                baseMaterial);
            GameObject spawn = CreateCellPrefab(
                "MapCell_Spawn",
                0.1f,
                spawnMaterial);
            GameObject background = CreateBackgroundPrefab(backgroundMaterial);

            MapVisualConfig config = AssetDatabase.LoadAssetAtPath<MapVisualConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MapVisualConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            config.groundCellPrefab = ground;
            config.highGroundCellPrefab = high;
            config.baseCellPrefab = baseCell;
            config.spawnCellPrefab = spawn;
            config.backgroundPrefab = background;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MapVisual] 地图表现资产已生成：" + ConfigPath);
        }

        private static GameObject CreateCellPrefab(string name, float height, Material material)
        {
            var root = new GameObject(name);
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Visual";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = new Vector3(0f, -height * 0.5f, 0f);
            cube.transform.localScale = new Vector3(0.94f, height, 0.94f);

            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            cube.GetComponent<Renderer>().sharedMaterial = material;
            return SavePrefab(root, PrefabFolder + "/" + name + ".prefab");
        }

        private static GameObject CreateBackgroundPrefab(Material material)
        {
            var root = new GameObject("MapBackground");
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Visual";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = Vector3.zero;
            cube.transform.localScale = new Vector3(1f, 0.02f, 1f);

            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            cube.GetComponent<Renderer>().sharedMaterial = material;
            return SavePrefab(root, PrefabFolder + "/MapBackground.prefab");
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                }

                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                if (shader == null)
                {
                    Debug.LogError("找不到可用于地图格子的 Shader。");
                    return null;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
    }
}
