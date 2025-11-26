using UnityEngine;

namespace UniFramework.Machine
{
    /// <summary>
    /// 敌人示例：演示如何基于 StateMachine 实现多状态 AI。
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        private StateMachine _machine;

        [Header("基础属性")]
        public int maxHp = 100;
        private int _hp;

        [Header("移动相关")]
        public float walkSpeed = 1.5f;
        public float runSpeed = 3.0f;
        public float chaseSpeed = 4.0f;
        public float chaseRange = 8f;       // 进入追击范围
        public float loseChaseRange = 12f;  // 脱离追击范围

        [Header("目标")]
        public Transform target;            // 可以在 Inspector 中拖玩家进来

        private void Awake()
        {
            _hp = maxHp;

            // 以 Enemy 自身作为 Owner
            _machine = new StateMachine(this);

            // 注册所有状态节点
            _machine.AddNode(new EnemyIdleState());
            _machine.AddNode(new EnemyAliveState());
            _machine.AddNode(new EnemyWalkState());
            _machine.AddNode(new EnemyRunState());
            _machine.AddNode(new EnemyChaseState());
            _machine.AddNode(new EnemyDeadState());

            // 如果有初始目标，写入黑板
            if (target != null)
                _machine.SetBlackboardValue("Target", target);

            // 启动：从 Idle 开始
            _machine.Run<EnemyIdleState>();
        }

        private void Update()
        {
            _machine.Update();
        }

        public bool IsAlive => _hp > 0;

        public void TakeDamage(int damage)
        {
            if (!IsAlive)
                return;

            _hp -= damage;
            if (_hp <= 0)
            {
                _hp = 0;
                _machine.ChangeState<EnemyDeadState>();
            }
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            _machine.SetBlackboardValue("Target", newTarget);
        }
    }

    /// <summary>
    /// 敌人状态工具：从状态机中获取 Enemy 和 Target。
    /// </summary>
    internal static class EnemyStateUtil
    {
        public static Enemy GetEnemy(StateMachine machine)
        {
            return machine.Owner as Enemy;
        }

        public static Transform GetTarget(StateMachine machine)
        {
            return machine.GetBlackboardValue("Target") as Transform;
        }
    }

    /// <summary>
    /// 待机状态
    /// </summary>
    internal class EnemyIdleState : IStateNode
    {
        private StateMachine _machine;
        private Enemy _enemy;

        public void OnCreate(StateMachine machine)
        {
            _machine = machine;
            _enemy = EnemyStateUtil.GetEnemy(machine);
        }

        public void OnEnter()
        {
            Debug.Log("Enemy -> Idle");
            // TODO：播放待机动画
        }

        public void OnUpdate()
        {
            if (_enemy == null || !_enemy.IsAlive)
            {
                _machine.ChangeState<EnemyDeadState>();
                return;
            }

            // 有目标并且进入追击范围，就切到 Chase
            Transform target = EnemyStateUtil.GetTarget(_machine) ?? _enemy.target;
            if (target != null)
            {
                float dist = Vector3.Distance(_enemy.transform.position, target.position);
                if (dist <= _enemy.chaseRange)
                {
                    _machine.SetBlackboardValue("Target", target);
                    _machine.ChangeState<EnemyChaseState>();
                    return;
                }
            }
        }

        public void OnExit()
        {
        }
    }

    /// <summary>
    /// 活着状态（可用于扩展通用逻辑）
    /// </summary>
    internal class EnemyAliveState : IStateNode
    {
        private StateMachine _machine;
        private Enemy _enemy;

        public void OnCreate(StateMachine machine)
        {
            _machine = machine;
            _enemy = EnemyStateUtil.GetEnemy(machine);
        }

        public void OnEnter()
        {
            Debug.Log("Enemy -> Alive");
        }

        public void OnUpdate()
        {
            if (_enemy == null || !_enemy.IsAlive)
            {
                _machine.ChangeState<EnemyDeadState>();
                return;
            }

            // 此处可根据需要在 Alive 中分发到 Walk / Run / Idle 等
        }

        public void OnExit()
        {
        }
    }

    /// <summary>
    /// 行走状态
    /// </summary>
    internal class EnemyWalkState : IStateNode
    {
        private StateMachine _machine;
        private Enemy _enemy;

        public void OnCreate(StateMachine machine)
        {
            _machine = machine;
            _enemy = EnemyStateUtil.GetEnemy(machine);
        }

        public void OnEnter()
        {
            Debug.Log("Enemy -> Walk");
            // TODO：播放走路动画
        }

        public void OnUpdate()
        {
            if (_enemy == null || !_enemy.IsAlive)
            {
                _machine.ChangeState<EnemyDeadState>();
                return;
            }

            _enemy.transform.Translate(Vector3.forward * _enemy.walkSpeed * Time.deltaTime);
        }

        public void OnExit()
        {
        }
    }

    /// <summary>
    /// 奔跑状态
    /// </summary>
    internal class EnemyRunState : IStateNode
    {
        private StateMachine _machine;
        private Enemy _enemy;

        public void OnCreate(StateMachine machine)
        {
            _machine = machine;
            _enemy = EnemyStateUtil.GetEnemy(machine);
        }

        public void OnEnter()
        {
            Debug.Log("Enemy -> Run");
            // TODO：播放奔跑动画
        }

        public void OnUpdate()
        {
            if (_enemy == null || !_enemy.IsAlive)
            {
                _machine.ChangeState<EnemyDeadState>();
                return;
            }

            _enemy.transform.Translate(Vector3.forward * _enemy.runSpeed * Time.deltaTime);
        }

        public void OnExit()
        {
        }
    }

    /// <summary>
    /// 追击状态（charse）
    /// </summary>
    internal class EnemyChaseState : IStateNode
    {
        private StateMachine _machine;
        private Enemy _enemy;
        private Transform _target;

        public void OnCreate(StateMachine machine)
        {
            _machine = machine;
            _enemy = EnemyStateUtil.GetEnemy(machine);
        }

        public void OnEnter()
        {
            Debug.Log("Enemy -> Chase");
            _target = EnemyStateUtil.GetTarget(_machine) ?? _enemy.target;
        }

        public void OnUpdate()
        {
            if (_enemy == null || !_enemy.IsAlive)
            {
                _machine.ChangeState<EnemyDeadState>();
                return;
            }

            if (_target == null)
            {
                _machine.ChangeState<EnemyIdleState>();
                return;
            }

            float dist = Vector3.Distance(_enemy.transform.position, _target.position);

            // 超出追击丢失范围，返回 Idle
            if (dist > _enemy.loseChaseRange)
            {
                _machine.ChangeState<EnemyIdleState>();
                return;
            }

            // 朝目标移动并朝向插值
            Vector3 dir = (_target.position - _enemy.transform.position).normalized;
            _enemy.transform.position += dir * _enemy.chaseSpeed * Time.deltaTime;
            _enemy.transform.forward = Vector3.Lerp(_enemy.transform.forward, dir, 10f * Time.deltaTime);
        }

        public void OnExit()
        {
        }
    }

    /// <summary>
    /// 死亡状态
    /// </summary>
    internal class EnemyDeadState : IStateNode
    {
        private StateMachine _machine;
        private Enemy _enemy;

        public void OnCreate(StateMachine machine)
        {
            _machine = machine;
            _enemy = EnemyStateUtil.GetEnemy(machine);
        }

        public void OnEnter()
        {
            Debug.Log("Enemy -> Dead");
            if (_enemy != null)
            {
                // 示例：3 秒后销毁对象
                Object.Destroy(_enemy.gameObject, 3f);
            }
        }

        public void OnUpdate()
        {
        }

        public void OnExit()
        {
        }
    }
}


