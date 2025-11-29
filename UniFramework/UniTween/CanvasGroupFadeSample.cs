using UnityEngine;
using UniFramework.Tween;

/// <summary>
/// CanvasGroup 透明度渐隐 / 渐显示例。
/// 展示：TweenAlphaFrom / TweenAlphaTo + PingPong。
/// </summary>
public class CanvasGroupFadeSample : MonoBehaviour
{
    public CanvasGroup target;
    public float duration = 0.8f;

    void Start()
    {
        if (target == null)
            target = GetComponent<CanvasGroup>();

        if (target == null)
            return;

        FloatTween fade = target
            .TweenAlphaFrom(duration, from: 0f, setRuntimeValue: true)
            .SetEase(TweenEase.Sine.EaseInOut)
            .SetLoop(ETweenLoop.PingPong, loopCount: 1);

        gameObject.PlayTween(fade);
    }
}


