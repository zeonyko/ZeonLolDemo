Shader "Battle/CombatText/Outline"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1, 0.82, 0.25, 1)
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 0.92)
        _OutlineWidth ("Outline Width", Range(0, 0.12)) = 0.045
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        // Outline
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _OutlineColor;
            half _OutlineWidth;

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
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy * (_OutlineWidth * 48.0);
                float a = 0;
                a = max(a, tex2D(_MainTex, i.uv + float2(texel.x, 0)).a);
                a = max(a, tex2D(_MainTex, i.uv - float2(texel.x, 0)).a);
                a = max(a, tex2D(_MainTex, i.uv + float2(0, texel.y)).a);
                a = max(a, tex2D(_MainTex, i.uv - float2(0, texel.y)).a);
                a = max(a, tex2D(_MainTex, i.uv + float2(texel.x, texel.y)).a);
                a = max(a, tex2D(_MainTex, i.uv + float2(-texel.x, texel.y)).a);
                a = max(a, tex2D(_MainTex, i.uv + float2(texel.x, -texel.y)).a);
                a = max(a, tex2D(_MainTex, i.uv + float2(-texel.x, -texel.y)).a);
                a *= i.color.a * _OutlineColor.a;
                clip(a - 0.01);
                return fixed4(_OutlineColor.rgb, a);
            }
            ENDCG
        }

        // Fill
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

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
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a * i.color.a;
                clip(a - 0.01);
                return fixed4(i.color.rgb, a);
            }
            ENDCG
        }
    }
}
