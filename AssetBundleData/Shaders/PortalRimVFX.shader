Shader "Portal/PortalOpenVFX03" {
    Properties {
        _TintColor ("Color", Color) = (0.5,0.5,0.5,1)
        _FinalPower ("Final Power", Range(0, 4)) = 0
        _Distortion ("Distortion", 2D) = "white" {}
        _DistortionPower ("Distortion Power", Range(0, 0.4)) = 0
		_Speed("Speed", Range(0, 2)) = 0
        _Tex01 ("Tex 01", 2D) = "white" {}
        _Tex02 ("Tex 02", 2D) = "white" {}
        _Tex03 ("Tex 03", 2D) = "white" {}
		_Ramp("Fade out", 2D) = "white" {}
    }
    SubShader {
        Tags {
            "IgnoreProjector"="True"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }
        Pass {
            Name "FORWARD"
            Tags {
                "LightMode"="ForwardBase"
            }
            Blend One One
            Cull Off
            ZWrite Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_FORWARDBASE
            #include "UnityCG.cginc"
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma only_renderers d3d9 d3d11 glcore gles 
            #pragma target 3.0
            uniform float4 _TintColor;
            uniform float _FinalPower;
            uniform sampler2D _Distortion; uniform float4 _Distortion_ST;
            uniform float _DistortionPower;
			uniform float _Speed;
            uniform sampler2D _Tex01; uniform float4 _Tex01_ST;
            uniform sampler2D _Tex02; uniform float4 _Tex02_ST;
			uniform sampler2D _Tex03; uniform float4 _Tex03_ST;
            uniform sampler2D _Ramp; uniform float4 _Ramp_ST;
            struct VertexInput {
                float4 vertex : POSITION;
                float2 texcoord0 : TEXCOORD0;
                float4 vertexColor : COLOR;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float4 vertexColor : COLOR;
                UNITY_FOG_COORDS(1)
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.uv0 = v.texcoord0;
                o.vertexColor = v.vertexColor;
                o.pos = UnityObjectToClipPos( v.vertex );
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
////// Lighting:
////// Emissive:
                float4 node_6901 = _Time * _Speed;
                float4 _Distortion_var = tex2D(_Distortion,TRANSFORM_TEX(i.uv0, _Distortion));
                float node_5766 = (_Distortion_var.r*_DistortionPower);
                float2 node_6399 = ((i.uv0+node_6901.g*float2(0,0.5))+node_5766);
                float4 _MainTex = tex2D(_Tex01,TRANSFORM_TEX(node_6399, _Tex01));
                float node_7910 = 2.0;
                float2 node_8173 = ((i.uv0+node_6901.g*float2(0,1))+node_5766);
                float4 node_1726 = tex2D(_Tex02,TRANSFORM_TEX(node_8173, _Tex02));
                float node_6808 = (((_MainTex.r*node_7910)*(node_7910*node_1726.r))*1.2+-0.2);
                float2 node_2010 = (i.uv0+node_6901.g*float2(0,5));
                float4 node_8338 = tex2D(_Tex03,TRANSFORM_TEX(node_2010, _Tex03));
                float3 emissive = (saturate((saturate(node_6808)*_TintColor.rgb*i.vertexColor.a*_FinalPower*saturate((0.5+node_8338.r))))*i.vertexColor.a);
                float3 finalColor = emissive;
                fixed4 finalRGBA = fixed4(finalColor,1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, finalRGBA, fixed4(0,0,0,1));
				float4 ramp = tex2D(_Ramp, TRANSFORM_TEX(i.uv0 + float2(_Time.x * 0.3, 0), _Ramp));
				float4 ramp2 = tex2D(_Ramp, TRANSFORM_TEX(i.uv0 + float2(_Time.x * -0.35, 0), _Ramp));
				finalRGBA *= (ramp + ramp2 * 0.5) * (ramp2 + ramp * 0.5);
                return finalRGBA;
            }
            ENDCG
        }
    }
}
