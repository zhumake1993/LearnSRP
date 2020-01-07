using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

// 渲染管线实例
// 尽管也可以继承接口IRenderPipeline并提供自己的实现，继承RenderPipeline会更方便
public class MyPipeline : RenderPipeline
{
	CullResults cull;

	CommandBuffer cameraBuffer = new CommandBuffer
	{
		// 指令缓冲的名字，会显示在Frame Debugger中
		name = "HY Render Camera"
	};

	// 用于绘制错误物体的材质
	Material errorMaterial;

	DrawRendererFlags drawFlags;

	public MyPipeline(bool dynamicBatching, bool instancing)
	{
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

		// 设置采样标志，用于在Frame Debugger中组织结构
		cameraBuffer.BeginSample("HY Render Camera");

		// 执行指令缓冲。这并不会立即执行指令，只是将指令拷贝到上下文的内部缓冲中
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		// 绘制设定
		// camera参数设定排序和剔除层，pass参数指定使用哪一个shader pass
		var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"));
		drawSettings.flags = drawFlags;
		// 只当排序，从前往后
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
}