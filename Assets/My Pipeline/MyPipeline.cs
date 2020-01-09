using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

// 渲染管线实例
// 尽管也可以继承接口IRenderPipeline并提供自己的实现，继承RenderPipeline会更方便
public class MyPipeline : RenderPipeline
{
// =============================================================================================================
// 光
// =============================================================================================================

	// 最大光数量
	const int maxVisibleLights = 16;

	// 光颜色和亮度的乘积
	static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];

	// 方向光的方向，点光和聚光的位置
	static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];

	// 光的边界衰减，点光存在x分量中，聚光存在z和w分量中
	static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
	Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];

	// 聚光的方向
	static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
	Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];

	// y分量表示影响物体的光的数量
	static int lightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");

// =============================================================================================================
// 阴影
// =============================================================================================================

	// 渲染阴影指令缓冲
	CommandBuffer shadowBuffer = new CommandBuffer
	{
		name = "HY Render Shadows"
	};

	// 阴影纹理
	static int shadowMapId = Shader.PropertyToID("_ShadowMap");
	RenderTexture shadowMap;

	// 世界空间到阴影空间的转换矩阵
	static int worldToShadowMatricesId = Shader.PropertyToID("_WorldToShadowMatrices");
	Matrix4x4[] worldToShadowMatrices = new Matrix4x4[maxVisibleLights];

	// 阴影纹理大小
	static int shadowMapSizeId = Shader.PropertyToID("_ShadowMapSize");
	int shadowMapSize;

	// 阴影偏移
	static int shadowBiasId = Shader.PropertyToID("_ShadowBias");

	// 阴影数据，x分量存阴影的强度，y分量存是否软阴影
	static int shadowDataId = Shader.PropertyToID("_ShadowData");
	Vector4[] shadowData = new Vector4[maxVisibleLights];

	// 软硬阴影关键字
	const string shadowsHardKeyword = "_SHADOWS_HARD";
	const string shadowsSoftKeyword = "_SHADOWS_SOFT";

	// 阴影子图集的数量
	int shadowTileCount;

// =============================================================================================================
// 渲染
// =============================================================================================================

	// 剔除结果
	CullResults cull;

	// 摄像机渲染指令缓冲
	CommandBuffer cameraBuffer = new CommandBuffer
	{
		name = "HY Render Camera"
	};

	// 用于绘制错误物体的材质
	Material errorMaterial;

	// 绘制设定的标志位
	DrawRendererFlags drawFlags;

	public MyPipeline(bool dynamicBatching, bool instancing, int shadowMapSize)
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

		this.shadowMapSize = shadowMapSize;
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

// 仅在编辑模式下
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

		if (cull.visibleLights.Count > 0)
		{
			ConfigureLights();
			if (shadowTileCount > 0)
			{
				RenderShadows(context);
			}
			else
			{
				cameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
				cameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
			}
		}
		else
		{
			// 由于该值会被保留为上一个物体使用的值，因此需要手动设置
			cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);

			cameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
			cameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
		}

		// 设置unity_MatrixVP，以及一些其他属性
		context.SetupCameraProperties(camera);

		// 清空
		CameraClearFlags clearFlags = camera.clearFlags;
		cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

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

		// 释放渲染纹理
		if (shadowMap)
		{
			RenderTexture.ReleaseTemporary(shadowMap);
			shadowMap = null;
		}
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
		shadowTileCount = 0;

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

			// 点光和聚光的边界衰减
			Vector4 attenuation = Vector4.zero;

			// 保证计算聚光灯的边界衰减不会影响其他类型的灯
			attenuation.w = 1f;

			// 
			Vector4 shadow = Vector4.zero;

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

					// x分量存阴影的强度
					// y分量存是否软阴影
					Light shadowLight = light.light;
					Bounds shadowBounds;
					if (shadowLight.shadows != LightShadows.None && cull.GetShadowCasterBounds(i, out shadowBounds))
					{
						shadowTileCount += 1;

						// 当阴影发射器和阴影接受器
						shadow.x = shadowLight.shadowStrength;
						shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
					}
				}
			}

			visibleLightAttenuations[i] = attenuation;

			shadowData[i] = shadow;
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

	// 绘制ShadowMap
	void RenderShadows(ScriptableRenderContext context)
	{
		// 计算阴影图集的划分尺度
		int split;
		if (shadowTileCount <= 1)
		{
			split = 1;
		}
		else if (shadowTileCount <= 4)
		{
			split = 2;
		}
		else if (shadowTileCount <= 9)
		{
			split = 3;
		}
		else
		{
			split = 4;
		}

		// 子图集的大小
		float tileSize = shadowMapSize / split;
		float tileScale = 1f / split;
		Rect tileViewport = new Rect(0f, 0f, tileSize, tileSize);

		// 创建或者复用一个渲染纹理
		shadowMap = RenderTexture.GetTemporary(shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap);

		shadowMap.filterMode = FilterMode.Bilinear;
		shadowMap.wrapMode = TextureWrapMode.Clamp;

		// 设置渲染目标，设置只清空深度通道
		CoreUtils.SetRenderTarget(shadowBuffer, shadowMap, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.Depth);

		shadowBuffer.BeginSample("HY Render Shadows");
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();

		int tileIndex = 0;

		bool hardShadows = false;
		bool softShadows = false;

		for (int i = 0; i < cull.visibleLights.Count; i++)
		{
			if (i == maxVisibleLights)
			{
				break;
			}

			// 强度不为正，直接跳过
			if (shadowData[i].x <= 0f)
			{
				continue;
			}

			// 获取灯的视矩阵和投影矩阵
			Matrix4x4 viewMatrix, projectionMatrix;
			ShadowSplitData splitData;
			if (!cull.ComputeSpotShadowMatricesAndCullingPrimitives(i, out viewMatrix, out projectionMatrix, out splitData))
			{
				// 获取矩阵失败则强度置0
				shadowData[i].x = 0f;
				continue;
			}

			// 计算视口
			float tileOffsetX = tileIndex % split;
			float tileOffsetY = tileIndex / split;
			tileViewport.x = tileOffsetX * tileSize;
			tileViewport.y = tileOffsetY * tileSize;
			if (split > 1)
			{
				// 设置视口
				shadowBuffer.SetViewport(tileViewport);
				// 使用裁剪，略微减少阴影子图集的大小，防止采样到临近的子图集
				shadowBuffer.EnableScissorRect(new Rect(tileViewport.x + 4f, tileViewport.y + 4f, tileSize - 8f, tileSize - 8f));
			}
			
			// 设置灯的视矩阵和投影矩阵
			shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			// 设置阴影偏移
			shadowBuffer.SetGlobalFloat(shadowBiasId, cull.visibleLights[i].light.shadowBias);
			context.ExecuteCommandBuffer(shadowBuffer);
			shadowBuffer.Clear();

			// 绘制阴影
			var shadowSettings = new DrawShadowsSettings(cull, i);
			context.DrawShadows(ref shadowSettings);

			// 计算世界空间到阴影空间的转换矩阵
			if (SystemInfo.usesReversedZBuffer)
			{
				projectionMatrix.m20 = -projectionMatrix.m20;
				projectionMatrix.m21 = -projectionMatrix.m21;
				projectionMatrix.m22 = -projectionMatrix.m22;
				projectionMatrix.m23 = -projectionMatrix.m23;
			}
			// 剪裁空间时-1到1，纹理坐标和深度是0到1，因此需要转换。将转换矩阵直接乘到世界空间到阴影空间的转换矩阵中
			var scaleOffset = Matrix4x4.identity;
			scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
			scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;

			worldToShadowMatrices[i] = scaleOffset * (projectionMatrix * viewMatrix);

			if (split > 1)
			{
				// 修改矩阵以采样到正确的子图集
				var tileMatrix = Matrix4x4.identity;
				tileMatrix.m00 = tileMatrix.m11 = tileScale;
				tileMatrix.m03 = tileOffsetX * tileScale;
				tileMatrix.m13 = tileOffsetY * tileScale;
				worldToShadowMatrices[i] = tileMatrix * worldToShadowMatrices[i];
			}

			tileIndex += 1;

			if (shadowData[i].y <= 0f)
			{
				hardShadows = true;
			}
			else
			{
				softShadows = true;
			}
		}

		if (split > 1)
		{
			// 关闭裁剪
			shadowBuffer.DisableScissorRect();
		}

		// 设置ShadowMap
		shadowBuffer.SetGlobalTexture(shadowMapId, shadowMap);

		// 设置世界空间到阴影空间的转换矩阵
		shadowBuffer.SetGlobalMatrixArray(worldToShadowMatricesId, worldToShadowMatrices);

		// 设置阴影数据
		shadowBuffer.SetGlobalVectorArray(shadowDataId, shadowData);

		// 设置阴影纹理大小
		float invShadowMapSize = 1f / shadowMapSize;
		shadowBuffer.SetGlobalVector(shadowMapSizeId, new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));

		// 设置阴影关键字
		CoreUtils.SetKeyword(shadowBuffer, shadowsHardKeyword, hardShadows);
		CoreUtils.SetKeyword(shadowBuffer, shadowsSoftKeyword, softShadows);

		shadowBuffer.EndSample("HY Render Shadows");
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();
	}
}