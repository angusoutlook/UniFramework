using UnityEngine;
using UniFramework.Tween;

public class FloatTweenSample : MonoBehaviour
{
    [Header("Float Tween 参数")]
    public float from = 0f;
    public float to = 1f;
    public float duration = 10f;

    [Header("当前运行时数值（仅用于观察）")]
    public float currentValue;
    public CanvasGroup target;

    private TweenHandle _handle;

    void Awake()
    {
        // 注意：整个项目中 UniTween.Initalize() 只需要调用一次
        // 如果你已经在别的入口脚本里初始化过，这里就不要再调用了
        //UniTween.Initalize();
    }

    void Start()
    {
        // 1. 构建一个 FloatTween 节点
        FloatTween tween = (FloatTween)FloatTween
            .Allocate(duration, from, to)                // 必要：时长 + 起始值 + 目标值
            .SetEase(TweenEase.Quad.EaseInOut)          // 可选：缓动曲线
            .SetOnBegin(() =>                           // 可选：开始时回调
            {
                Debug.Log("FloatTween Begin");
            })
            .SetOnUpdate(value =>                       // 关键：每帧拿到补间结果
            {
                target.alpha = value; // 举例：将结果应用到 CanvasGroup 的透明度上
                currentValue = value;
                // 这里你可以做真正的业务逻辑，例如：
                // audioSource.volume = value;
                // material.SetFloat("_MyParam", value);
            })
            .SetOnComplete(() =>                        // 可选：结束时回调
            {
                Debug.Log("FloatTween Complete");
            });

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