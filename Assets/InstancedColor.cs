using UnityEngine;

public class InstancedColor : MonoBehaviour
{
	[SerializeField]
	Color color = Color.white;

	static MaterialPropertyBlock propertyBlock;

	static int colorID = Shader.PropertyToID("_Color");

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
		GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
	}
}
