#ifndef MYRP_LIGHTING_INCLUDED
#define MYRP_LIGHTING_INCLUDED

// 天空球的立方体贴图
TEXTURECUBE(unity_SpecCube0);
TEXTURECUBE(unity_SpecCube1);
SAMPLER(samplerunity_SpecCube0);

// 光表面数据结构
struct LitSurface {
	float3 normal, position, viewDir;
	float3 diffuse, specular;
	float perceptualRoughness, roughness, fresnelStrength, reflectivity;
	bool perfectDiffuser;
};

// 获取光表面数据结构
LitSurface GetLitSurface(float3 normal, float3 position, float3 viewDir, float3 color, float metallic, float smoothness, bool perfectDiffuser = false) {
	LitSurface s;
	s.normal = normal;
	s.position = position;
	s.viewDir = viewDir;
	s.diffuse = color; // 物体本身的颜色
	if (perfectDiffuser) {
		s.reflectivity = 0.0;
		smoothness = 0.0;
		s.specular = 0.0;
	}
	else {
		// 如果是金属，color控制高光的颜色，而不是漫反射的颜色
		// 0.04用于绝缘体的高光项
		s.specular = lerp(0.04, color, metallic);
		s.reflectivity = lerp(0.04, 1.0, metallic);
		s.diffuse *= 1.0 - s.reflectivity; // 反射的光不会散射
	}
	s.perfectDiffuser = perfectDiffuser;
	s.perceptualRoughness = 1.0 - smoothness; // 感知粗糙度
	s.roughness = s.perceptualRoughness * s.perceptualRoughness; // 物理粗糙度，感知粗糙度的平方
	s.fresnelStrength = saturate(smoothness + s.reflectivity); // 反射率正作用于菲涅尔强度
	return s;
}

// 用于Meta
LitSurface GetLitSurfaceMeta(float3 color, float metallic, float smoothness) {
	return GetLitSurface(0, 0, 0, color, metallic, smoothness);
}

// 获取光表面数据结构
// 用于顶点着色器，只用来计算漫反射，所以假定后3个参数是0，1，0，0
LitSurface GetLitSurfaceVertex(float3 normal, float3 position) {
	return GetLitSurface(normal, position, 0, 1, 0, 0, true);
}

// 玻璃之类的材质是透明的，但仍然反射光，因此应该将透明度只作用于漫反射光
// 做法是先把alpha乘到diffuse中，而不使用通常的片段混合
// 然后再根据反射率调整alpha
void PremultiplyAlpha(inout LitSurface s, inout float alpha) {
	s.diffuse *= alpha;
	alpha = lerp(alpha, 1, s.reflectivity);
}

// 根据表面粗糙度调整反射高光
float3 ReflectEnvironment(LitSurface s, float3 environment) {
	if (s.perfectDiffuser) {
		return 0;
	}

	// 菲涅尔
	float fresnel = Pow4(1.0 - saturate(dot(s.normal, s.viewDir)));
	environment *= lerp(s.specular, s.fresnelStrength, fresnel);

	environment /= s.roughness * s.roughness + 1.0;
	return environment;
}

float3 LightSurface(LitSurface s, float3 lightDir) {
	float3 color = s.diffuse;

	// 使用修改版CookTorrance BRDF计算高光
	if (!s.perfectDiffuser) {
		float3 halfDir = SafeNormalize(lightDir + s.viewDir);
		float nh = saturate(dot(s.normal, halfDir));
		float lh = saturate(dot(lightDir, halfDir));
		float d = nh * nh * (s.roughness * s.roughness - 1.0) + 1.00001;
		float normalizationTerm = s.roughness * 4.0 + 2.0;
		float specularTerm = s.roughness * s.roughness;
		specularTerm /= (d * d) * max(0.1, lh * lh) * normalizationTerm;
		color += specularTerm * s.specular;
	}

	return color * saturate(dot(s.normal, lightDir)); // 兰伯特余弦定理
}



#endif // MYRP_LIGHTING_INCLUDED