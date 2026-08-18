Shader "Battle/Map/SkyDome"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.35, 0.55, 0.85, 1)
        _Horizon ("Horizon", Color) = (0.75, 0.82, 0.92, 1)
        _Ground ("Ground Glow", Color) = (0.55, 0.65, 0.75, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" }
        Cull Front
        ZWrite Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Zenith;
            fixed4 _Horizon;
            fixed4 _Ground;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 local : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.local);
                float h = saturate(d.y * 0.5 + 0.5);
                fixed3 col = lerp(_Ground.rgb, _Horizon.rgb, saturate(h * 1.6));
                col = lerp(col, _Zenith.rgb, saturate((h - 0.45) * 2.2));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
