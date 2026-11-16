/****************************************************
    文件：Phase3BuildinCatalogMerger.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/4 14:30:00
    功能：合并内置包目录：把当前目录下所有 bundle 写入 BuildinCatalog，保留旧版本 bundle 的回滚映射。
*****************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// YooAsset 默认只为当前清单生成 Catalog，旧版本 bundle 虽然保留在目录里，
    /// 但回滚旧 manifest 时会因为缺少 GUID->文件映射而加载失败。
    /// 这里按文件名补齐所有 bundle 的映射，保证 latest/previous 都能解析。
    /// </summary>
    public static class Phase3BuildinCatalogMerger
    {
        private const uint FileSign = 0x0133C5EE;
        private const string FileVersion = "1.0.0";
        private const string JsonFileName = "BuildinCatalog.json";
        private const string BinaryFileName = "BuildinCatalog.bytes";

        [Serializable]
        private sealed class CatalogData
        {
            public string FileVersion;
            public string PackageName;
            public string PackageVersion;
            public List<Wrapper> Wrappers = new List<Wrapper>();
        }

        [Serializable]
        private sealed class Wrapper
        {
            public string BundleGUID;
            public string FileName;
        }

        public static void Merge(string packageRoot)
        {
            if (!Directory.Exists(packageRoot))
            {
                throw new DirectoryNotFoundException("内置包目录不存在：" + packageRoot);
            }

            string packageName = new DirectoryInfo(packageRoot).Name;
            string versionFile = Path.Combine(packageRoot, packageName + ".version");
            if (!File.Exists(versionFile))
            {
                throw new FileNotFoundException("找不到版本文件：" + versionFile);
            }

            string packageVersion = File.ReadAllText(versionFile).Trim();
            var catalog = new CatalogData
            {
                FileVersion = FileVersion,
                PackageName = packageName,
                PackageVersion = packageVersion,
            };

            string[] bundles = Directory.GetFiles(packageRoot, "*.bundle", SearchOption.TopDirectoryOnly);
            Array.Sort(bundles, StringComparer.Ordinal);
            for (int i = 0; i < bundles.Length; i++)
            {
                string fileName = Path.GetFileName(bundles[i]);
                string guid = Path.GetFileNameWithoutExtension(fileName);
                catalog.Wrappers.Add(new Wrapper
                {
                    BundleGUID = guid,
                    FileName = fileName,
                });
            }

            string jsonPath = Path.Combine(packageRoot, JsonFileName);
            File.WriteAllText(jsonPath, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
            WriteBinary(Path.Combine(packageRoot, BinaryFileName), catalog);

            Debug.Log($"[M3.3] Catalog 合并完成：{packageName} {packageVersion}，{catalog.Wrappers.Count} 个 bundle");
            AssetDatabase.Refresh();
        }

        private static void WriteBinary(string path, CatalogData catalog)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(FileSign);
                WriteString(writer, catalog.FileVersion);
                WriteString(writer, catalog.PackageName);
                WriteString(writer, catalog.PackageVersion);
                writer.Write(catalog.Wrappers.Count);

                for (int i = 0; i < catalog.Wrappers.Count; i++)
                {
                    WriteString(writer, catalog.Wrappers[i].BundleGUID);
                    WriteString(writer, catalog.Wrappers[i].FileName);
                }
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new FormatException("Catalog 字符串过长：" + value);
            }

            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }
}
