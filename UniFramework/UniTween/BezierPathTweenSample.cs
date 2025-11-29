using UnityEngine;
using UniFramework.Tween;

/// <summary>
/// 使用 TweenMath.CubicBezier 构建三阶贝塞尔路径的位移动画示例。
/// 展示：SetLerp 自定义插值 + TweenEase 组合使用。
/// </summary>
public class BezierPathTweenSample : MonoBehaviour
{
    public Transform target;
    public Transform p1;
    public Transform c1;
    public Transform c2;
    public Transform p2;

    public float duration = 2f;

    void Start()
    {
        if (target == null)
            target = this.transform;

        if (p1 == null || c1 == null || c2 == null || p2 == null)
            return;

        // from/to 只是起终点，轨迹由 SetLerp 里的 CubicBezier 决定
        Vector3Tween moveOnBezier = Vector3Tween
            .Allocate(duration, p1.position, p2.position)
            .SetLerp((from, to, progress) =>
            {
                return TweenMath.CubicBezier(
                    p1.position, c1.position, c2.position, p2.position, progress);
            })
            .SetOnUpdate(pos => target.position = pos)
            .SetEase(TweenEase.Sine.EaseInOut);

        gameObject.PlayTween(moveOnBezier);
    }
}


