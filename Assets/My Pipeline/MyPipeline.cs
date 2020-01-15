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

	// 是否存在主光（方向光，有阴影，阴影强度为正，开启层级阴影）
	bool mainLightExists;

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

	// 阴影数据，x分量存阴影的强度，y分量存是否软阴影，z分量存是否是方向光
	static int shadowDataId = Shader.PropertyToID("_ShadowData");
	Vector4[] shadowData = new Vector4[maxVisibleLights];

	// 软硬阴影关键字
	const string shadowsHardKeyword = "_SHADOWS_HARD";
	const string shadowsSoftKeyword = "_SHADOWS_SOFT";

	// 阴影子图集的数量
	int shadowTileCount;

	// 阴影距离
	float shadowDistance;

	// 阴影全局变量，
	static int globalShadowDataId = Shader.PropertyToID("_GlobalShadowData");

	// 层级阴影
	int shadowCascades;
	Vector3 shadowCascadeSplit;

	// 层级阴影纹理
	static int cascadedShadowMapId = Shader.PropertyToID("_CascadedShadowMap");
	RenderTexture cascadedShadowMap;

	// 世界空间到层级阴影空间的转换矩阵
	static int worldToShadowCascadeMatricesId = Shader.PropertyToID("_WorldToShadowCascadeMatrices");
	Matrix4x4[] worldToShadowCascadeMatrices = new Matrix4x4[5];

	// 层级软硬阴影
	const string cascadedShadowsHardKeyword = "_CASCADED_SHADOWS_HARD";
	const string cascadedShadowsSoftKeyword = "_CASCADED_SHADOWS_SOFT";

	// 层级阴影纹理大小
	static int cascadedShadowMapSizeId = Shader.PropertyToID("_CascadedShadowMapSize");

	// 层级阴影强度
	static int cascadedShadoStrengthId = Shader.PropertyToID("_CascadedShadowStrength");

	// 层级阴影剔除球
	static int cascadeCullingSpheresId = Shader.PropertyToID("_CascadeCullingSpheres");
	Vector4[] cascadeCullingSpheres = new Vector4[4];

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

	// 后处理
	MyPostProcessingStack defaultStack;

	CommandBuffer postProcessingBuffer = new CommandBuffer
	{
		name = "Post-Processing"
	};

	static int cameraColorTextureId = Shader.PropertyToID("_CameraColorTexture");
	static int cameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

	// =============================================================================================================
	// 图像质量
	// =============================================================================================================

	// 图像降采样
	float renderScale;

	// MSAA
	int msaaSamples;

	// HDR
	bool allowHDR;

	public MyPipeline(bool dynamicBatching, bool instancing, MyPostProcessingStack defaultStack, int shadowMapSize, float shadowDistance, 
		int shadowCascades, Vector3 shadowCascadeSplit, float renderScale, int msaaSamples, bool allowHDR)
	{
		// Unity默认光强度是在Gamma空间中定义，即使我们工作在线性空间
		// 我们需要指定Unity将光强度理解为线性空间中的值
		GraphicsSettings.lightsUseLinearIntensity = true;

		if (SystemInfo.usesReversedZBuffer)
		{
			worldToShadowCascadeMatrices[4].m33 = 1f;
		}

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

		this.defaultStack = defaultStack;

		this.shadowMapSize = shadowMapSize;
		this.shadowDistance = shadowDistance;
		this.shadowCascades = shadowCascades;
		this.shadowCascadeSplit = shadowCascadeSplit;

		this.renderScale = renderScale;

		// QualitySettings会处理平台或者硬件不支持的情况，所以把值赋过去再取回。如果不支持MSAA，取回的值是0
		QualitySettings.antiAliasing = msaaSamples;
		this.msaaSamples = Mathf.Max(QualitySettings.antiAliasing, 1);

		this.allowHDR = allowHDR;
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

		// 设置阴影参数
		cullingParameters.shadowDistance = Mathf.Min(shadowDistance, camera.farClipPlane);

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

			if (mainLightExists)
			{
				RenderCascadedShadows(context);
			}
			else
			{
				cameraBuffer.DisableShaderKeyword(cascadedShadowsHardKeyword);
				cameraBuffer.DisableShaderKeyword(cascadedShadowsSoftKeyword);
			}

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

			cameraBuffer.DisableShaderKeyword(cascadedShadowsHardKeyword);
			cameraBuffer.DisableShaderKeyword(cascadedShadowsSoftKeyword);
			cameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
			cameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
		}

		// 设置unity_MatrixVP，以及一些其他属性
		context.SetupCameraProperties(camera);

		// 获取摄像机的定制后处理栈
		var myPipelineCamera = camera.GetComponent<MyPipelineCamera>();
		MyPostProcessingStack activeStack = myPipelineCamera ? myPipelineCamera.PostProcessingStack : defaultStack;

		// 只影响游戏摄像机
		bool scaledRendering = (renderScale < 1f || renderScale > 1f) && camera.cameraType == CameraType.Game;

		int renderWidth = camera.pixelWidth;
		int renderHeight = camera.pixelHeight;
		if (scaledRendering)
		{
			renderWidth = (int)(renderWidth * renderScale);
			renderHeight = (int)(renderHeight * renderScale);
		}

		// 摄像机是否开启MSAA
		int renderSamples = camera.allowMSAA ? msaaSamples : 1;

		// 开启渲染到纹理
		bool renderToTexture = scaledRendering || renderSamples > 1 || activeStack;

		// 是否需要深度纹理（深度纹理不支持MSAA）
		bool needsDepth = activeStack && activeStack.NeedsDepth;
		bool needsDirectDepth = needsDepth && renderSamples == 1; // 不使用MSAA
		bool needsDepthOnlyPass = needsDepth && renderSamples > 1; // 使用MSAA、

		// 纹理格式
		RenderTextureFormat format = allowHDR && camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;

		// 获取并设置渲染目标，用于后处理
		if (renderToTexture)
		{
			cameraBuffer.GetTemporaryRT(
				cameraColorTextureId, renderWidth, renderHeight, needsDirectDepth ? 0 : 24,
				FilterMode.Bilinear, format,
				RenderTextureReadWrite.Default, renderSamples
			);
			if (needsDepth)
			{
				cameraBuffer.GetTemporaryRT(
					cameraDepthTextureId, renderWidth, renderHeight, 24,
					FilterMode.Point, RenderTextureFormat.Depth,
					RenderTextureReadWrite.Linear, 1 // 1表示不使用MSAA
				);
			}
			if (needsDirectDepth)
			{
				cameraBuffer.SetRenderTarget(
					cameraColorTextureId,
					RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
					cameraDepthTextureId,
					RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
				);
			}
			else
			{
				cameraBuffer.SetRenderTarget(
					cameraColorTextureId,
					RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
				);
			}
		}

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
		// 仅在有可见光时设置，否则Unity会崩溃
		if (cull.visibleLights.Count > 0)
		{
			// 指定Unity为每个物体传输光索引数据
			drawSettings.rendererConfiguration = RendererConfiguration.PerObjectLightIndices8;
		}
		// 指定使用反射探针，如果场景中没有反射探针，则使用天空球的立方体贴图
		drawSettings.rendererConfiguration |= RendererConfiguration.PerObjectReflectionProbes | RendererConfiguration.PerObjectLightmaps;
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

		// 后处理
		if (activeStack)
		{
			// depth-only pass
			if (needsDepthOnlyPass)
			{
				var depthOnlyDrawSettings = new DrawRendererSettings(
					camera, new ShaderPassName("DepthOnly")
				)
				{
					flags = drawFlags
				};
				depthOnlyDrawSettings.sorting.flags = SortFlags.CommonOpaque;
				cameraBuffer.SetRenderTarget(
					cameraDepthTextureId,
					RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
				);
				cameraBuffer.ClearRenderTarget(true, false, Color.clear);
				context.ExecuteCommandBuffer(cameraBuffer);
				cameraBuffer.Clear();
				context.DrawRenderers(
					cull.visibleRenderers, ref depthOnlyDrawSettings, filterSettings
				);
			}

			activeStack.RenderAfterOpaque(
				postProcessingBuffer, cameraColorTextureId, cameraDepthTextureId,
				renderWidth, renderHeight, renderSamples, format
			);
			context.ExecuteCommandBuffer(postProcessingBuffer);
			postProcessingBuffer.Clear();

			if (needsDirectDepth)
			{
				cameraBuffer.SetRenderTarget(
					cameraColorTextureId,
					RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
					cameraDepthTextureId,
					RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
				);
			}
			else
			{
				cameraBuffer.SetRenderTarget(
					cameraColorTextureId,
					RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
				);
			}
			context.ExecuteCommandBuffer(cameraBuffer);
			cameraBuffer.Clear();
		}

		// 指定排序，从后往前
		drawSettings.sorting.flags = SortFlags.CommonTransparent;
		// 绘制透明物体，渲染队列为[2501，5000]
		filterSettings.renderQueueRange = RenderQueueRange.transparent;
		context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);

		DrawDefaultPipeline(context, camera);

		// 后处理
		if (renderToTexture)
		{
			if (activeStack)
			{
				activeStack.RenderAfterTransparent(
					postProcessingBuffer, cameraColorTextureId,
					cameraDepthTextureId, renderWidth, renderHeight, renderSamples, format
				);
				context.ExecuteCommandBuffer(postProcessingBuffer);
				postProcessingBuffer.Clear();
			}
			else
			{
				cameraBuffer.Blit(
					cameraColorTextureId, BuiltinRenderTextureType.CameraTarget
				);
			}
			cameraBuffer.ReleaseTemporaryRT(cameraColorTextureId);
			if (needsDepth)
			{
				cameraBuffer.ReleaseTemporaryRT(cameraDepthTextureId);
			}
		}

		cameraBuffer.EndSample("HY Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		// 提交指令
		context.Submit();

		// 释放阴影纹理
		if (shadowMap)
		{
			RenderTexture.ReleaseTemporary(shadowMap);
			shadowMap = null;
		}

		// 释放层级阴影纹理
		if (cascadedShadowMap)
		{
			RenderTexture.ReleaseTemporary(cascadedShadowMap);
			cascadedShadowMap = null;
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
		mainLightExists = false;

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

				shadow = ConfigureShadows(i, light.light);
				shadow.z = 1f; // z分量指示是否处理方向光

				// 如果存在主光
				if (i == 0 && shadow.x > 0f && shadowCascades > 0)
				{
					mainLightExists = true;

					// 层级阴影使用另一张阴影纹理
					shadowTileCount -= 1;
				}
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

					shadow = ConfigureShadows(i, light.light);
				}
			}

			visibleLightAttenuations[i] = attenuation;

			shadowData[i] = shadow;
		}

		// 如果光的数量超过maxVisibleLights，将超过的灯光的索引剔除掉
		if (mainLightExists || cull.visibleLights.Count > maxVisibleLights)
		{
			// 获取光索引数组
			int[] lightIndices = cull.GetLightIndexMap();

			if (mainLightExists)
			{
				// 将主光剔除掉，否则主光会被渲染两次
				// 这也意味着每个物体支持的像素光的数量变为5
				lightIndices[0] = -1;
			}

			for (int i = maxVisibleLights; i < cull.visibleLights.Count; i++)
			{
				// 索引为-1的光会被剔除掉
				lightIndices[i] = -1;
			}
			// 设置光索引数组
			cull.SetLightIndexMap(lightIndices);
		}
	}

	Vector4 ConfigureShadows(int lightIndex, Light shadowLight)
	{
		Vector4 shadow = Vector4.zero;
		Bounds shadowBounds;
		if (shadowLight.shadows != LightShadows.None && cull.GetShadowCasterBounds(lightIndex, out shadowBounds))
		{
			shadowTileCount += 1;

			// x分量存阴影的强度，y分量存是否软阴影
			shadow.x = shadowLight.shadowStrength;
			shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
		}
		return shadow;
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

		shadowMap = SetShadowRenderTarget();

		shadowBuffer.BeginSample("HY Render Shadows");

		// 设置全局变量
		shadowBuffer.SetGlobalVector(globalShadowDataId, new Vector4(tileScale, shadowDistance * shadowDistance));

		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();

		int tileIndex = 0;

		bool hardShadows = false;
		bool softShadows = false;

		// 如果存在主光，需要跳过
		for (int i = mainLightExists ? 1 : 0; i < cull.visibleLights.Count; i++)
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
			bool validShadows;
			if (shadowData[i].z > 0f)
			{
				validShadows =cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(
						i, 0, 1, Vector3.right, (int)tileSize,
						cull.visibleLights[i].light.shadowNearPlane,
						out viewMatrix, out projectionMatrix, out splitData);
			}
			else
			{
				validShadows = cull.ComputeSpotShadowMatricesAndCullingPrimitives(i, out viewMatrix, out projectionMatrix, out splitData);
			}
			if (!validShadows)
			{
				// 获取矩阵失败则强度置0
				shadowData[i].x = 0f;
				continue;
			}

			Vector2 tileOffset = ConfigureShadowTile(tileIndex, split, tileSize);
			// 存储地图集的偏移
			shadowData[i].z = tileOffset.x * tileScale;
			shadowData[i].w = tileOffset.y * tileScale;
			
			// 设置灯的视矩阵和投影矩阵
			shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			// 设置阴影偏移
			shadowBuffer.SetGlobalFloat(shadowBiasId, cull.visibleLights[i].light.shadowBias);
			context.ExecuteCommandBuffer(shadowBuffer);
			shadowBuffer.Clear();

			// 绘制阴影
			var shadowSettings = new DrawShadowsSettings(cull, i);
			// 设置方向光的剔除球，其他光不受影响
			shadowSettings.splitData.cullingSphere = splitData.cullingSphere;
			context.DrawShadows(ref shadowSettings);

			CalculateWorldToShadowMatrix(ref viewMatrix, ref projectionMatrix, out worldToShadowMatrices[i]);

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

		// 关闭裁剪
		shadowBuffer.DisableScissorRect();

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

	// 绘制层级阴影
	void RenderCascadedShadows(ScriptableRenderContext context)
	{
		float tileSize = shadowMapSize / 2;
		cascadedShadowMap = SetShadowRenderTarget();

		shadowBuffer.BeginSample("HY Render Cascaded Shadows");
		shadowBuffer.SetGlobalVector(globalShadowDataId, new Vector4(0f, shadowDistance * shadowDistance));
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();

		Light shadowLight = cull.visibleLights[0].light;
		shadowBuffer.SetGlobalFloat(shadowBiasId, shadowLight.shadowBias);
		var shadowSettings = new DrawShadowsSettings(cull, 0);
		var tileMatrix = Matrix4x4.identity;
		tileMatrix.m00 = tileMatrix.m11 = 0.5f;

		for (int i = 0; i < shadowCascades; i++)
		{
			Matrix4x4 viewMatrix, projectionMatrix;
			ShadowSplitData splitData;
			cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(
				0, i, shadowCascades, shadowCascadeSplit, (int)tileSize,
				shadowLight.shadowNearPlane,
				out viewMatrix, out projectionMatrix, out splitData
			);

			Vector2 tileOffset = ConfigureShadowTile(i, 2, tileSize);
			shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			context.ExecuteCommandBuffer(shadowBuffer);
			shadowBuffer.Clear();

			cascadeCullingSpheres[i] = shadowSettings.splitData.cullingSphere = splitData.cullingSphere;
			cascadeCullingSpheres[i].w *= splitData.cullingSphere.w;
			context.DrawShadows(ref shadowSettings);

			CalculateWorldToShadowMatrix(ref viewMatrix, ref projectionMatrix, out worldToShadowCascadeMatrices[i]);
			tileMatrix.m03 = tileOffset.x * 0.5f;
			tileMatrix.m13 = tileOffset.y * 0.5f;
			worldToShadowCascadeMatrices[i] = tileMatrix * worldToShadowCascadeMatrices[i];
		}

		shadowBuffer.DisableScissorRect();
		shadowBuffer.SetGlobalTexture(cascadedShadowMapId, cascadedShadowMap);
		shadowBuffer.SetGlobalVectorArray(cascadeCullingSpheresId, cascadeCullingSpheres);
		shadowBuffer.SetGlobalMatrixArray(worldToShadowCascadeMatricesId, worldToShadowCascadeMatrices);

		float invShadowMapSize = 1f / shadowMapSize;
		shadowBuffer.SetGlobalVector(cascadedShadowMapSizeId, new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));
		shadowBuffer.SetGlobalFloat(cascadedShadoStrengthId, shadowLight.shadowStrength);

		bool hard = shadowLight.shadows == LightShadows.Hard;
		CoreUtils.SetKeyword(shadowBuffer, cascadedShadowsHardKeyword, hard);
		CoreUtils.SetKeyword(shadowBuffer, cascadedShadowsSoftKeyword, !hard);

		shadowBuffer.EndSample("HY Render Cascaded Shadows");
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();
	}

	// 设置渲染目标
	RenderTexture SetShadowRenderTarget()
	{
		// 创建或者复用一个渲染纹理
		RenderTexture texture = RenderTexture.GetTemporary(shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap);

		texture.filterMode = FilterMode.Bilinear;
		texture.wrapMode = TextureWrapMode.Clamp;

		// 设置渲染目标，设置只清空深度通道
		CoreUtils.SetRenderTarget(shadowBuffer, texture,RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,ClearFlag.Depth);

		return texture;
	}

	// 计算阴影地图集参数
	Vector2 ConfigureShadowTile(int tileIndex, int split, float tileSize)
	{
		// 计算视口
		Vector2 tileOffset;
		tileOffset.x = tileIndex % split;
		tileOffset.y = tileIndex / split;
		// 设置视口
		var tileViewport = new Rect(tileOffset.x * tileSize, tileOffset.y * tileSize, tileSize, tileSize);
		shadowBuffer.SetViewport(tileViewport);
		// 使用裁剪，略微减少阴影子图集的大小，防止采样到临近的子图集
		shadowBuffer.EnableScissorRect(new Rect(tileViewport.x + 4f, tileViewport.y + 4f,tileSize - 8f, tileSize - 8f));

		return tileOffset;
	}

	// 世界空间到阴影空间的转换矩阵
	void CalculateWorldToShadowMatrix(ref Matrix4x4 viewMatrix, ref Matrix4x4 projectionMatrix,out Matrix4x4 worldToShadowMatrix)
	{
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

		worldToShadowMatrix = scaleOffset * (projectionMatrix * viewMatrix);
	}
}