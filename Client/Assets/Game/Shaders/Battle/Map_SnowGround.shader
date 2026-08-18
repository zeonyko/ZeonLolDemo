Shader "Battle/Map/SnowGround"
{
    Properties
    {
        _Color ("Tint", Color) = (0.78, 0.84, 0.92, 1)
        _LineColor ("Line Color", Color) = (0.45, 0.55, 0.68, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.35
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
        half _Glossiness;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            // 贴图 R=雪底，G=刻线
            fixed3 albedo = lerp(_Color.rgb, _LineColor.rgb, c.g);
            albedo *= lerp(0.92, 1.08, c.r);
            o.Albedo = albedo;
            o.Metallic = 0.05;
            o.Smoothness = _Glossiness * (0.7 + c.r * 0.3);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
