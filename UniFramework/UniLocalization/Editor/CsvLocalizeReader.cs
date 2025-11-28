using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UniFramework.Localization.Editor
{
    /// <summary>
    /// CSV 本地化读取器
    /// 约定格式（建议）：
    /// 第一行：表头，例如：Key,Value_zh-CN,Value_en
    /// 之后每行：第一列为本地化 Key，后续列为不同语言的值。
    /// 本读取器当前只关心第一列 Key，用于在编辑器中列出可选的翻译键。
    /// </summary>
    public class CsvLocalizeReader : ILocalizeReader
    {
        private readonly List<string> _keys = new List<string>(256);

        /// <summary>
        /// 本地化 KEY 集合
        /// </summary>
        public List<string> Keys => _keys;

        /// <summary>
        /// 从 CSV 文本资源中读取 Key 列表
        /// </summary>
        /// <param name="assetPath">Unity 资源路径，例如：Assets/Localization/UI_zh-CN.csv</param>
        public void Read(string assetPath)
        {
            _keys.Clear();

            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning($"{nameof(CsvLocalizeReader)} assetPath is null or empty.");
                return;
            }

            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (textAsset == null)
            {
                Debug.LogWarning($"{nameof(CsvLocalizeReader)} cannot load TextAsset at path : {assetPath}");
                return;
            }

            using (var reader = new StringReader(textAsset.text))
            {
                string line;
                bool headerSkipped = false;

                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // 跳过注释行
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("#") || trimmed.StartsWith("//"))
                        continue;

                    // 第一行视为表头
                    if (!headerSkipped)
                    {
                        headerSkipped = true;
                        continue;
                    }

                    var columns = ParseCsvLine(line);
                    if (columns.Count == 0)
                        continue;

                    var key = columns[0].Trim();
                    if (string.IsNullOrEmpty(key))
                        continue;

                    if (_keys.Contains(key) == false)
                        _keys.Add(key);
                }
            }
        }

        /// <summary>
        /// 简单 CSV 行解析（支持双引号与转义的双引号）
        /// </summary>
        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line))
                return result;

            var builder = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '\"')
                {
                    // 处理转义双引号 ""
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        builder.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && inQuotes == false)
                {
                    result.Add(builder.ToString());
                    builder.Length = 0;
                }
                else
                {
                    builder.Append(c);
                }
            }

            result.Add(builder.ToString());
            return result;
        }
    }
}


