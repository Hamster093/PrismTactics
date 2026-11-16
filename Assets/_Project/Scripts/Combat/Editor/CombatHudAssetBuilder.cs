/****************************************************
    文件：CombatHudAssetBuilder.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 15:30:00
	功能：生成血条/技力条和伤害数字的 Prefab 与材质占位资产。
*****************************************************/

using System.IO;
using PrismTactics.Battle;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PrismTactics.EditorTools
{
    public static class CombatHudAssetBuilder
    {
        private const string ResourceRoot = "Assets/Resources";
        private const string MaterialRoot = "Assets/Resources/CombatBars/Materials";
        private const string HealthBarPath = ResourceRoot + "/HealthBar.prefab";
        private const string DamageNumberPath = ResourceRoot + "/DamageNumber.prefab";

        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.4 生成 Combat HUD 资源")]
        public static void Build()
        {
            EnsureFolder(MaterialRoot);

            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite == null)
            {
                Debug.LogError("找不到内置 UISprite，无法生成血条 Prefab。");
                return;
            }

            Material backgroundMaterial = CreateMaterial(
                "BarBackground",
                Shader.Find("Sprites/Default"),
                Color.white);
            Material fillMaterial = CreateMaterial(
                "BarFill",
                Shader.Find("PrismTactics/SpriteFill"),
                Color.white);

            if (backgroundMaterial == null || fillMaterial == null)
            {
                Debug.LogError("血条 Shader/材质生成失败。");
                return;
            }

            GameObject root = new GameObject("HealthBar");
            HealthBarView healthView = root.AddComponent<HealthBarView>();
            SpCostBarView spView = root.AddComponent<SpCostBarView>();

            CreateBarRow(
                root.transform,
                "HealthBackground",
                0.06f,
                0.96f,
                0.08f,
                new Color(0.08f, 0.08f, 0.08f, 0.9f),
                backgroundMaterial,
                sprite,
                20,
                false);

            FillBarRenderer healthFill = CreateBarRow(
                root.transform,
                "HealthFill",
                0.06f,
                0.96f,
                0.08f,
                new Color(0.9f, 0.16f, 0.16f, 1f),
                fillMaterial,
                sprite,
                21,
                true);

            GameObject spRoot = new GameObject("SpBar");
            spRoot.transform.SetParent(root.transform, false);

            CreateBarRow(
                spRoot.transform,
                "SpBackground",
                -0.03f,
                0.8f,
                0.06f,
                new Color(0.08f, 0.08f, 0.08f, 0.9f),
                backgroundMaterial,
                sprite,
                20,
                false);

            FillBarRenderer spFill = CreateBarRow(
                spRoot.transform,
                "SpFill",
                -0.03f,
                0.8f,
                0.06f,
                new Color(0.25f, 0.9f, 0.42f, 1f),
                fillMaterial,
                sprite,
                21,
                true);

            SetObject(healthView, "fill", healthFill);
            SetObject(spView, "fill", spFill);
            SetObject(spView, "spRoot", spRoot);

            PrefabUtility.SaveAsPrefabAsset(root, HealthBarPath);
            Object.DestroyImmediate(root);

            CreateDamageNumberPrefab();
            CreateProjectilePrefab(sprite);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CombatHud] 血条和伤害数字 Prefab 已生成。");
        }

        private static FillBarRenderer CreateBarRow(
            Transform parent,
            string name,
            float y,
            float width,
            float height,
            Color color,
            Material material,
            Sprite sprite,
            int sortingOrder,
            bool isFill)
        {
            var row = new GameObject(name);
            row.transform.SetParent(parent, false);
            row.transform.localPosition = new Vector3(0f, y, 0f);
            // UISprite 原始宽度为 0.16，用缩放保持用户样例约 0.96 宽。
            row.transform.localScale = new Vector3(width / 0.16f, height / 0.16f, 1f);

            SpriteRenderer renderer = row.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (!isFill)
            {
                return null;
            }

            FillBarRenderer fill = row.AddComponent<FillBarRenderer>();
            SetObject(fill, "fillRenderer", renderer);
            SetFloat(fill, "fillDirection", 0f);
            fill.SetFill(1f);
            return fill;
        }

        private static void CreateDamageNumberPrefab()
        {
            var root = new GameObject(
                "DamageNumber",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text),
                typeof(CanvasGroup),
                typeof(DamageNumberView));

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(120f, 50f);

            Text text = root.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            DamageNumberView view = root.GetComponent<DamageNumberView>();
            SetObject(view, "text", text);
            SetObject(view, "canvasGroup", group);

            PrefabUtility.SaveAsPrefabAsset(root, DamageNumberPath);
            Object.DestroyImmediate(root);
        }

        private static void CreateProjectilePrefab(Sprite sprite)
        {
            Material material = CreateMaterial(
                "Projectile",
                Shader.Find("Sprites/Default"),
                Color.white);
            if (material == null)
            {
                return;
            }

            var root = new GameObject("Projectile");
            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = new Color(1f, 0.62f, 0.18f, 1f);
            renderer.sortingOrder = 22;
            renderer.transform.localScale = Vector3.one * 0.16f;
            root.AddComponent<ProjectileView>();

            PrefabUtility.SaveAsPrefabAsset(root, ResourceRoot + "/Projectile.prefab");
            Object.DestroyImmediate(root);
        }

        private static Material CreateMaterial(string name, Shader shader, Color color)
        {
            if (shader == null)
            {
                return null;
            }

            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetObject(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
