Shader "Battle/VFX/GroundDecal"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.7, 1, 0.55)
        _InnerColor ("Inner Color", Color) = (0.7, 0.95, 1, 0.8)
        _RingWidth ("Ring Width", Range(0.02, 0.45)) = 0.12
        _Pulse ("Pulse", Range(0, 2)) = 0.35
        _Softness ("Softness", Range(0.01, 0.3)) = 0.06
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Lighting Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _InnerColor;
            half _RingWidth;
            half _Pulse;
            half _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(p);
                float pulse = 1.0 + sin(_Time.y * 3.5) * _Pulse * 0.08;
                float outer = 0.98 * pulse;
                float inner = outer - _RingWidth;
                float ring = smoothstep(inner - _Softness, inner, d) * (1.0 - smoothstep(outer - _Softness, outer, d));
                float fill = (1.0 - smoothstep(0.0, inner, d)) * 0.25;
                fixed4 col = lerp(_Color, _InnerColor, saturate(1.0 - d));
                col.a *= saturate(ring + fill);
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
