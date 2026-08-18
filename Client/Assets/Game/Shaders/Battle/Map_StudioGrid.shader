Shader "Battle/Map/StudioGrid"
{
    Properties
    {
        _Color ("Floor", Color) = (0.86, 0.86, 0.88, 1)
        _LineColor ("Grid Line", Color) = (0.62, 0.62, 0.66, 1)
        _MajorLineColor ("Major Line", Color) = (0.48, 0.48, 0.52, 1)
        _MainTex ("Grid Mask (R=minor, G=major)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.35
        _Metallic ("Metallic", Range(0, 1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _LineColor;
        fixed4 _MajorLineColor;
        half _Glossiness;
        half _Metallic;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 mask = tex2D(_MainTex, IN.uv_MainTex);
            // R = minor，G = major；major 叠在细线之上
            fixed3 col = _Color.rgb;
            col = lerp(col, _LineColor.rgb, saturate(mask.r));
            col = lerp(col, _MajorLineColor.rgb, saturate(mask.g));
            o.Albedo = col;
            o.Metallic = _Metallic;
            // 线略哑光，更像预览台地胶
            o.Smoothness = _Glossiness * (1.0 - saturate(mask.r) * 0.4 - saturate(mask.g) * 0.2);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
