using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/My Post-Processing Stack")]
public class MyPostProcessingStack : ScriptableObject
{
	[SerializeField, Range(0, 10)]
	int blurStrength;

	[SerializeField]
	bool depthStripes;

	[SerializeField]
	bool toneMapping;

	[SerializeField, Range(1f, 100f)]
	float toneMappingRange = 100f;

	static Mesh fullScreenTriangle;

	static Material material;

	static int mainTexId = Shader.PropertyToID("_MainTex");

	static int tempTexId = Shader.PropertyToID("_MyPostProcessingStackTempTex");

	static int depthTexId = Shader.PropertyToID("_DepthTex");

	static int resolvedTexId = Shader.PropertyToID("_MyPostProcessingStackResolvedTex");

	// t4est
	static int dddddresolvedTexId = Shader.PropertyToID("_MyPostProcessingStackdddddResolvedTex");

	enum Pass { Copy, Blur, DepthStripes, ToneMapping };

	public bool NeedsDepth
	{
		get
		{
			return depthStripes;
		}
	}

	static void InitializeStatic()
	{
		if (fullScreenTriangle)
		{
			return;
		}
		fullScreenTriangle = new Mesh
		{
			name = "My Post-Processing Stack Full-Screen Triangle",
			vertices = new Vector3[] {
				new Vector3(-1f, -1f, 0f),
				new Vector3(-1f,  3f, 0f),
				new Vector3( 3f, -1f, 0f)
			},
			triangles = new int[] { 0, 1, 2 },
		};
		fullScreenTriangle.UploadMeshData(true);

		material =
			new Material(Shader.Find("Hidden/My Pipeline/PostEffectStack"))
			{
				name = "My Post-Processing Stack material",
				hideFlags = HideFlags.HideAndDontSave
			};
	}

	public void RenderAfterOpaque(
		CommandBuffer cb, int cameraColorId, int cameraDepthId,
		int width, int height, int samples, RenderTextureFormat format
	)
	{
		InitializeStatic();
		if (depthStripes)
		{
			DepthStripes(cb, cameraColorId, cameraDepthId, width, height, format);
		}
	}

	public void RenderAfterTransparent(
		CommandBuffer cb, int cameraColorId, int cameraDepthId,
		int width, int height, int samples, RenderTextureFormat format
	)
	{
		if (blurStrength > 0)
		{
			if (toneMapping || samples > 1)
			{
				// 使用了MSAA的情况下，从MS纹理中采样需要解析的操作
				// 在MS纹理上进行多次Blur操作会产生许多不必要的解析操作，这可以通过使用一个临时纹理来解决
				cb.GetTemporaryRT(
					resolvedTexId, width, height, 0, FilterMode.Bilinear
				);
				if (toneMapping)
				{
					ToneMapping(cb, cameraColorId, resolvedTexId);
				}
				else
				{
					Blit(cb, cameraColorId, resolvedTexId);
				}
				Blur(cb, resolvedTexId, width, height);
				cb.ReleaseTemporaryRT(resolvedTexId);
			}
			else if (toneMapping)
			{
				ToneMapping(cb, cameraColorId, BuiltinRenderTextureType.CameraTarget);
			}
			else
			{
				Blur(cb, cameraColorId, width, height);
			}
		}
		else
		{
			Blit(cb, cameraColorId, BuiltinRenderTextureType.CameraTarget);
		}
	}

	void Blit(
		CommandBuffer cb,
		RenderTargetIdentifier sourceId, RenderTargetIdentifier destinationId,
		Pass pass = Pass.Copy
	)
	{
		cb.SetGlobalTexture(mainTexId, sourceId);
		cb.SetRenderTarget(
			destinationId,
			RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
		);
		cb.DrawMesh(
			fullScreenTriangle, Matrix4x4.identity, material, 0, (int)pass
		);
	}

	void Blur(CommandBuffer cb, int cameraColorId, int width, int height)
	{
		cb.BeginSample("Blur");
		if (blurStrength == 1)
		{
			Blit(cb, cameraColorId, BuiltinRenderTextureType.CameraTarget, Pass.Blur);
			cb.EndSample("Blur");
			return;
		}
		cb.GetTemporaryRT(tempTexId, width, height, 0, FilterMode.Bilinear);
		int passesLeft;
		for (passesLeft = blurStrength; passesLeft > 2; passesLeft -= 2)
		{
			Blit(cb, cameraColorId, tempTexId, Pass.Blur);
			Blit(cb, tempTexId, cameraColorId, Pass.Blur);
		}
		if (passesLeft > 1) {
			Blit(cb, cameraColorId, tempTexId, Pass.Blur);
			Blit(cb, tempTexId, BuiltinRenderTextureType.CameraTarget, Pass.Blur);
		}
		else {
			Blit(cb, cameraColorId, BuiltinRenderTextureType.CameraTarget, Pass.Blur);
		}
		cb.ReleaseTemporaryRT(tempTexId);
		cb.EndSample("Blur");
	}

	void DepthStripes(
		CommandBuffer cb, int cameraColorId, int cameraDepthId,
		int width, int height, RenderTextureFormat format
	)
	{
		cb.BeginSample("Depth Stripes");
		cb.GetTemporaryRT(tempTexId, width, height, 0, FilterMode.Point, format);
		cb.SetGlobalTexture(depthTexId, cameraDepthId);
		Blit(cb, cameraColorId, tempTexId, Pass.DepthStripes);
		Blit(cb, tempTexId, cameraColorId);
		cb.ReleaseTemporaryRT(tempTexId);
		cb.EndSample("Depth Stripes");
	}

	void ToneMapping(CommandBuffer cb, RenderTargetIdentifier sourceId, RenderTargetIdentifier destinationId)
	{
		cb.BeginSample("Tone Mapping");
		cb.SetGlobalFloat("_ReinhardModifier", 1f / (toneMappingRange * toneMappingRange));
		Blit(cb, sourceId, destinationId, Pass.ToneMapping);
		cb.EndSample("Tone Mapping");
	}
}