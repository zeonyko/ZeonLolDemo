Shader "Battle/VFX/SoftGlow"
{
    Properties
    {
        _Color ("Core Color", Color) = (0.55, 0.85, 1, 1)
        _RimColor ("Rim Color", Color) = (1, 1, 1, 0.85)
        _Intensity ("Intensity", Range(0, 5)) = 1.8
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.4
        _Alpha ("Alpha", Range(0, 1)) = 0.85
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
        Cull Back
        ZWrite Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _RimColor;
            half _Intensity;
            half _RimPower;
            half _Alpha;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);
                float ndotv = saturate(dot(n, v));
                float rim = pow(1.0 - ndotv, _RimPower);
                fixed3 rgb = _Color.rgb * _Intensity + _RimColor.rgb * rim * 1.6;
                float a = saturate(_Alpha * (0.35 + rim * 0.9));
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
