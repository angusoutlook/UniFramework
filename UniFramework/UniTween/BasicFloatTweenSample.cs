using UnityEngine;
using UniFramework.Tween;

/// <summary>
/// 使用 FloatTween 补间一个普通 float 数值的示例。
/// 展示：Allocate / SetOnUpdate / SetEase / SetLoop / UniTween.Play
/// </summary>
public class BasicFloatTweenSample : MonoBehaviour
{
    [Header("Float Tween 参数")]
    public float from = 0f;
    public float to = 1f;
    public float duration = 2f;

    [Header("当前运行时数值（仅用于观察）")]
    public float currentValue;

    private TweenHandle _handle;

    void Start()
    {
        // 假定项目入口已调用 UniTween.Initalize()

        // 1. 构建一个 FloatTween 节点
        FloatTween tween = FloatTween
            .Allocate(duration, from, to)
            .SetEase(TweenEase.Cubic.EaseOut)
            .SetOnUpdate(value =>
            {
                // 演示：将补间结果写回到 public 字段，方便在 Inspector 中观察
                currentValue = value;
            })
            .SetLoop(ETweenLoop.Restart, loopCount: 3);

        // 2. 交给 UniTween 系统驱动，并绑定到当前 GameObject
        _handle = UniTween.Play(tween, this.gameObject);
    }

    void Update()
    {
        // 示例：按下 Escape 时，中途终止这个物体上的所有补间
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            UniTween.Abort(this.gameObject);
        }
    }
}


