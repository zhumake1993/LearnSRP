#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

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

// 定义UNITY_MATRIX_M，保证后续代码的一致性
#define UNITY_MATRIX_M unity_ObjectToWorld
// 该文件会重定义UNITY_MATRIX_M，因此必须放在我们自己定义的宏的后面
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

// GPU实例化常量缓冲
UNITY_INSTANCING_BUFFER_START(PerInstance)
UNITY_DEFINE_INSTANCED_PROP(float4, _Color) // 当实例化未开启时，等价于float4 _Color
UNITY_INSTANCING_BUFFER_END(PerInstance)

// 顶点着色器输入
struct VertexInput {
	float4 pos : POSITION;
	float3 normal : NORMAL;
	UNITY_VERTEX_INPUT_INSTANCE_ID // 实例的索引
};

// 片段着色器输入
struct VertexOutput {
	float4 clipPos : SV_POSITION;
	float3 normal : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID // 实例的索引
};

// 顶点着色器
VertexOutput LitPassVertex(VertexInput input) {
	VertexOutput output;

	// 使实例索引可见
	// 必须在UNITY_MATRIX_M之前使用
	UNITY_SETUP_INSTANCE_ID(input);

	// 传输实例索引
	UNITY_TRANSFER_INSTANCE_ID(input, output);

	// 通过显示指定第4位是1，可以让编译器优化
	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
	output.clipPos = mul(unity_MatrixVP, worldPos);
	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
	return output;
}

// 片段着色器
float4 LitPassFragment(VertexOutput input) : SV_TARGET{
	// 使实例索引可见
	// 必须在UNITY_ACCESS_INSTANCED_PROP之前使用
	UNITY_SETUP_INSTANCE_ID(input);

	input.normal = normalize(input.normal);

	// 使用GPU实例化的颜色
	float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;

	float3 color = input.normal;
	return float4(color, 1);
}

#endif // MYRP_LIT_INCLUDED