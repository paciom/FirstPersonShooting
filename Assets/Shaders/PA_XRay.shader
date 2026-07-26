// See-through-wall silhouette shader for the X-Ray Scope. Renders a flat
// glowing colour ONLY where the surface is occluded by nearer geometry
// (ZTest Greater), so enemies show up as coloured shapes through walls.
Shader "PhotonArena/XRay"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.3, 0.9, 0.6)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZTest Greater   // draw only where something nearer already covers it
            ZWrite Off
            Blend SrcAlpha One
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _Color;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
