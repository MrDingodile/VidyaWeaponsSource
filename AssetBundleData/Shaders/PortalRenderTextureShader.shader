Shader "Portal/RenderTextureShader"
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
				float4 screenPos : TEXCOORD2;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.uv2 = TRANSFORM_TEX(v.uv2, _Mask);
				o.screenPos = ComputeScreenPos(o.vertex);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				i.screenPos /= i.screenPos.w;
				//fixed4 col = tex2D(_MainTex, float2(i.screenPos.x, i.screenPos.y));

				float2 screenUV = float2(i.screenPos.x, i.screenPos.y);
				float4 DistortionTex = tex2D(_Distortion, TRANSFORM_TEX(i.uv2 + fixed2(_Animation * _Time.x, _Animation * _Time.x), _Distortion));
				float distortion = (DistortionTex.r * _DistortionPower);
				float2 distortedUV = screenUV + distortion - 0.15 * _DistortionPower;
				fixed4 col = tex2D(_MainTex, TRANSFORM_TEX(distortedUV, _MainTex)) * _Color;

				fixed4 tex = tex2D(_Mask, i.uv2);
				//col.a = tex.a;
				clip(tex.a - _Cutoff);

				return col;
			}
			ENDCG
		}
	}
}
