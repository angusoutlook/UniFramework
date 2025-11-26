using System;
using System.IO;
using UnityEngine;

namespace UniFramework.Log
{
    /// <summary>
    /// UniLog 调试面板
    /// 显示日志目录、最近日志文件，并支持复制路径和上传钩子。
    /// </summary>
    public class UniLogPanel : MonoBehaviour
    {
        /// <summary>
        /// 列表中显示的最大日志文件数量
        /// </summary>
        [SerializeField]
        private int _maxFiles = 10;

        /// <summary>
        /// 是否显示面板
        /// </summary>
        [SerializeField]
        private bool _visible = true;

        /// <summary>
        /// 可选：用于切换面板显示的按键（例如 F9）
        /// </summary>
        [SerializeField]
        private KeyCode _toggleKey = KeyCode.F9;

        /// <summary>
        /// 上传日志文件的回调，由外部项目自行实现上传逻辑
        /// </summary>
        public static Action<string> OnUploadLogFile;

        private Vector2 _scrollPosition;

        private void Update()
        {
            if (_toggleKey != KeyCode.None && Input.GetKeyDown(_toggleKey))
            {
                _visible = !_visible;
            }
        }

        private void OnGUI()
        {
            if (!_visible)
                return;

            const int width = 520;
            const int height = 360;
            Rect rect = new Rect(10, 10, width, height);

            GUILayout.BeginArea(rect, "UniLog Debug Panel", GUI.skin.window);

            string directory = UniLog.GetLogDirectory();
            GUILayout.Label("日志目录:");
            GUILayout.TextField(directory);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("复制目录路径", GUILayout.Width(120)))
            {
                GUIUtility.systemCopyBuffer = directory;
            }
            if (GUILayout.Button("打开目录", GUILayout.Width(120)))
            {
                OpenDirectory(directory);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("最近日志文件:");

            string[] files = UniLog.GetRecentLogFiles(_maxFiles);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));

            if (files != null && files.Length > 0)
            {
                foreach (string path in files)
                {
                    if (string.IsNullOrEmpty(path))
                        continue;

                    string fileName = Path.GetFileName(path);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(fileName, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("复制路径", GUILayout.Width(80)))
                    {
                        GUIUtility.systemCopyBuffer = path;
                    }

                    if (GUILayout.Button("上传", GUILayout.Width(80)))
                    {
                        if (OnUploadLogFile != null)
                            OnUploadLogFile.Invoke(path);
                        else
                            UnityEngine.Debug.LogWarning($"[{nameof(UniLogPanel)}] OnUploadLogFile 未实现，无法上传：{path}");
                    }

                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("当前目录下没有找到日志文件。");
            }

            GUILayout.EndScrollView();

            GUILayout.Space(8);
            if (GUILayout.Button("关闭面板", GUILayout.Height(24)))
            {
                _visible = false;
            }

            GUILayout.EndArea();
        }

        private void OpenDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return;

            if (!Directory.Exists(directory))
            {
                UnityEngine.Debug.LogWarning($"[{nameof(UniLogPanel)}] 日志目录不存在：{directory}");
                return;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string url = "file:///" + directory.Replace("\\", "/");
            Application.OpenURL(url);
#else
            Application.OpenURL(directory);
#endif
        }
    }
}


