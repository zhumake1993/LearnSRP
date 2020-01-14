using UnityEngine;

// ImageEffectAllowedInSceneView并不直接作用于图像特效，Unity会把激活的主摄像机（标签为MainCamera）的所有属性拷贝到场景摄像机中
[ImageEffectAllowedInSceneView, RequireComponent(typeof(Camera))]
public class MyPipelineCamera : MonoBehaviour
{

	[SerializeField]
	MyPostProcessingStack postProcessingStack = null;

	public MyPostProcessingStack PostProcessingStack
	{
		get
		{
			return postProcessingStack;
		}
	}
}