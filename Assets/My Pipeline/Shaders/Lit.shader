Shader "My Pipeline/Lit"
{
	Properties
	{
		_Color("Color", Color) = (1, 1, 1, 1)
		_MainTex("Albedo & Alpha", 2D) = "white" {}
		[KeywordEnum(Off, On, Shadows)] _Clipping("Alpha Clipping", Float) = 0
		_Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
		_Metallic("Metallic", Range(0, 1)) = 0
		_Smoothness("Smoothness", Range(0, 1)) = 0.5
		[Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
		[Enum(Off, 0, On, 1)] _ZWrite("Z Write", Float) = 1
		[Toggle(_RECEIVE_SHADOWS)] _ReceiveShadows("Receive Shadows", Float) = 1
		[Toggle(_PREMULTIPLY_ALPHA)] _PremulAlpha("Premultiply Alpha", Float) = 0 //需要把source blend mode设置为one
	}

	SubShader{

		Pass
		{
			Blend [_SrcBlend] [_DstBlend]
			Cull [_Cull]
			ZWrite [_ZWrite]

			// 默认shader使用GLSL，新的渲染管线使用HLSL
			HLSLPROGRAM

			// 指定目标着色器等级
			// LWRP中使用#pragma prefer_hlslcc gles指令来适配OpenGL ES 2，我们这里不这么做
			#pragma target 3.5

			// GPU实例化关键字
			// 这会产生两个shader变体，其中一个会定义INSTANCING_ON
			#pragma multi_compile_instancing

			// 指定均匀缩放，这样实例化缓冲中不会包含从世界空间到对象空间的转换矩阵
			//#pragma instancing_options assumeuniformscaling

			// shader_feature指令确保只包含需要的shader变体
			#pragma shader_feature _CLIPPING_ON
			#pragma shader_feature _RECEIVE_SHADOWS

			// 光照贴图
			#pragma multi_compile _ LIGHTMAP_ON

			// 是否开启层级阴影，硬阴影，软阴影
			#pragma multi_compile _ _CASCADED_SHADOWS_HARD _CASCADED_SHADOWS_SOFT

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

			Cull [_Cull]

			HLSLPROGRAM

			#pragma target 3.5

			#pragma multi_compile_instancing
			//#pragma instancing_options assumeuniformscaling

			#pragma shader_feature _CLIPPING_OFF

			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment

			#include "../ShaderLibrary/ShadowCaster.hlsl"

			ENDHLSL
		}

		//Pass {
		//	Tags {
		//		"LightMode" = "Meta"
		//	}

		//	Cull Off

		//	HLSLPROGRAM

		//	#pragma vertex MetaPassVertex
		//	#pragma fragment MetaPassFragment

		//	#include "../ShaderLibrary/Meta.hlsl"

		//	ENDHLSL
		//}
	}

	CustomEditor "LitShaderGUI"
}