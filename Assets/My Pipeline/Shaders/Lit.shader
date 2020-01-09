Shader "My Pipeline/Lit"
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

			// 是否开启硬阴影
			#pragma multi_compile _ _SHADOWS_HARD

			// 是否开启软阴影
			#pragma multi_compile _ _SHADOWS_SOFT

			// 指定顶点着色器和片段着色器
			#pragma vertex LitPassVertex
			#pragma fragment LitPassFragment

			#include "../ShaderLibrary/Lit.hlsl"

			ENDHLSL
		}

		Pass {
			Tags {
				"LightMode" = "ShadowCaster"
			}

			HLSLPROGRAM

			#pragma target 3.5

			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling

			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment

			#include "../ShaderLibrary/ShadowCaster.hlsl"

			ENDHLSL
		}
	}
}