using System;
using UnityEngine;
using UniFramework.Reference;

namespace UniFramework.Event
{
    /// <summary>
    /// UniEvent 使用示例脚本
    /// 挂在场景中的任意 GameObject 上，通过按键演示多种用法。
    /// </summary>
    public class UniEventSample : MonoBehaviour
    {
        /// <summary>
        /// 可选：使用 EventGroup 统一管理一组监听，便于一键注销
        /// </summary>
        private readonly EventGroup _group = new EventGroup();

        private void Awake()
        {
            // 确保事件系统已初始化（如果已经初始化会抛异常，这里简单忽略）
            try
            {
                UniEvent.Initalize();
            }
            catch (Exception)
            {
                // 已经初始化就不再处理
            }

            // 默认使用正序广播，不允许重复注册
            UniEvent.BroadcastOrder = UniEvent.EBroadcastOrder.Normal;
            UniEvent.AllowDuplicateRegistration = false;

            // 1. 通过泛型方式注册监听
            UniEvent.AddListener<SampleEventA>(OnSampleEventA_Main);

            // 2. 同一事件再注册一个“只执行一次”的监听（在内部自移除）
            UniEvent.AddListener<SampleEventA>(OnSampleEventA_Once);

            // 3. 通过 Type 注册监听
            UniEvent.AddListener(typeof(SamplePooledEvent), OnSamplePooledEvent);

            // 4. 使用 EventGroup 注册监听，方便在 OnDestroy 里统一移除
            _group.AddListener<SampleEventA>(OnSampleEventA_Grouped);
        }

        private void OnDestroy()
        {
            // 移除通过 EventGroup 注册的所有监听
            _group.RemoveAllListener();

            // 其它通过 UniEvent.AddListener 注册的监听，
            // 可以根据需要在这里显式 RemoveListener。
            UniEvent.RemoveListener<SampleEventA>(OnSampleEventA_Main);
            UniEvent.RemoveListener<SampleEventA>(OnSampleEventA_Once);
            UniEvent.RemoveListener(typeof(SamplePooledEvent), OnSamplePooledEvent);
        }

        private void Update()
        {
            // 数字 1：发送一个简单事件（立即广播）
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                var msg = new SampleEventA
                {
                    Text = "立即广播事件 A"
                };
                UniEvent.SendMessage(msg);
            }

            // 数字 2：发送一个支持对象池复用的事件（实现 IReference）
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                var msg = UniReference.Spawn<SamplePooledEvent>();
                msg.Value = Time.frameCount;
                UniEvent.SendMessage(msg);
                // 注意：这里不需要手动 Release，UniEvent 会在 Trigger 结束后自动回收
            }

            // 数字 3：延迟一帧广播事件
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                var msg = new SampleEventA
                {
                    Text = "延迟一帧广播事件 A"
                };
                UniEvent.PostMessage(msg);
            }

            // R 键：切换广播顺序（正序 / 倒序）
            if (Input.GetKeyDown(KeyCode.R))
            {
                UniEvent.BroadcastOrder = UniEvent.BroadcastOrder == UniEvent.EBroadcastOrder.Normal
                    ? UniEvent.EBroadcastOrder.Reverse
                    : UniEvent.EBroadcastOrder.Normal;

                Debug.Log($"[UniEventSample] BroadcastOrder = {UniEvent.BroadcastOrder}");
            }
        }

        /// <summary>
        /// 普通监听：每次 SampleEventA 都会执行
        /// </summary>
        private void OnSampleEventA_Main(IEventMessage message)
        {
            if (message is SampleEventA evt)
            {
                Debug.Log($"[UniEventSample] OnSampleEventA_Main : {evt.Text}");
            }
        }

        /// <summary>
        /// 只执行一次的监听：在回调内部自移除
        /// </summary>
        private void OnSampleEventA_Once(IEventMessage message)
        {
            if (message is SampleEventA evt)
            {
                Debug.Log($"[UniEventSample] OnSampleEventA_Once : {evt.Text} (下次不会再触发)");
                UniEvent.RemoveListener<SampleEventA>(OnSampleEventA_Once);
            }
        }

        /// <summary>
        /// 通过 EventGroup 注册的监听，方便统一释放
        /// </summary>
        private void OnSampleEventA_Grouped(IEventMessage message)
        {
            if (message is SampleEventA evt)
            {
                Debug.Log($"[UniEventSample] OnSampleEventA_Grouped : {evt.Text}");
            }
        }

        /// <summary>
        /// 监听支持对象池复用的事件
        /// </summary>
        private void OnSamplePooledEvent(IEventMessage message)
        {
            if (message is SamplePooledEvent evt)
            {
                Debug.Log($"[UniEventSample] OnSamplePooledEvent : Value = {evt.Value}");
            }
        }
    }

    /// <summary>
    /// 简单事件示例（不走对象池）
    /// </summary>
    internal class SampleEventA : IEventMessage
    {
        public string Text;
    }

    /// <summary>
    /// 支持对象池复用的事件示例
    /// </summary>
    internal class SamplePooledEvent : IEventMessage, IReference
    {
        public int Value;

        public void OnSpawn()
        {
            Value = 0;
        }
    }
}


