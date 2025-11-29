using UnityEngine;
using UnityEngine.UI;
using UniFramework.Tween;

/// <summary>
/// Image / Text / SpriteRenderer 颜色补间示例。
/// 展示：UI.Image / UI.Text / SpriteRenderer 的 TweenColorTo / TweenColorFrom，并行执行。
/// </summary>
public class ColorTweenSample : MonoBehaviour
{
    public Image image;
    public Text label;
    public SpriteRenderer spriteRenderer;

    void Start()
    {
        // Image 颜色变为红色
        ColorTween imgTween = null;
        if (image != null)
        {
            imgTween = image
                .TweenColorTo(1f, Color.red)
                .SetEase(TweenEase.Expo.EaseOut);
        }

        // Text 颜色变为绿色
        ColorTween textTween = null;
        if (label != null)
        {
            textTween = label
                .TweenColorTo(1f, Color.green)
                .SetEase(TweenEase.Circ.EaseInOut);
        }

        // SpriteRenderer 从透明变为原色
        ColorTween spriteTween = null;
        if (spriteRenderer != null)
        {
            spriteTween = spriteRenderer
                .TweenColorFrom(0.8f, Color.clear)
                .SetEase(TweenEase.Bounce.EaseOut);
        }

        // 并行执行所有有效 Tween
        ParallelNode parallel = new ParallelNode();
        if (imgTween != null) parallel.AddNode(imgTween);
        if (textTween != null) parallel.AddNode(textTween);
        if (spriteTween != null) parallel.AddNode(spriteTween);

        if (parallel != null)
        {
            gameObject.PlayTween(parallel);
        }
    }
}


