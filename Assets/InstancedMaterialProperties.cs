using UnityEngine;

public class InstancedMaterialProperties : MonoBehaviour
{
	[SerializeField]
	Color color = Color.white;

	[SerializeField, Range(0f, 1f)]
	float metallic;

	[SerializeField, Range(0f, 1f)]
	float smoothness = 0.5f;

	static MaterialPropertyBlock propertyBlock;

	static int colorID = Shader.PropertyToID("_Color");
	static int metallicId = Shader.PropertyToID("_Metallic");
	static int smoothnessId = Shader.PropertyToID("_Smoothness");

	void Awake()
	{
		OnValidate();
	}

	// 在编辑器模式下，当组件被载入或者修改时，会调用该方法
	void OnValidate()
	{
		if (propertyBlock == null)
		{
			propertyBlock = new MaterialPropertyBlock();
		}
		propertyBlock.SetColor(colorID, color);
		propertyBlock.SetFloat(metallicId, metallic);
		propertyBlock.SetFloat(smoothnessId, smoothness);
		GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
	}
}
