Shader "Battle/VFX/AdditiveSoft"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        _TintColor ("Tint", Color) = (1, 0.85, 0.4, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.35
        _SoftFactor ("Edge Softness", Range(0.1, 4)) = 1.6
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }
        Blend SrcAlpha One
        ColorMask RGB
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_particles
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TintColor;
            half _Intensity;
            half _SoftFactor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _TintColor;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                // Soft radial falloff when texture is flat white (procedural quads / default particle)
                float2 centered = i.uv * 2.0 - 1.0;
                float soft = saturate(1.0 - pow(saturate(dot(centered, centered)), _SoftFactor));
                fixed a = max(tex.a, soft) * i.color.a;
                fixed3 rgb = tex.rgb * i.color.rgb * _Intensity;
                return fixed4(rgb * a, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
