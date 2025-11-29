using UnityEngine;
using UniFramework.Tween;

/// <summary>
/// RectTransform.anchoredPosition 补间示例。
/// 展示：TweenAnchoredPositionTo。
/// </summary>
public class RectTransformTweenSample : MonoBehaviour
{
    public RectTransform panel;
    public Vector2 targetPosition = Vector2.zero;
    public float duration = 0.6f;

    void Start()
    {
        if (panel == null)
            panel = GetComponent<RectTransform>();

        if (panel == null)
            return;

        Vector2Tween tween = panel
            .TweenAnchoredPositionTo(duration, targetPosition)
            .SetEase(TweenEase.Quart.EaseOut);

        gameObject.PlayTween(tween);
    }
}


