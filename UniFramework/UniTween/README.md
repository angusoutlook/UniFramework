# UniFramework.Tween

一个轻量级的补间动画系统。（扩展方便，使用灵活，功能强大）

初始化补间动画系统

```c#
using UnityEngine;
using UniFramework.Tween;

void Start()
{
    // 初始化补间动画系统
    UniTween.Initalize();
}
```

传统编程

```c#
void Start()
{
    // 原地停留1秒，然后向上移动，停留1秒，然后同时缩小并回归原位。
    var tween = UniTween.AllocateSequence
    (
        UniTween.AllocateDelay(1f),
        this.transform.TweenMove(0.5f, new Vector3(0, 256, 0)),
        UniTween.AllocateDelay(1f),
        UniTween.AllocateParallel
        (
            this.transform.TweenScaleTo(0.5f, new Vector3(0.2f, 0.2f, 1f)),
            this.transform.TweenMove(0.5f, new Vector3(0, 0, 0))
        )
    );
    this.gameObject.PlayTween(tween); 
}
```

链式编程

```c#
void Start()
{
    // 原地停留1秒，然后向上移动，停留1秒，然后同时缩小并回归原位。
    ITweenChain tween = UniTween.AllocateSequence();
    tween.Delay(1f).
        Append(this.transform.TweenMove(0.5f, new Vector3(0, 256, 0))).
        Delay(1f).
        SwitchToParallel().
        Append(this.transform.TweenScaleTo(0.5f, new Vector3(0.2f, 0.2f, 1f))).
        Append(this.transform.TweenMove(0.5f, new Vector3(0, 0, 0)));
     this.gameObject.PlayTween(tween);  
}
```

默认的公共补间方法一共有30种，还可以使用AnimationCurve补充效果

```c#
public AnimationCurve EaseCurve;

public void PlayAnim()
{
    var tween = this.transform.TweenScaleTo(1f, Vector3.zero).SetEase(EaseCurve);
    UniTween.Play(tween);
}
```

扩展支持任意对象

```c#
// 扩展支持Image对象
public static class UnityEngine_UI_Image_Tween_Extension
{
    public static ColorTween TweenColor(this Image obj, float duration, Color from, Color to)
    {
        ColorTween node = ColorTween.Allocate(duration, from, to);
        node.SetOnUpdate((result) => { obj.color = result; });
        return node;
    }
    public static ColorTween TweenColorTo(this Image obj, float duration, Color to)
    {
        return TweenColor(obj, duration, obj.color, to);
    }
    public static ColorTween TweenColorFrom(this Image obj, float duration, Color from)
    {
        return TweenColor(obj, duration, from, obj.color);
    }
}
```

## 架构概览（基于当前 Runtime 源码）

- **核心入口 `UniTween`**：静态补间系统，负责：
  - `Initalize()` 创建挂有 `UniTweenDriver` 的常驻 `GameObject`，在 `Update()` 中统一驱动所有补间。
  - 维护三个列表：`_tweens`（运行中）、`_newer`（待加入）、`_remover`（待移除），每帧根据 `ETweenStatus` 和异常/对象销毁情况增删补间。
  - 通过 `Play(ITweenNode|ITweenChain|ChainNode, Object)` 创建 `TweenHandle` 并加入系统，通过 `Abort(TweenHandle|Object)` 中途终止。

- **运行实例 `TweenHandle`**：
  - 持有补间根节点 `ITweenNode tweenRoot` 和可选的 `UnityEngine.Object unityObject`，提供 `OnDispose` 结束回调。
  - “安全模式”：当传入 `unityObject` 时，如果对象被销毁，`IsCanRemove()` 会自动标记该补间可移除，避免空引用。
  - 捕获节点更新中的异常，出现异常则中止该补间并输出日志。

- **节点模型 `ITweenNode` & `ETweenStatus`**：
  - 所有节点统一实现 `ITweenNode` 接口：`Status`（Idle / Runing / Completed）与 `OnUpdate(deltaTime)`。
  - `ETweenStatus` 被 `SequenceNode`、`ParallelNode` 等复合节点用来判断子节点是否完成。

- **数值补间 `ValueNode<T>` 系列**：
  - `ValueNode<T>` 作为抽象基类，内部实现时间推进、循环（`ETweenLoop.None/Restart/PingPong`）、缓动函数和回调逻辑。
  - `FloatTween` / `Vector2Tween` / `Vector3Tween` / `Vector4Tween` / `ColorTween` / `QuaternionTween` 仅重写插值函数（默认使用 Unity 的 `LerpUnclamped`），复用同一时序逻辑。
  - 通过 `SetEase(AnimationCurve)` 或 `SetEase(TweenEaseDelegate)` 接入 `TweenEase` 中的多种缓动曲线，通过 `SetLerp` 自定义插值。

- **复合链 `ChainNode` 及其子类**：
  - `ChainNode` 同时实现 `ITweenNode` 与 `ITweenChain`，维护子节点列表 `_nodes`，并通过抽象方法 `UpdateChainNodes(deltaTime)` 定义组合逻辑。
  - `SequenceNode`：子节点顺序执行，遇到还在 Idle/Runing 的节点就停止本帧后续节点的更新，全部完成后自身标记 Completed。
  - `ParallelNode`：子节点并行执行，只要有任意子节点仍在 Idle/Runing，则整体未完成。
  - `SelectorNode`：在子节点中随机选择一个节点执行，选中节点完成后整个 Selector 即完成。

- **基础功能节点**：
  - `ExecuteNode`：执行一次 `Action` 后立即完成，适合在链中插入任意逻辑。
  - `UntilNode`：每帧判断条件 `Func<bool>`，条件满足即完成；若条件为 null 会直接发出警告并视为完成。
  - `TimerNode`：封装 `UniTimer`，支持延迟、间隔、持续时长、最大触发次数等多种时间控制形式。

- **时间与数学工具**：
  - `UniTimer`：提供 `CreateOnceTimer` / `CreatePepeatTimer` / `CreateDurationTimer` 等工厂方法，统一管理延迟 + 间隔 + 总时长 + 触发次数。
  - `TweenEase`：封装自 easings.net 的常见缓动函数族（Sine、Quad、Cubic、Expo、Bounce、Elastic 等），与 `ValueNode` 的 `TweenEaseDelegate` 完全匹配。
  - `TweenMath`：提供角度插值、二/三阶贝塞尔曲线、样条曲线及抖动 `Shake` 等高级路径和噪声工具。

- **Unity 扩展与链式 API**：
  - 在 `UnityEngine_XXX_Tween_Extension` 中，通过扩展方法为 `Transform`、`RectTransform`、`Image`、`CanvasGroup` 等组件提供 `TweenXXXTo/From` 等高层 API，内部通过 `SetOnUpdate` 把补间结果写回组件属性。
  - `TweenChainExtension` 为任意 `ITweenChain` 提供 `Execute` / `Delay` / `Duration` / `Repeat` / `AppendParallel` / `AppendSequence` / `AppendSelector` 等扩展方法，配合 `UniTweenFactory` 以 DSL 的方式构建复杂动画流程。

## 典型用法示例（推荐）

下面示例展示了基于当前 Runtime 源码的推荐写法：通过 `SequenceNode` + 链式扩展方法构造补间链，再通过 `UniTween.Play` 播放，并绑定到指定的 GameObject。

```c#
using UnityEngine;
using UniFramework.Tween;

public class UniTweenSample : MonoBehaviour
{
    public Transform target;

    void Awake()
    {
        // 一般在游戏入口初始化一次即可
        UniTween.Initalize();
    }

    void Start()
    {
        if (target == null)
            target = this.transform;

        // 构建一个顺序补间链：
        // 1）立即执行一段逻辑
        // 2）停留 1 秒
        // 3）并行：位移 + 缩放
        // 4）再停留 0.3 秒
        // 5）执行收尾逻辑
        ITweenChain chain = new SequenceNode()
            .Execute(() =>
            {
                Debug.Log("Sequence Begin");
            })
            .Delay(1f)
            .AppendParallel(
                // 位移动画：1 秒内移动到 (0, 256, 0)
                target.TweenPositionTo(
                    duration: 1f,
                    to: new Vector3(0f, 256f, 0f),
                    relativeWorld: false,
                    setRuntimeValue: true
                ).SetEase(TweenEase.Quad.EaseInOut),

                // 缩放动画：1 秒内缩放到一半，然后 PingPong 往返一次
                target.TweenScaleTo(
                    duration: 1f,
                    to: new Vector3(0.5f, 0.5f, 1f),
                    setRuntimeValue: true
                ).SetLoop(ETweenLoop.PingPong, 1)
                 .SetEase(TweenEase.Sine.EaseInOut)
            )
            .Delay(0.3f)
            .Execute(() =>
            {
                Debug.Log("Sequence Complete");
            });

        // 播放补间链，并绑定到当前 GameObject
        TweenHandle handle = UniTween.Play(chain, this.gameObject);

        // 可选：结束回调
        handle.OnDispose = () =>
        {
            Debug.Log("TweenHandle disposed.");
        };
    }

    void Update()
    {
        // 示例：按下 Escape 键时，关闭当前 GameObject 下的所有补间
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            UniTween.Abort(this.gameObject);
        }
    }
}
```

## ValueNode\<T\> 与 FloatTween 深入理解：_easeFun、_lerpFun 与 LerpUnclamped

- **ValueNode\<T\> 的时间推进流程（基于当前源码）**：
  - 每帧在 `OnUpdate(deltaTime)` 中累加 `_runningTime`，并在 `0 < _runningTime < _duration` 时视为“运行中”。
  - 先通过 `_easeFun(t, b=0, c=1, d=_duration)` 计算出当前补间进度 `progress`（通常在 0~1 之间，但不会被强制限制）。
  - 再调用子类实现的 `GetResultValue(ValueFrom, ValueTo, progress)` 得到本帧的 `Result`，并通过 `_onUpdate(Result)` 回调给外部。

- **_easeFun：时间轴 → 进度（缓动曲线）**：
  - 类型为 `TweenEaseDelegate`，签名为 `float (float t, float b, float c, float d)`，在 `ValueNode` 构造函数中默认指向 `TweenEase.Linear.Default`。
  - 作用是把真实时间 `t`（已运行时长）映射为补间进度 `progress`，例如线性（`t/d`）、二次曲线（`Quad.EaseInOut`）、弹跳（`Bounce.EaseOut`）、自定义 `AnimationCurve` 等。
  - 通过 `SetEase(AnimationCurve)` 可以使用 Unity 的曲线编辑器；通过 `SetEase(TweenEaseDelegate)` 可以直接挂接 `TweenEase.Sine/Quad/Bounce/...` 等静态方法。

- **_lerpFun：起点/终点 + 进度 → 实际值（插值策略）**：
  - 类型为 `TweenLerpDelegate`，签名为 `ValueType (ValueType from, ValueType to, float progress)`。
  - 在各个具体节点（如 `FloatTween`、`Vector3Tween` 等）中，`GetResultValue` 默认逻辑是：
    - 若 `_lerpFun != null`，则调用 `_lerpFun(from, to, progress)` 输出结果；
    - 否则使用 Unity 内置的 `Mathf/Vector2/Vector3/Color/Quaternion.LerpUnclamped(from, to, progress)`。
  - 通过 `SetLerp(TweenLerpDelegate)` 可以覆盖默认插值方式，例如：
    - 在 `ShakePosition` 中使用 `TweenMath.Shake`，忽略 `to`，基于 `from + 随机偏移` 做抖动；
    - 在自定义扩展中使用贝塞尔曲线、样条曲线或 HSV 颜色插值等高级路径，而不改变时间推进逻辑。

- **为什么同时需要 _easeFun 和 _lerpFun（以 0→1、10 秒为例）**：
  - `_easeFun` 控制的是“时间轴上的速度感”：给定 10 秒中的某一时刻 `t`，输出当前进度 `progress` 是线性还是缓入缓出、弹跳等。
  - `_lerpFun` 控制的是“在同一个 progress 下，值如何变化”：可以是普通 Lerp，也可以是二次放大、随机抖动、绕路走曲线等。
  - 组合起来可以做到：既能用 `_easeFun` 控制“什么时候到达某个进度”，又能用 `_lerpFun` 控制“在该进度下具体走哪条数值路径”。

- **LerpUnclamped 在本库中的意义**：
  - `Mathf.LerpUnclamped(a, b, t)` 与 `Mathf.Lerp(a, b, t)` 的差异在于：前者不会把 `t` 限制在 `[0,1]` 内，因此当 `t < 0` 或 `t > 1` 时会产生外插效果（结果会超出 `[a,b]` 区间）。
  - 由于 `_easeFun`（包括自定义 `AnimationCurve` 和部分复杂缓动）可能输出略小于 0 或大于 1 的 `progress`，使用 `LerpUnclamped` 可以保留这些“超出 0~1”的细节，不会被硬截断。
  - 对于抖动、弹性、过冲等效果来说，这种不过度裁剪的插值方式能更真实地还原缓动曲线的形状。
