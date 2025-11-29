using UnityEngine;
using UniFramework.Tween;

/// <summary>
/// 使用 Transform.ShakePosition 的抖动示例。
/// 展示：ShakePosition + TweenMath.Shake + PingPong 循环。
/// </summary>
public class TransformShakeSample : MonoBehaviour
{
    public Transform target;
    public Vector3 magnitude = new Vector3(0.5f, 0.5f, 0f);
    public float duration = 0.5f;

    void Start()
    {
        if (target == null)
            target = this.transform;

        Vector3Tween shake = target.ShakePosition(
            duration: duration,
            magnitude: magnitude,
            relativeWorld: false);

        gameObject.PlayTween(shake);
    }
}


