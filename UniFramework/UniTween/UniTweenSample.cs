using UnityEngine;
using UniFramework.Tween;

public class UniTweenSample : MonoBehaviour
{
    public Transform target;

    void Awake()
    {
        // 一般在游戏入口初始化一次即可（如果项目里还没初始化的话）
        UniTween.Initalize();
    }

    void Start()
    {
        if (target == null)
            target = this.transform;

        // 1. 构建一个顺序补间链
        ITweenChain chain = new SequenceNode()
            // 第一步：立即执行一段逻辑
            .Execute(() =>
            {
                Debug.Log("Sequence Begin");
            })
            // 第二步：等待 0.5 秒
            .Delay(0.5f)
            // 第三步：并行动画：位移动画 + 缩放动画
            .AppendParallel(
                // 3.1 把物体在 1 秒内移动到 (3,0,0)
                target.TweenPositionTo(
                    duration: 1f,
                    to: new Vector3(3f, 0f, 0f),
                    relativeWorld: false,
                    setRuntimeValue: true
                )
                .SetEase(TweenEase.Quad.EaseInOut),

                // 3.2 同时在 1 秒内从当前缩放缩放到 2 倍，并做 PingPong 循环一次（往返一次）
                target.TweenScaleTo(
                    duration: 1f,
                    to: Vector3.one * 2f,
                    setRuntimeValue: true
                )
                .SetLoop(ETweenLoop.PingPong, loopCount: 1)
                .SetEase(TweenEase.Sine.EaseInOut)
            )
            // 第四步：再等待 0.3 秒
            .Delay(0.3f)
            // 第五步：执行收尾逻辑
            .Execute(() =>
            {
                Debug.Log("Sequence Complete");
            });

        // 2. 播放补间链，并绑定到当前 GameObject
        TweenHandle handle = UniTween.Play(chain, this.gameObject);

        // 3. 可选：在补间结束时回调
        handle.OnDispose = () =>
        {
            Debug.Log("TweenHandle disposed.");
        };
    }

    // 可选：演示中途终止
    void Update()
    {
        // 示例：按下 Escape 键时，关闭当前 GameObject 下所有补间
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            UniTween.Abort(this.gameObject);
        }
    }
}