using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

// 渲染管线实例
// 尽管也可以继承接口IRenderPipeline并提供自己的实现，继承RenderPipeline会更方便
public class MyPipeline : RenderPipeline
{
	// 光数据
	const int maxVisibleLights = 16;
	static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
	static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
	static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
	static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
	Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
	Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];

	static int lightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");

	// 剔除结果
	CullResults cull;

	// 指令缓冲
	CommandBuffer cameraBuffer = new CommandBuffer
	{
		// 指令缓冲的名字，会显示在Frame Debugger中
		name = "HY Render Camera"
	};

	// 用于绘制错误物体的材质
	Material errorMaterial;

	// 绘制设定的标志位
	DrawRendererFlags drawFlags;

	public MyPipeline(bool dynamicBatching, bool instancing)
	{
		// Unity默认光强度是在Gamma空间中定义，即使我们工作在线性空间
		// 我们需要指定Unity将光强度理解为线性空间中的值
		GraphicsSettings.lightsUseLinearIntensity = true;

		if (dynamicBatching)
		{
			// 开启动态批处理
			drawFlags = DrawRendererFlags.EnableDynamicBatching;
		}
		if (instancing)
		{
			// 开启GPU实例化
			// 如果同时开启动态批处理，Unity优先使用GPU实例化
			drawFlags |= DrawRendererFlags.EnableInstancing;
		}
	}

	public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
	{
		// RenderPipeline.Render不进行绘制工作，仅仅检查渲染对象是否合法。如果不合法则抛出异常
		base.Render(renderContext, cameras);

		foreach (var camera in cameras)
		{
			Render(renderContext, camera);
		}
	}

	void Render(ScriptableRenderContext context, Camera camera)
	{
		// 剔除参数
		ScriptableCullingParameters cullingParameters;
		// 获取剔除参数
		if (!CullResults.GetCullingParameters(camera, out cullingParameters))
		{
			// 如果相机设定非法，直接返回
			return;
		}

// 尽在编辑模式下
#if UNITY_EDITOR
		// 仅在渲染场景视图时。否则游戏视图中的UI元素会被渲染两次
		if (camera.cameraType == CameraType.SceneView)
		{
			// 当canvas的渲染被设置在世界空间时，UI元素不会出现在场景视图中
			// 需要手动指定以使其正确显示
			ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
		}
#endif

		// 剔除
		CullResults.Cull(ref cullingParameters, context, ref cull);

		// 设置unity_MatrixVP，以及一些其他属性
		context.SetupCameraProperties(camera);

		// 清空
		CameraClearFlags clearFlags = camera.clearFlags;
		cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

		if (cull.visibleLights.Count > 0)
		{
			ConfigureLights();
		}
		else
		{
			// 由于该值会被保留为上一个物体使用的值，因此需要手动设置
			cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
		}

		// 设置采样标志，用于在Frame Debugger中组织结构
		cameraBuffer.BeginSample("HY Render Camera");

		// 设置光数据
		cameraBuffer.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
		cameraBuffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions);
		cameraBuffer.SetGlobalVectorArray(visibleLightAttenuationsId, visibleLightAttenuations);
		cameraBuffer.SetGlobalVectorArray(visibleLightSpotDirectionsId, visibleLightSpotDirections);

		// 执行指令缓冲。这并不会立即执行指令，只是将指令拷贝到上下文的内部缓冲中
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		// 绘制设定
		// camera参数设定排序和剔除层，pass参数指定使用哪一个shader pass
		var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"))
		{
			flags = drawFlags
		};
		// 尽在有可见光时设置，否则Unity会崩溃
		if (cull.visibleLights.Count > 0)
		{
			// 指定Unity为每个物体传输光索引数据
			drawSettings.rendererConfiguration = RendererConfiguration.PerObjectLightIndices8;
		}
		// 指定排序，从前往后
		drawSettings.sorting.flags = SortFlags.CommonOpaque;

		// 过滤设定
		// true表示包括所有物体
		var filterSettings = new FilterRenderersSettings(true)
		{
			// 绘制不透明物体，渲染队列为[0，2500]
			renderQueueRange = RenderQueueRange.opaque
		};

		context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);

		// 绘制天空盒
		// camera参数仅用来判断是否绘制天空盒（根据camera的清空标志位）
		context.DrawSkybox(camera);

		// 指定排序，从后往前
		drawSettings.sorting.flags = SortFlags.CommonTransparent;
		// 绘制透明物体，渲染队列为[2501，5000]
		filterSettings.renderQueueRange = RenderQueueRange.transparent;
		context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);

		DrawDefaultPipeline(context, camera);

		cameraBuffer.EndSample("HY Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		// 提交指令
		context.Submit();
	}

	// 绘制默认管线
	[Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
	void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
	{
		if (errorMaterial == null)
		{
			Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
			errorMaterial = new Material(errorShader)
			{
				hideFlags = HideFlags.HideAndDontSave
			};
		}

		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("ForwardBase")
		);
		// 添加多个pass
		drawSettings.SetShaderPassName(1, new ShaderPassName("PrepassBase"));
		drawSettings.SetShaderPassName(2, new ShaderPassName("Always"));
		drawSettings.SetShaderPassName(3, new ShaderPassName("Vertex"));
		drawSettings.SetShaderPassName(4, new ShaderPassName("VertexLMRGBM"));
		drawSettings.SetShaderPassName(5, new ShaderPassName("VertexLM"));

		// 替换渲染时使用的材质
		drawSettings.SetOverrideMaterial(errorMaterial, 0);

		var filterSettings = new FilterRenderersSettings(true);

		context.DrawRenderers(
			cull.visibleRenderers, ref drawSettings, filterSettings
		);
	}

	// 计算光数据
	void ConfigureLights()
	{
		for (int i = 0; i < cull.visibleLights.Count; i++)
		{
			// 其他光被抛弃
			if (i == maxVisibleLights)
			{
				break;
			}

			VisibleLight light = cull.visibleLights[i];

			// finalColor是光的颜色和亮度的乘积，并且已经被转换到正确的颜色空间
			visibleLightColors[i] = light.finalColor;

			// 点光和聚光边界衰减
			Vector4 attenuation = Vector4.zero;

			// 保证计算聚光灯的边界衰减不会影响其他类型的灯
			attenuation.w = 1f;

			if (light.lightType == LightType.Directional)
			{
				// 获取光线方向
				Vector4 v = light.localToWorld.GetColumn(2);
				// shader中使用视点到光的方向，因此需要取反，w为0，不需要取反
				v.x = -v.x;
				v.y = -v.y;
				v.z = -v.z;
				visibleLightDirectionsOrPositions[i] = v;
			}
			else
			{
				// 获取光的位置
				visibleLightDirectionsOrPositions[i] = light.localToWorld.GetColumn(3);

				// 计算点光的边界衰减数据，放在attenuation的x分量中
				attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);

				// 如果是聚光灯
				if (light.lightType == LightType.Spot)
				{
					// 设定方向
					Vector4 v = light.localToWorld.GetColumn(2);
					v.x = -v.x;
					v.y = -v.y;
					v.z = -v.z;
					visibleLightSpotDirections[i] = v;

					// 计算聚光的边界衰减数据，并放在attenuation的z和w分量中
					float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
					float outerCos = Mathf.Cos(outerRad);
					float outerTan = Mathf.Tan(outerRad);
					float innerCos = Mathf.Cos(Mathf.Atan(((46f / 64f) * outerTan)));
					float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
					attenuation.z = 1f / angleRange;
					attenuation.w = -outerCos * attenuation.z;
				}
			}

			visibleLightAttenuations[i] = attenuation;
		}

		// 如果光的数量超过maxVisibleLights，将超过的灯光的索引剔除掉
		if (cull.visibleLights.Count > maxVisibleLights)
		{
			// 获取光索引数组
			int[] lightIndices = cull.GetLightIndexMap();
			for (int i = maxVisibleLights; i < cull.visibleLights.Count; i++)
			{
				// 索引为-1的光会被剔除掉
				lightIndices[i] = -1;
			}
			// 设置光索引数组
			cull.SetLightIndexMap(lightIndices);
		}
	}
}