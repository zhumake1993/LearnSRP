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

	// 在Editor中显示的时候会自动去掉前面的下划线
	public enum ShadowMapSize
	{
		_256 = 256,
		_512 = 512,
		_1024 = 1024,
		_2048 = 2048,
		_4096 = 4096
	}

	[SerializeField]
	ShadowMapSize shadowMapSize = ShadowMapSize._1024;

	protected override IRenderPipeline InternalCreatePipeline()
	{
		return new MyPipeline(dynamicBatching, instancing, (int)shadowMapSize);
	}
}