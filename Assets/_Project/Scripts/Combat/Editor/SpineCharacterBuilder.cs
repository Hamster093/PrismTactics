/****************************************************
    文件：SpineCharacterBuilder.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 14:20:00
	功能：把 SpineAssets 里的角色生成可复用预设，并生成一个待机/攻击演示场景。
*****************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PrismTactics.Battle;
using Spine.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrismTactics.EditorTools
{
    public static class SpineCharacterBuilder
    {
        private const string CharacterRoot = "Assets/_Project/SpineAssets";
        private const string PrefabFolderName = "Prefabs";
        private const string PrefabRoot = CharacterRoot + "/" + PrefabFolderName;
        private const string DemoScenePath = "Assets/_Project/Scenes/SpineDemo.unity";
        private const string RequestFile = "Library/SpineBuild.request";
        private const string ResultFile = "Library/SpineBuild.result.txt";

        private const string PreferredIdle = "Idle";
        private const string FallbackIdle = "Walk";
        private const string AttackAnimation = "Attack";

        private const string CameraObjectName = "Main Camera";
        private const string DriverObjectName = "Spine Demo Driver";

        // 演示场景布局
        private const int CharactersPerRow = 5;
        private const float Spacing = 1.2f;
        private const float CharacterScale = 0.5f;
        private const float CameraSize = 2.4f;

        // 各角色骨架原始尺寸差别很大（Boss 约 1.7~2.9 单位，Normal 只有 0.9 单位），
        // 演示场景里把每个角色等比缩放到同一个框内，保证截图里 9 个角色大小接近且不互相压住。
        private const float DemoCharacterHeight = 1.5f;
        private const float DemoCharacterWidth = 1.15f;
        private static readonly float[] RowY = { 0.45f, -1.25f };

        /// <summary>
        /// 一个角色 = 资源目录名 + 骨架数据文件名。
        /// 目录名用友好英文名（同时也是预设名），骨架数据文件名沿用美术导出的 Monster_* 名。
        /// 加新角色就往这里加一行，然后跑一次生成菜单。
        /// </summary>
        private struct CharacterEntry
        {
            public readonly string Folder;
            public readonly string DataName;

            public CharacterEntry(string folder, string dataName)
            {
                Folder = folder;
                DataName = dataName;
            }
        }

        private static readonly CharacterEntry[] Characters =
        {
            new CharacterEntry("frost_queen", "Monster_Boss_1010"),
            new CharacterEntry("flame_mage", "Monster_Boss_1014"),
            new CharacterEntry("red_knight", "Monster_Boss_1028"),
            new CharacterEntry("black_knight", "Monster_Boss_1029"),
            new CharacterEntry("white_knight", "Monster_Boss_1030"),
            new CharacterEntry("slime", "Monster_Normal_1"),
            new CharacterEntry("stone_bone", "Monster_Normal_12"),
            new CharacterEntry("lava_monster", "Monster_Normal_32"),
            new CharacterEntry("iceflame_skeleton", "Monster_Normal_46"),
            new CharacterEntry("gold_slime", "Monster_Normal_2"),
        };

        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.6 生成 Spine 角色预设")]
        public static void BuildPrefabsFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Spine] 请先退出播放模式再生成。");
                return;
            }

            BuildAndReport(false);
        }

        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.7 生成 Spine 预设与演示场景")]
        public static void BuildFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Spine] 请先退出播放模式再生成。");
                return;
            }

            BuildAndReport(true);
        }

        /// <summary>
        /// 一次性触发器：存在 Library/SpineBuild.request 时自动执行一次生成，
        /// 避免需要手动点菜单。跑完会写 Library/SpineBuild.result.txt。
        /// </summary>
        // 请求文件随时可能出现（不一定会伴随一次域重载），所以这里除了域重载时试一次，
        // 还在 update 里低频轮询，保证任何时刻丢进去的请求都会被处理。
        private const int PollIntervalFrames = 120;

        [InitializeOnLoadMethod]
        private static void BuildOnRequest()
        {
            EditorApplication.delayCall += TryBuildFromRequest;
            EditorApplication.update -= PollBuildRequest;
            EditorApplication.update += PollBuildRequest;
        }

        private static void PollBuildRequest()
        {
            if (++pollFrameCounter < PollIntervalFrames)
            {
                return;
            }

            pollFrameCounter = 0;
            TryBuildFromRequest();
        }

        private static int pollFrameCounter;

        private static void TryBuildFromRequest()
        {
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string request = Path.Combine(Directory.GetCurrentDirectory(), RequestFile);
            if (!File.Exists(request))
            {
                return;
            }

            // 文件内容 = 只生成这个目录名对应的角色；留空 = 全部生成。
            string onlyFolder = string.Empty;
            try
            {
                onlyFolder = File.ReadAllText(request).Trim();
            }
            catch (Exception)
            {
                onlyFolder = string.Empty;
            }

            try
            {
                File.Delete(request);
            }
            catch (Exception)
            {
                // 文件被占用，下一轮轮询再试
                return;
            }

            BuildAndReport(false, onlyFolder);
        }

        private static void BuildAndReport(bool includeDemoScene, string onlyFolder = null)
        {
            var report = new StringBuilder();
            int exitCode = 0;

            try
            {
                Build(report, includeDemoScene, onlyFolder);
            }
            catch (Exception exception)
            {
                exitCode = 1;
                report.AppendLine("EXCEPTION: " + exception);
                Debug.LogError("[Spine] 生成失败：" + exception);
            }

            report.AppendLine("EXIT=" + exitCode);

            try
            {
                File.WriteAllText(
                    Path.Combine(Directory.GetCurrentDirectory(), ResultFile),
                    report.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Spine] 写结果文件失败：" + exception.Message);
            }
        }

        private static void Build(StringBuilder report, bool includeDemoScene, string onlyFolder)
        {
            EnsureFolder();

            var prefabs = new List<GameObject>();

            for (int i = 0; i < Characters.Length; i++)
            {
                CharacterEntry entry = Characters[i];

                if (!string.IsNullOrEmpty(onlyFolder)
                    && !string.Equals(entry.Folder, onlyFolder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SkeletonDataAsset data = LoadSkeletonData(entry, report);
                prefabs.Add(data != null ? CreateCharacterPrefab(entry, data, report) : null);
            }

            AssetDatabase.SaveAssets();

            if (prefabs.Count == 0)
            {
                report.AppendLine("NO_CHARACTER_MATCHED=" + (onlyFolder ?? string.Empty));
                Debug.LogError("[Spine] 没有匹配到角色：" + onlyFolder);
                return;
            }

            if (!includeDemoScene)
            {
                report.AppendLine("DEMO_SCENE_SKIPPED=True");
                return;
            }

            BuildDemoScene(prefabs, report);
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(CharacterRoot))
            {
                throw new InvalidOperationException("找不到目录：" + CharacterRoot);
            }

            if (!AssetDatabase.IsValidFolder(PrefabRoot))
            {
                AssetDatabase.CreateFolder(CharacterRoot, PrefabFolderName);
            }
        }

        private static SkeletonDataAsset LoadSkeletonData(CharacterEntry entry, StringBuilder report)
        {
            string dataPath = CharacterRoot + "/" + entry.Folder + "/" + entry.DataName + "_SkeletonData.asset";
            SkeletonDataAsset data = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(dataPath);

            if (data == null)
            {
                report.AppendLine("MISSING_SKELETON_DATA=" + dataPath);
                Debug.LogError("[Spine] 找不到 SkeletonDataAsset：" + dataPath);
                return null;
            }

            return data;
        }

        /// <summary>
        /// 选一个当待机循环用的动画：优先 Idle，其次 Walk，最后取第一条动画。
        /// （Monster_Normal_46 没有 Idle，只有 Attack / Die / Walk）
        /// </summary>
        private static string ResolveIdleAnimation(SkeletonDataAsset data)
        {
            Spine.SkeletonData skeletonData = data.GetSkeletonData(true);
            if (skeletonData == null || skeletonData.Animations.Count == 0)
            {
                return PreferredIdle;
            }

            if (skeletonData.FindAnimation(PreferredIdle) != null)
            {
                return PreferredIdle;
            }

            if (skeletonData.FindAnimation(FallbackIdle) != null)
            {
                return FallbackIdle;
            }

            return skeletonData.Animations.Items[0].Name;
        }

                private static GameObject CreateCharacterPrefab(
            CharacterEntry entry,
            SkeletonDataAsset data,
            StringBuilder report)
        {
            string prefabPath = PrefabRoot + "/" + entry.Folder + ".prefab";
            string idleAnimation = ResolveIdleAnimation(data);

            var go = new GameObject(entry.Folder);

            SkeletonAnimation skeletonAnimation = go.AddComponent<SkeletonAnimation>();
            skeletonAnimation.skeletonDataAsset = data;

            MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                var materials = new List<Material>();
                AtlasAssetBase[] atlases = data.atlasAssets;
                if (atlases != null)
                {
                    for (int i = 0; i < atlases.Length; i++)
                    {
                        AtlasAssetBase atlas = atlases[i];
                        if (atlas == null)
                        {
                            continue;
                        }

                        foreach (Material material in atlas.Materials)
                        {
                            if (material != null)
                            {
                                materials.Add(material);
                            }
                        }
                    }
                }

                meshRenderer.sharedMaterials = materials.ToArray();
            }

            // 序列化字段：初始动画 = 待机动画且循环
            var skeletonSo = new SerializedObject(skeletonAnimation);
            skeletonSo.FindProperty("_animationName").stringValue = idleAnimation;
            skeletonSo.FindProperty("loop").boolValue = true;
            skeletonSo.FindProperty("timeScale").floatValue = 1f;
            skeletonSo.ApplyModifiedPropertiesWithoutUndo();

            SpineUnitView view = go.AddComponent<SpineUnitView>();
            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("skeletonAnimation").objectReferenceValue = skeletonAnimation;
            viewSo.FindProperty("idleAnimation").stringValue = idleAnimation;
            viewSo.FindProperty("attackAnimation").stringValue = AttackAnimation;
            viewSo.FindProperty("playIdleOnEnable").boolValue = true;
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);

            // 预设存盘后，场景里这个临时对象就删掉（不保存场景，不会弄脏你的工程）
            UnityEngine.Object.DestroyImmediate(go);

            report.AppendLine(
                "PREFAB=" + entry.Folder
                + " idle=" + idleAnimation
                + " ok=" + (prefab != null));
            return prefab;
        }

        private static void BuildDemoScene(List<GameObject> prefabs, StringBuilder report)
        {
            bool sceneExists = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(DemoScenePath));
            Scene demo = SceneManager.GetSceneByPath(DemoScenePath);
            bool alreadyOpen = demo.IsValid() && demo.isLoaded;

            if (!alreadyOpen)
            {
                demo = sceneExists
                    ? EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }

            report.AppendLine("DEMO_OPEN=" + alreadyOpen + " existed=" + sceneExists);

            try
            {
                // 只清掉「我们自己生成的」对象（相机、演示驱动器、本目录下的角色实例），
                // 场景里其它东西（比如手加的 Canvas / UI）原样保留。
                foreach (GameObject root in demo.GetRootGameObjects())
                {
                    if (!IsManagedRoot(root))
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(root);
                }

                CreateCamera(demo);

                var instances = new List<GameObject>();
                var views = new List<SpineUnitView>();

                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (prefabs[i] == null)
                    {
                        continue;
                    }

                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], demo);
                    instance.transform.localScale = Vector3.one * ResolveDemoScale(instance);
                    instances.Add(instance);

                    SpineUnitView view = instance.GetComponent<SpineUnitView>();
                    if (view != null)
                    {
                        views.Add(view);
                    }
                }

                LayoutInstances(instances);

                for (int i = 0; i < instances.Count; i++)
                {
                    Vector3 position = instances[i].transform.localPosition;
                    report.AppendLine("INSTANCE=" + instances[i].name + " x=" + position.x + " y=" + position.y);
                }

                CreateDemoDriver(demo, views);

                EditorSceneManager.MarkSceneDirty(demo);
                bool saved = EditorSceneManager.SaveScene(demo, DemoScenePath);
                report.AppendLine("DEMO_SCENE_SAVED=" + saved + " path=" + DemoScenePath);
                Debug.Log("[Spine] 演示场景已生成：" + DemoScenePath);
            }
            finally
            {
                if (!alreadyOpen && demo.IsValid())
                {
                    EditorSceneManager.CloseScene(demo, true);
                }
            }
        }

        /// <summary>
        /// 判断场景根对象是不是本工具生成的（可以安全删掉重建）。
        /// </summary>
        private static bool IsManagedRoot(GameObject root)
        {
            if (root.name == CameraObjectName || root.name == DriverObjectName)
            {
                return true;
            }

            if (PrefabUtility.GetPrefabAssetType(root) == PrefabAssetType.NotAPrefab)
            {
                return false;
            }

            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            return !string.IsNullOrEmpty(prefabPath)
                && prefabPath.StartsWith(PrefabRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 按骨架的原始宽高算出演示场景里的缩放，让所有角色看起来大小接近且不互相压住。
        /// 只影响演示场景里的实例，预设本身仍是原始尺寸。
        /// 注意：SkeletonData.Width/Height 是 json 里的原始像素值，要乘 SkeletonDataAsset.scale 才是世界单位。
        /// </summary>
        private static float ResolveDemoScale(GameObject instance)
        {
            SkeletonAnimation skeleton = instance.GetComponent<SkeletonAnimation>();
            if (skeleton == null || skeleton.skeletonDataAsset == null)
            {
                return CharacterScale;
            }

            SkeletonDataAsset dataAsset = skeleton.skeletonDataAsset;
            Spine.SkeletonData data = dataAsset.GetSkeletonData(true);
            if (data == null)
            {
                return CharacterScale;
            }

            float worldWidth = data.Width * dataAsset.scale;
            float worldHeight = data.Height * dataAsset.scale;
            if (worldWidth <= 0.01f || worldHeight <= 0.01f)
            {
                return CharacterScale;
            }

            return Mathf.Min(DemoCharacterHeight / worldHeight, DemoCharacterWidth / worldWidth);
        }

        private static void LayoutInstances(List<GameObject> instances)
        {
            for (int row = 0; row < RowY.Length; row++)
            {
                int start = row * CharactersPerRow;
                int count = Mathf.Min(CharactersPerRow, instances.Count - start);
                if (count <= 0)
                {
                    break;
                }

                float startX = -Spacing * (count - 1) * 0.5f;
                for (int column = 0; column < count; column++)
                {
                    instances[start + column].transform.localPosition =
                        new Vector3(startX + Spacing * column, RowY[row], 0f);
                }
            }
        }

        private static void CreateCamera(Scene scene)
        {
            var cameraGo = new GameObject(CameraObjectName);
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            cameraGo.tag = "MainCamera";

            Camera camera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();

            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            camera.transform.localPosition = new Vector3(0f, 0f, -10f);
            camera.transform.localRotation = Quaternion.identity;

            // 场景里不放灯光：Spine/Skeleton 是无光照着色器。
        }

        private static void CreateDemoDriver(Scene scene, List<SpineUnitView> views)
        {
            var driverGo = new GameObject(DriverObjectName);
            SceneManager.MoveGameObjectToScene(driverGo, scene);

            SpineAnimationDemo driver = driverGo.AddComponent<SpineAnimationDemo>();

            var driverSo = new SerializedObject(driver);
            SerializedProperty units = driverSo.FindProperty("units");
            units.arraySize = views.Count;
            for (int i = 0; i < views.Count; i++)
            {
                units.GetArrayElementAtIndex(i).objectReferenceValue = views[i];
            }

            driverSo.FindProperty("attackInterval").floatValue = 1.5f;
            driverSo.FindProperty("autoCycle").boolValue = true;
            driverSo.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
