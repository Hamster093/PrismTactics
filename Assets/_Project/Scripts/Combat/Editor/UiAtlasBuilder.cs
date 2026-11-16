/****************************************************
    文件：UiAtlasBuilder.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/09 18:15:00
	功能：生成/重建 UI 图集，把小尺寸 Sprite 收进一张 SpriteAtlas 并完成打包。
*****************************************************/

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// UI 图集构建工具：按"尺寸 ≤ 600 且不属于立绘"的规则收录 Sprite，
    /// 打包成 2048 的 BC7 图集（超过一页会自动拆页），供 UiAtlasService 运行时加载。
    /// </summary>
    public static class UiAtlasBuilder
    {
        private const string UiRoot = "Assets/GameContent/UIHandDrawn";
        private const string AtlasPath = UiRoot + "/UIHandDrawnAtlas.spriteatlas";
        // 超过这个尺寸的是背景板，不进图集
        private const int MaxSpriteSize = 600;

        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.1 生成 UI 图集")]
        public static void Build()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, AtlasPath);
            }

            // 清掉旧收录项，保证重复执行结果一致
            Object[] oldPackables = SpriteAtlasExtensions.GetPackables(atlas);
            if (oldPackables != null && oldPackables.Length > 0)
            {
                SpriteAtlasExtensions.Remove(atlas, oldPackables);
            }

            var packables = new List<Object>();
            int skipped = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UiRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType != TextureImporterType.Sprite)
                {
                    skipped++;
                    continue;
                }

                // 立绘只收「裁切版」：它与完整版同名，收两份会冲突；完整版只在详情面板单张显示
                if (path.Contains("/OperatorPortraits/") && !path.Contains("/OperatorPortraits/Cropped/"))
                {
                    skipped++;
                    continue;
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null || Mathf.Max(texture.width, texture.height) > MaxSpriteSize)
                {
                    skipped++;
                    continue;
                }

                packables.Add(texture);
            }

            SpriteAtlasExtensions.Add(atlas, packables.ToArray());
            SpriteAtlasExtensions.SetPackingSettings(atlas, new SpriteAtlasPackingSettings
            {
                blockOffset = 1,
                enableRotation = false,
                enableTightPacking = false,
                padding = 4,
            });
            SpriteAtlasExtensions.SetTextureSettings(atlas, new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = false,
                sRGB = true,
                filterMode = FilterMode.Bilinear,
            });
            SpriteAtlasExtensions.SetPlatformSettings(atlas, new TextureImporterPlatformSettings
            {
                name = "Standalone",
                overridden = true,
                maxTextureSize = 2048,
                format = TextureImporterFormat.BC7,
                textureCompression = TextureImporterCompression.CompressedHQ,
                compressionQuality = 100,
            });

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);

            Debug.Log($"[UIAtlas] 收录 {packables.Count} 个 Sprite，跳过 {skipped} 个（立绘/背景/非 Sprite）。图集：{AtlasPath}");
        }

        /// <summary>
        /// 先把收录范围内的源图改成无压缩，再重新生成图集；用于消除 Unity 的二次压缩警告。
        /// 代价是这些贴图在资源包里变成 RGBA32，包体变大。
        /// </summary>
        [MenuItem("Tools/PrismTactics/9 其他/9.3 资源与美术/9.3.2 生成 UI 图集（源图无压缩）")]
        public static void BuildWithUncompressedSources()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UiRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/OperatorPortraits/"))
                {
                    continue;
                }

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType != TextureImporterType.Sprite)
                {
                    continue;
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null || Mathf.Max(texture.width, texture.height) > MaxSpriteSize)
                {
                    continue;
                }

                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }

            Build();
        }
    }
}
