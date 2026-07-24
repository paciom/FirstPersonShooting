// Sci-fi force-field bubble: fresnel rim + scrolling hex energy lattice.
// _Intensity is driven by ShieldBubble — 0 when idle, flashes on hits.
Shader "PhotonArena/ForceField"
{
    Properties
    {
        _MainTex ("Hex Pattern", 2D) = "black" {}
        [HDR] _Color ("Color", Color) = (0.2, 0.9, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 0
        _FresnelPower ("Fresnel Power", Float) = 2.5
        _ScrollSpeed ("Scroll Speed", Float) = 0.12
        _Tiling ("Hex Tiling", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _Color;
            float _Intensity;
            float _FresnelPower;
            float _ScrollSpeed;
            float _Tiling;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = _WorldSpaceCameraPos - worldPos;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float fresnel = pow(1.0 - saturate(dot(normalize(i.worldNormal), normalize(i.viewDir))), _FresnelPower);
                float2 uv = i.uv * _Tiling + _Time.y * _ScrollSpeed;
                float hex = tex2D(_MainTex, uv).a;
                float energy = fresnel * 0.7 + hex * fresnel * 1.6 + hex * 0.08;
                half4 col = _Color * _Intensity * energy;
                col.a = saturate(col.a);
                return col;
            }
            ENDCG
        }
    }
}
