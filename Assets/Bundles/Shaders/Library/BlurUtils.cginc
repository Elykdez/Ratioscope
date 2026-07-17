#ifndef BLUR_UTILS_INCLUDED
#define BLUR_UTILS_INCLUDED
#include "_Meta.cginc"

#ifndef KERNEL_SIZE
	#define KERNEL_SIZE 35
#endif

// 1 - 19
#if defined(RADIUS_1)
	#define RADIUS 1
#elif defined(RADIUS_3)
	#define RADIUS 3
#elif defined(RADIUS_5)
	#define RADIUS 5
#elif defined(RADIUS_7)
	#define RADIUS 7
#elif defined(RADIUS_9)
	#define RADIUS 9
#elif defined(RADIUS_11)
	#define RADIUS 11
#elif defined(RADIUS_13)
	#define RADIUS 13
#elif defined(RADIUS_15)
	#define RADIUS 15
#elif defined(RADIUS_17)
	#define RADIUS 17
#elif defined(RADIUS_19)
	#define RADIUS 19
#else
	#define RADIUS 5 // Default
#endif

// 1d
float gauss(float x, float sigma)
{
	return 1.0f / (2.0f * PI * sigma * sigma) * exp( - (x * x) / (2.0f * sigma * sigma));
}

// 2d
float gauss(float x, float y, float sigma)
{
	return 1.0f / (2.0f * PI * sigma * sigma) * exp( - (x * x + y * y) / (2.0f * sigma * sigma));
}

// Expands edge in an xy area
float4 DilateEdge(TEXTURE2D_PARAM(tex, texSampler), float2 uv, float4 texelSize)
{
	float4 maxCol = SAMPLE_TEXTURE2D(tex, texSampler, uv);
	UNITY_LOOP
	for (int y = -RADIUS; y <= RADIUS; ++y)
	{
		UNITY_LOOP
		for (int x = -RADIUS; x <= RADIUS; ++x)
		{
			float2 offset = float2(x, y) * texelSize.xy;
			float4 sampleCol = SAMPLE_TEXTURE2D(tex, texSampler, uv + offset);
			if (sampleCol.a > maxCol.a)
			{
				maxCol = sampleCol;
			}
		}
	}
	return maxCol;
}


// Linear Gaussian blur, generally done 2 times in u and v directions.
float4 LinearGaussianBlur(TEXTURE2D_PARAM(tex, texSampler), float2 uv, float4 texelSize, float sigma, float2 dir)
{
	float3 rgbAccum = 0;
	float alphaAccum = 0;
	float weightSum = 0;

	UNITY_UNROLL// Cant loop because of tex2D
	for (int i = -KERNEL_SIZE / 2; i <= KERNEL_SIZE / 2; i += 2)
	{
		float2 offset = float2(i + 0.5f, i + 0.5f) * texelSize.xy * dir;
		float2 sampleUV = uv + offset;
		// Gaussian bell curve
		float weight = gauss(i, sigma) + gauss(i + 1, sigma);

		float4 sample = SAMPLE_TEXTURE2D(tex, texSampler, sampleUV);
		// Blur color regardless of alpha
		rgbAccum += sample.rgb * weight;
		alphaAccum += sample.a * weight;
		weightSum += weight;
	}

	return float4(rgbAccum / weightSum, alphaAccum / weightSum);
}

// Less costy than Gaussian blur for fast blurring by calling multiple times.
// https://community.arm.com/cfs-file/__key/communityserver-blogs-components-weblogfiles/00-00-00-20-66/siggraph2015_2D00_mmg_2D00_marius_2D00_notes.pdf
float4 KawaseBlur(TEXTURE2D_PARAM(tex, texSampler), float2 uv, float4 texelSize, int pixelOffset)
{
	float4 o = 0;
	o += SAMPLE_TEXTURE2D(tex, texSampler, uv + (float2(pixelOffset +0.5, pixelOffset +0.5) * texelSize)) * 0.25;
	o += SAMPLE_TEXTURE2D(tex, texSampler, uv + (float2(-pixelOffset -0.5, pixelOffset +0.5) * texelSize)) * 0.25;
	o += SAMPLE_TEXTURE2D(tex, texSampler, uv + (float2(-pixelOffset -0.5, -pixelOffset -0.5) * texelSize)) * 0.25;
	o += SAMPLE_TEXTURE2D(tex, texSampler, uv + (float2(pixelOffset +0.5, -pixelOffset -0.5) * texelSize)) * 0.25;
	return o;
}

// Gaussian-like small 3x3 blur for textures
float4 SampleTexBlur(TEXTURE2D_PARAM(tex, texSampler), float2 uv, float2 texelSize)
{
	// Gaussian weights (symmetric)
	const float w[3] = {
		0.27901, 0.44198, 0.27901
	};

	float4 colorSum = float4(0, 0, 0, 0);
	float weightSum = EPSILON;

	UNITY_UNROLL
	for (int x = -1; x <= 1; x++)
	{
		UNITY_UNROLL
		for (int y = -1; y <= 1; y++)
		{
			float wx = w[abs(x)];
			float wy = w[abs(y)];
			float wxy = wx * wy;

			float2 offset = float2(x, y) * texelSize;
			colorSum += SAMPLE_TEXTURE2D(tex, texSampler, uv + offset) * wxy;
			weightSum += wxy;
		}
	}

	return colorSum / weightSum;
}

#endif // BLUR_UTILS_INCLUDED