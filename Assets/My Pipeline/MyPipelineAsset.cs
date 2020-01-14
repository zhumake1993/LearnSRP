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

	[SerializeField]
	float shadowDistance = 100f;

	public enum ShadowCascades
	{
		Zero = 0,
		Two = 2,
		Four = 4
	}

	[SerializeField]
	ShadowCascades shadowCascades = ShadowCascades.Four;

	[SerializeField, HideInInspector]
	float twoCascadesSplit = 0.25f;

	[SerializeField, HideInInspector]
	Vector3 fourCascadesSplit = new Vector3(0.067f, 0.2f, 0.467f);

	[SerializeField]
	MyPostProcessingStack defaultStack = null;

	protected override IRenderPipeline InternalCreatePipeline()
	{
		Vector3 shadowCascadeSplit = shadowCascades == ShadowCascades.Four ? fourCascadesSplit : new Vector3(twoCascadesSplit, 0f);

		return new MyPipeline(dynamicBatching, instancing, defaultStack, (int)shadowMapSize, shadowDistance, (int)shadowCascades, shadowCascadeSplit);
	}
}