Shader "Battle/Map/Terrain"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.72, 0.80, 0.90, 1)
        _ColorB ("Secondary Color", Color) = (0.48, 0.58, 0.70, 1)
        _MainTex ("Detail (R height, G macro, B micro)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.42
        _Metallic ("Metallic", Range(0, 1)) = 0.08
        _RimColor ("Rim", Color) = (0.75, 0.88, 1, 0.35)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.5
        _DetailTiling ("Detail Tiling", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        // surface 的 uv_MainTex 会自动生成 _MainTex_ST，勿重复声明
        fixed4 _Color;
        fixed4 _ColorB;
        half _Glossiness;
        half _Metallic;
        fixed4 _RimColor;
        half _RimPower;
        half _DetailTiling;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
            float3 worldNormal;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.uv_MainTex;
            fixed4 a = tex2D(_MainTex, uv);
            fixed4 b = tex2D(_MainTex, uv * _DetailTiling);

            float h = saturate(a.r * 0.65 + b.b * 0.35);
            float macro = a.g;
            fixed3 albedo = lerp(_ColorB.rgb, _Color.rgb, h);
            albedo = lerp(albedo, _ColorB.rgb * 0.85, macro * 0.45);
            // 微观高光起伏
            albedo *= lerp(0.9, 1.12, b.r);

            o.Albedo = albedo;
            o.Metallic = _Metallic;
            o.Smoothness = saturate(_Glossiness * (0.55 + h * 0.55));

            half rim = 1.0 - saturate(dot(normalize(IN.viewDir), normalize(IN.worldNormal)));
            o.Emission = _RimColor.rgb * pow(rim, _RimPower) * _RimColor.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
