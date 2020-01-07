using UnityEngine;
using UnityEngine.Experimental.Rendering;

// 需要指定线性颜色空间

// 渲染管线资源
[CreateAssetMenu(menuName = "Rendering/My Pipeline")]
public class MyPipelineAsset : RenderPipelineAsset
{
	[SerializeField]
	bool dynamicBatching;

	[SerializeField]
	bool instancing;

	protected override IRenderPipeline InternalCreatePipeline()
	{
		return new MyPipeline(dynamicBatching, instancing);
	}
}