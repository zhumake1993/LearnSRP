#ifndef MYRP_SHADOWCASTER_INCLUDED
#define MYRP_SHADOWCASTER_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// 每帧更新
// 由于OpenGL不支持该功能，因此需要使用宏来适配
CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
CBUFFER_END

// 每个Draw Call更新
CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
CBUFFER_END

CBUFFER_START(_ShadowCasterBuffer)
	float _ShadowBias;
CBUFFER_END

// 定义UNITY_MATRIX_M，保证后续代码的一致性
#define UNITY_MATRIX_M unity_ObjectToWorld
// 该文件会重定义UNITY_MATRIX_M，因此必须放在我们自己定义的宏的后面
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

// 顶点着色器输入
struct VertexInput {
	float4 pos : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID // 实例的索引
};

// 片段着色器输入
struct VertexOutput {
	float4 clipPos : SV_POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID // 实例的索引
};

// 顶点着色器
VertexOutput ShadowCasterPassVertex(VertexInput input) {

	VertexOutput output;

	// 使实例索引可见
	// 必须在UNITY_MATRIX_M之前使用
	UNITY_SETUP_INSTANCE_ID(input);

	// 通过显示指定第4位是1，可以让编译器优化
	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
	output.clipPos = mul(unity_MatrixVP, worldPos);

	// 防止物体与近平面相交时产生阴影洞
#if UNITY_REVERSED_Z
	output.clipPos.z -= _ShadowBias;
	output.clipPos.z = min(output.clipPos.z, output.clipPos.w * UNITY_NEAR_CLIP_VALUE);
#else
	output.clipPos.z += _ShadowBias;
	output.clipPos.z = max(output.clipPos.z, output.clipPos.w * UNITY_NEAR_CLIP_VALUE);
#endif

	return output;
}

// 片段着色器
float4 ShadowCasterPassFragment(VertexOutput input) : SV_TARGET{

	return 0;
}

#endif // MYRP_SHADOWCASTER_INCLUDED