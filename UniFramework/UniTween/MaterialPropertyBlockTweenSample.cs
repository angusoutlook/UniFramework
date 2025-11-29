using UnityEngine;
using UniFramework.Tween;

/// <summary>
/// MaterialPropertyBlock 上颜色 / 浮点属性补间示例。
/// 展示：TweenColorValue / TweenFloatValue，并在 Update 回写到 Renderer。
/// </summary>
public class MaterialPropertyBlockTweenSample : MonoBehaviour
{
    public Renderer targetRenderer;
    public string colorProperty = "_TintColor";
    public string floatProperty = "_Glow";

    private MaterialPropertyBlock _mpb;

    void Start()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer == null)
            return;

        _mpb = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(_mpb);

        // 颜色属性补间
        ColorTween colorTween = _mpb.TweenColorValue(
            property: colorProperty,
            duration: 1.5f,
            from: Color.white,
            to: Color.cyan);

        // 浮点属性补间
        FloatTween floatTween = _mpb.TweenFloatValue(
            property: floatProperty,
            duration: 1.5f,
            from: 0f,
            to: 2f);

        // 每帧把 mpb 回写到 renderer
        colorTween.SetOnUpdate(_ =>
        {
            targetRenderer.SetPropertyBlock(_mpb);
        });
        floatTween.SetOnUpdate(_ =>
        {
            targetRenderer.SetPropertyBlock(_mpb);
        });

        ITweenChain chain = new ParallelNode();
        chain.Append(colorTween);
        chain.Append(floatTween);

        gameObject.PlayTween(chain);
    }
}


