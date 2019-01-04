Shader "Portal/ClosedShader"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("Texture", 2D) = "white" {}
		_Distortion("Distortion", 2D) = "white" {}
		_DistortionPower("Distortion Power", Range(0, 0.4)) = 0
		_Mask("Mask", 2D) = "white" {}
		_Cutoff("Alpha cutoff", Range(0,1)) = 0.1
		_Animation("Animation Speed", Range(0,2)) = 0.0
	}
		SubShader
	{
		Tags{ "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		Lighting Off
		Cull Back
		ZWrite On
		ZTest Less
			Blend SrcAlpha OneMinusSrcAlpha

		Fog{ Mode Off }

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0 

			#include "UnityCG.cginc"

			fixed4 _Color;
			sampler2D _MainTex;	float4 _MainTex_ST;
			sampler2D _Mask; float4 _Mask_ST;
			fixed _Cutoff;
			fixed _Animation;
			uniform sampler2D _Distortion; uniform float4 _Distortion_ST;
			uniform float _DistortionPower;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				float2 uv2 : TEXCOORD1;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				half2 uv : TEXCOORD0;
				half2 uv2 : TEXCOORD1;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.uv2 = TRANSFORM_TEX(v.uv2, _Mask);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				float pct = (1 - _Color.a);

				float2 animatedUV = i.uv * 0.4 + fixed2(_Animation * (_CosTime.y * 0.3 + 0.7) * 4, _Animation * _Time.x);
				float4 DistortionTex = tex2D(_Distortion, TRANSFORM_TEX(animatedUV, _Distortion));
				float distortion = (DistortionTex.r * _DistortionPower);
				float2 distortedUV = i.uv * (1 - float2(pct, pct) * 0.2) + float2(pct, pct) * 0.1 + distortion - 0.15 * _DistortionPower;
				fixed4 col = tex2D(_MainTex, TRANSFORM_TEX(distortedUV, _MainTex));
				float p = pow(_Color.a, 5);
				float alpha = p * 1 + (1 - p) * col.r;

				fixed4 mask = tex2D(_Mask, i.uv2);
				float pct2 = pct * 3;
				if (pct2 > 1) pct = 1;
				float inv = (1 - col.r);
				float erode = inv * pct2;
				float cl = mask.a - erode - _Cutoff;
				clip(cl);
				col.rgb *= _Color.rgb;
				float a = pow(_Color.a, 1) * 2;
				if (a > 1) a = 1;
				col.a *= a * alpha;
				return col;
			}
			ENDCG
		}
	}
}
