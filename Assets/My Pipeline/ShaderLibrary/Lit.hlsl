#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

// 最大光数量
#define MAX_VISIBLE_LIGHTS 16

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

// 每帧更新
// 由于OpenGL不支持该功能，因此需要使用宏来适配
CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
CBUFFER_END

// 每个Draw Call更新
CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
	float4 unity_LightIndicesOffsetAndCount; // y分量表示影响物体的光的数量
	float4 unity_4LightIndices0, unity_4LightIndices1;
CBUFFER_END

// 定义UNITY_MATRIX_M，保证后续代码的一致性
#define UNITY_MATRIX_M unity_ObjectToWorld
// 该文件会重定义UNITY_MATRIX_M，因此必须放在我们自己定义的宏的后面
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

// GPU实例化常量缓冲
UNITY_INSTANCING_BUFFER_START(PerInstance)
	UNITY_DEFINE_INSTANCED_PROP(float4, _Color) // 当实例化未开启时，等价于float4 _Color
UNITY_INSTANCING_BUFFER_END(PerInstance)

// 光数据数组
CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END

// 阴影缓冲
CBUFFER_START(_ShadowBuffer)
	float4x4 _WorldToShadowMatrices[MAX_VISIBLE_LIGHTS];
	float4 _ShadowData[MAX_VISIBLE_LIGHTS];
	float4 _ShadowMapSize;
CBUFFER_END

// 纹理资源
TEXTURE2D_SHADOW(_ShadowMap);

// 采样器
// 该采样器会在双线性插值之前进行比较操作，结果更准确
SAMPLER_CMP(sampler_ShadowMap);

// 计算硬阴影
float HardShadowAttenuation (float4 shadowPos) {
	return SAMPLE_TEXTURE2D_SHADOW(_ShadowMap, sampler_ShadowMap, shadowPos.xyz);
}

// 计算软阴影
float SoftShadowAttenuation(float4 shadowPos) {
	real tentWeights[9];
	real2 tentUVs[9];
	SampleShadow_ComputeSamples_Tent_5x5(
		_ShadowMapSize, shadowPos.xy, tentWeights, tentUVs
	);
	float attenuation = 0;
	for (int i = 0; i < 9; i++) {
		attenuation += tentWeights[i] * SAMPLE_TEXTURE2D_SHADOW(
			_ShadowMap, sampler_ShadowMap, float3(tentUVs[i].xy, shadowPos.z)
		);
	}
	return attenuation;
}

// 计算阴影
float ShadowAttenuation(int index, float3 worldPos) {

	// 如果不投射阴影，直接返回1
#if !defined(_SHADOWS_HARD) && !defined(_SHADOWS_SOFT)
	return 1.0;
#endif

	// 如果阴影强度不为正，直接返回1
	if (_ShadowData[index].x <= 0) {
		return 1.0;
	}

	float4 shadowPos = mul(_WorldToShadowMatrices[index], float4(worldPos, 1.0));

	// 我们需要NDC坐标，因此需要除以w分量
	shadowPos.xyz /= shadowPos.w;

	float attenuation;

	// 根据硬软阴影关键字分类计算
#if defined(_SHADOWS_HARD)
	#if defined(_SHADOWS_SOFT)
		if (_ShadowData[index].y == 0) {
			attenuation = HardShadowAttenuation(shadowPos);
		}
		else {
			attenuation = SoftShadowAttenuation(shadowPos);
		}
	#else
		attenuation = HardShadowAttenuation(shadowPos);
	#endif
#else
	attenuation = SoftShadowAttenuation(shadowPos);
#endif

	// 根据阴影强度插值
	return lerp(1, attenuation, _ShadowData[index].x);
}

// 计算漫反射光
float3 DiffuseLight(int index, float3 normal, float3 worldPos, float shadowAttenuation) {

	float3 lightColor = _VisibleLightColors[index].rgb;
	float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
	float4 lightAttenuation = _VisibleLightAttenuations[index];
	float3 spotDirection = _VisibleLightSpotDirections[index].xyz;

	// 如果是方向光，lightPositionOrDirection.w是0
	// 如果是点光，lightPositionOrDirection.w是1
	float3 lightVector = lightPositionOrDirection.xyz - worldPos * lightPositionOrDirection.w;

	// 兰伯特余弦定理
	float3 lightDirection = normalize(lightVector);
	float diffuse = saturate(dot(normal, lightDirection));

	// 计算点光的边界衰减
	// 对于方向光，lightAttenuation.x为0，故方向光不受边界衰减影响
	float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;
	rangeFade = saturate(1.0 - rangeFade * rangeFade);
	rangeFade *= rangeFade;

	// 计算聚光的边界衰减
	float spotFade = dot(spotDirection, lightDirection);
	spotFade = saturate(spotFade * lightAttenuation.z + lightAttenuation.w);
	spotFade *= spotFade;

	// 计算光衰减
	// 对于方向光，lightVector的模为1，故方向光不受衰减影响
	float distanceSqr = max(dot(lightVector, lightVector), 0.00001);

	diffuse *= shadowAttenuation * spotFade * rangeFade / distanceSqr;

	return diffuse * lightColor;
}

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
	float3 worldPos : TEXCOORD1;
	float3 vertexLighting : TEXCOORD2;
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

	// 因为假定均匀缩放，因此不需要使用逆转置矩阵
	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);

	output.worldPos = worldPos.xyz;

	// 计算顶点光
	// 顶点光不计算阴影
	output.vertexLighting = 0;
	for (int i = 4; i < min(unity_LightIndicesOffsetAndCount.y, 8); i++) {
		int lightIndex = unity_4LightIndices1[i - 4];
		output.vertexLighting += DiffuseLight(lightIndex, output.normal, output.worldPos, 1);
	}

	return output;
}

// 片段着色器
float4 LitPassFragment(VertexOutput input) : SV_TARGET{

	// 使实例索引可见
	// 必须在UNITY_ACCESS_INSTANCED_PROP之前使用
	UNITY_SETUP_INSTANCE_ID(input);

	// 法线插值后会丧失单位性
	input.normal = normalize(input.normal);

	// 使用GPU实例化的颜色
	float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;

	float3 diffuseLight = input.vertexLighting;

	// 如果循环不是很复杂的话，编译器会将其展开
	for (int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 4); i++) {
		int lightIndex = unity_4LightIndices0[i];
		float shadowAttenuation = ShadowAttenuation(lightIndex, input.worldPos);
		diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos, shadowAttenuation);
	}

	float3 color = diffuseLight * albedo;
	return float4(color, 1);
}

#endif // MYRP_LIT_INCLUDED