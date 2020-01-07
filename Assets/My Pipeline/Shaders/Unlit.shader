Shader "My Pipeline/Unlit"
{
	Properties
	{
		_Color("Color", Color) = (1, 1, 1, 1)
	}

	SubShader{

		Pass
		{
			// 默认shader使用GLSL，新的渲染管线使用HLSL
			HLSLPROGRAM

			// 指定目标着色器等级
			// LWRP中使用#pragma prefer_hlslcc gles指令来适配OpenGL ES 2，我们这里不这么做
			#pragma target 3.5
			
			// GPU实例化关键字
			// 这会产生两个shader变体，其中一个会定义INSTANCING_ON
			#pragma multi_compile_instancing

			// 指定均匀缩放，这样实例化缓冲中不会包含从世界空间到对象空间的转换矩阵
			#pragma instancing_options assumeuniformscaling

			// 指定顶点着色器和片段着色器
			#pragma vertex UnlitPassVertex
			#pragma fragment UnlitPassFragment

			#include "../ShaderLibrary/Unlit.hlsl"

			ENDHLSL
		}
	}
}