using UnityEngine;

namespace UniFramework.Singleton
{
    /// <summary>
    /// 光标管理单例
    /// </summary>
    public sealed class CursorManager : SingletonInstance<CursorManager>, ISingleton
    {
        private bool _visible = true;
        private CursorLockMode _lockMode = CursorLockMode.None;

        public bool IsVisible => _visible;
        public CursorLockMode LockMode => _lockMode;

        /// <summary>
        /// 创建单例时同步一次当前配置
        /// </summary>
        public void OnCreate(object createParam)
        {
            ApplyCursorState();
        }

        public void OnUpdate()
        {
            // 当前光标管理不需要逐帧更新
        }

        /// <summary>
        /// 销毁单例时可根据需要恢复到默认状态
        /// </summary>
        public void OnDestroy()
        {
            DestroyInstance();
        }

        /// <summary>
        /// 设置光标是否可见
        /// </summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            ApplyCursorState();
        }

        /// <summary>
        /// 设置光标锁定模式
        /// </summary>
        public void SetLockState(CursorLockMode mode)
        {
            _lockMode = mode;
            ApplyCursorState();
        }

        private void ApplyCursorState()
        {
            Cursor.visible = _visible;
            Cursor.lockState = _lockMode;
        }
    }
}


