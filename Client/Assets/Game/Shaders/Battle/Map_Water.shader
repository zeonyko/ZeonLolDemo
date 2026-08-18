Shader "Battle/Map/Water"
{
    Properties
    {
        _Color ("Shallow", Color) = (0.35, 0.65, 0.95, 0.7)
        _DeepColor ("Deep", Color) = (0.08, 0.22, 0.48, 0.88)
        _FoamColor ("Foam", Color) = (0.85, 0.95, 1, 0.65)
        _MainTex ("Wave", 2D) = "white" {}
        _Speed ("Scroll", Vector) = (0.04, 0.025, -0.03, 0.045)
        _Spec ("Specular", Range(0, 2)) = 0.85
        _Foam ("Foam Strength", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _DeepColor;
            fixed4 _FoamColor;
            float4 _Speed;
            half _Spec;
            half _Foam;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                // 轻微顶点起伏
                float bob = sin(wp.x * 0.35 + _Time.y * 1.4) * 0.04
                          + cos(wp.z * 0.28 + _Time.y * 1.1) * 0.03;
                v.vertex.y += bob;
                o.pos = UnityObjectToClipPos(v.vertex);
                float2 uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv = uv + _Time.y * _Speed.xy;
                o.uv2 = uv * 2.3 + _Time.y * _Speed.zw;
                o.worldPos = wp;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed w1 = tex2D(_MainTex, i.uv).r;
                fixed w2 = tex2D(_MainTex, i.uv2).g;
                float wave = saturate(w1 * 0.55 + w2 * 0.45);
                fixed4 col = lerp(_DeepColor, _Color, wave);

                // 伪高光
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float spec = pow(saturate(dot(reflect(-l, n), v)), 48.0) * _Spec;
                col.rgb += spec * float3(0.85, 0.95, 1.0);

                // 岸边泡沫（UV 边缘）
                float2 e = abs(i.uv * 2.0 - 1.0);
                float foamMask = saturate(max(e.x, e.y) * 1.15 - 0.72);
                foamMask *= (0.6 + 0.4 * wave);
                col = lerp(col, _FoamColor, foamMask * _Foam);
                col.a = lerp(_Color.a, _DeepColor.a, 1.0 - wave * 0.35);
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
