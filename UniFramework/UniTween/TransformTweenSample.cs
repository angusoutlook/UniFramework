using UnityEngine;
using UniFramework.Tween;

/// <summary>
/// Transform 位置 / 缩放 / 欧拉角组合补间示例。
/// 展示：Transform 扩展的 TweenPositionTo / TweenScaleTo / TweenAnglesTo 以及 SequenceNode。
/// </summary>
public class TransformTweenSample : MonoBehaviour
{
    public Transform target;

    void Start()
    {
        if (target == null)
            target = this.transform;

        // 1）位移到指定位置（本地坐标）
        Vector3Tween move = target
            .TweenPositionTo(
                duration: 1.5f,
                to: new Vector3(0f, 3f, 0f),
                relativeWorld: false,
                setRuntimeValue: true)
            .SetEase(TweenEase.Quad.EaseInOut);

        // 2）放大到 2 倍（本地缩放）
        Vector3Tween scale = target
            .TweenScaleTo(
                duration: 1.0f,
                to: Vector3.one * 2f,
                setRuntimeValue: true)
            .SetEase(TweenEase.Back.EaseOut);

        // 3）旋转到目标角（本地欧拉角）
        Vector3Tween angles = target
            .TweenAnglesTo(
                duration: 1.0f,
                to: new Vector3(0f, 180f, 0f),
                relativeWorld: false,
                setRuntimeValue: true)
            .SetEase(TweenEase.Sine.EaseInOut);

        // 顺序执行：位移 → 缩放 → 旋转（SequenceNode + AppendSequence）
        ITweenChain chain = new SequenceNode()
            .AppendSequence(move, scale, angles);

        gameObject.PlayTween(chain);
    }
}


