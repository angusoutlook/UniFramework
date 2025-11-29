using UnityEngine;
using UnityEngine.UI;
using UniFramework.Tween;

/// <summary>
/// 完整演示 TweenChainExtension 的示例：
/// Execute / Delay / Repeat / Until / AppendSequence / AppendParallel / SwitchToSequence。
/// </summary>
public class TweenChainFullSample : MonoBehaviour
{
    public Transform target;
    public RectTransform panel;
    public CanvasGroup canvasGroup;
    public Image image;

    private bool _conditionMet;

    void Start()
    {
        if (target == null)
            target = this.transform;

        // 1）起始：顺序链
        ITweenChain chain = new SequenceNode()

            // 1.1 执行节点（ExecuteNode）
            .Execute(() => Debug.Log("TweenChain Begin"))

            // 1.2 延迟 0.5 秒
            .Delay(0.5f)

            // 1.3 在 2 秒内每 0.5 秒触发一次（Repeat + duration）
            .Repeat(
                delay: 0f,
                interval: 0.5f,
                duration: 2f,
                trigger: () => Debug.Log("Repeat Trigger"))

            // 1.4 条件等待：直到 _conditionMet 为 true（UntilNode）
            .Until(() => _conditionMet);

        // 1.5 顺序追加：UI 面板移动 + CanvasGroup 渐显（仅在存在有效节点时调用）
        System.Collections.Generic.List<ITweenNode> seqNodes = new System.Collections.Generic.List<ITweenNode>();
        if (panel != null)
        {
            seqNodes.Add(panel.TweenAnchoredPositionTo(0.6f, new Vector2(0f, 200f)).SetEase(TweenEase.Quad.EaseOut));
        }
        if (canvasGroup != null)
        {
            seqNodes.Add(canvasGroup.TweenAlphaFrom(0.8f, 0f).SetEase(TweenEase.Sine.EaseIn));
        }
        if (seqNodes.Count > 0)
        {
            chain = chain.AppendSequence(seqNodes.ToArray());
        }

        // 1.6 并行执行：物体抖动 + 图片颜色变化（仅在存在有效节点时调用）
        System.Collections.Generic.List<ITweenNode> parallelNodes = new System.Collections.Generic.List<ITweenNode>();
        if (target != null)
        {
            parallelNodes.Add(target.ShakePosition(0.5f, new Vector3(0.3f, 0.3f, 0f)));
        }
        if (image != null)
        {
            parallelNodes.Add(image.TweenColorTo(0.5f, Color.red));
        }
        if (parallelNodes.Count > 0)
        {
            chain = chain.AppendParallel(parallelNodes.ToArray());
        }

        // 1.7 使用 SwitchToSequence 构建子链（仅当 target 可用时）
        if (target != null)
        {
            chain = chain
                .SwitchToSequence(
                    target.TweenScaleTo(0.4f, Vector3.one * 1.2f),
                    target.TweenScaleTo(0.4f, Vector3.one)
                )
                .Execute(() => Debug.Log("Inner Sequence Complete"));
        }
        else
        {
            chain = chain.Execute(() => Debug.Log("Inner Sequence Complete (no target)"));
        }

        gameObject.PlayTween(chain);
    }

    void Update()
    {
        // 示例：按下 Space 时触发条件满足，UntilNode 得以继续
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _conditionMet = true;
        }

        // 示例：按下 Escape 时，中途终止这个物体上的所有补间
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            UniTween.Abort(this.gameObject);
        }
    }
}


