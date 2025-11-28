using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniFramework.Localization.Editor
{
    /// <summary>
    /// JSON 本地化读取器
    /// 约定格式（建议）：
    /// {
    ///     "entries": [
    ///         { "key": "HELLO", "value": "xxxx" },
    ///         { "key": "QUIT",  "value": "yyyy" }
    ///     ]
    /// }
    /// 本读取器当前只关心每个 entry 的 key 字段，用于在编辑器中列出可选的翻译键。
    /// </summary>
    public class JsonLocalizeReader : ILocalizeReader
    {
        [Serializable]
        private class JsonEntry
        {
            public string key;
            public string value;
        }

        [Serializable]
        private class JsonLocalizationFile
        {
            public JsonEntry[] entries;
        }

        private readonly List<string> _keys = new List<string>(256);

        /// <summary>
        /// 本地化 KEY 集合
        /// </summary>
        public List<string> Keys => _keys;

        /// <summary>
        /// 从 JSON 文本资源中读取 Key 列表
        /// </summary>
        /// <param name="assetPath">Unity 资源路径，例如：Assets/Localization/UI.json</param>
        public void Read(string assetPath)
        {
            _keys.Clear();

            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning($"{nameof(JsonLocalizeReader)} assetPath is null or empty.");
                return;
            }

            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (textAsset == null)
            {
                Debug.LogWarning($"{nameof(JsonLocalizeReader)} cannot load TextAsset at path : {assetPath}");
                return;
            }

            JsonLocalizationFile fileData = null;
            try
            {
                fileData = JsonUtility.FromJson<JsonLocalizationFile>(textAsset.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{nameof(JsonLocalizeReader)} parse json failed : {assetPath}\n{e}");
                return;
            }

            if (fileData == null || fileData.entries == null)
            {
                Debug.LogWarning($"{nameof(JsonLocalizeReader)} json entries is null : {assetPath}");
                return;
            }

            foreach (var entry in fileData.entries)
            {
                if (entry == null)
                    continue;

                var key = entry.key;
                if (string.IsNullOrEmpty(key))
                    continue;

                if (_keys.Contains(key) == false)
                    _keys.Add(key);
            }
        }
    }
}


