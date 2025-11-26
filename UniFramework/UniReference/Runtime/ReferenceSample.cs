using System.Collections.Generic;
using UnityEngine;

namespace UniFramework.Reference
{
    /// <summary>
    /// UniReference 使用示例
    /// 演示如何实现 IReference 并通过对象池申请 / 回收。
    /// </summary>
    public class ReferenceSample : MonoBehaviour
    {
        /// <summary>
        /// 示例：自定义一个实现 IReference 的结构，用于统计伤害事件。
        /// </summary>
        internal class DamageEvent : IReference
        {
            public int AttackerId;
            public int TargetId;
            public int DamageValue;

            /// <summary>
            /// 从对象池取出时重置为默认状态
            /// </summary>
            public void OnSpawn()
            {
                AttackerId = 0;
                TargetId = 0;
                DamageValue = 0;
            }
        }

        private readonly List<DamageEvent> _tempEvents = new List<DamageEvent>(16);

        private void Start()
        {
            // 可选：调整默认初始容量
            UniReference.InitCapacity = 128;

            // 简单演示：生成几条伤害事件并立即回收
            GenerateDamageEvents();
            ReleaseAllDamageEvents();
        }

        /// <summary>
        /// 申请多个 DamageEvent，模拟使用过程
        /// </summary>
        private void GenerateDamageEvents()
        {
            _tempEvents.Clear();

            for (int i = 0; i < 5; i++)
            {
                // 从对象池申请一个 DamageEvent 实例
                DamageEvent evt = UniReference.Spawn<DamageEvent>();

                // OnSpawn 已经重置为默认值，这里填充业务数据
                evt.AttackerId = 1000 + i;
                evt.TargetId = 2000 + i;
                evt.DamageValue = 10 * (i + 1);

                Debug.Log($"DamageEvent {i}: {evt.AttackerId} -> {evt.TargetId}, damage = {evt.DamageValue}");

                _tempEvents.Add(evt);
            }
        }

        /// <summary>
        /// 批量回收 DamageEvent 到对象池
        /// </summary>
        private void ReleaseAllDamageEvents()
        {
            if (_tempEvents.Count == 0)
                return;

            UniReference.Release(_tempEvents);
            _tempEvents.Clear();

            Debug.Log("All DamageEvent instances released to UniReference pool.");
        }
    }
}


