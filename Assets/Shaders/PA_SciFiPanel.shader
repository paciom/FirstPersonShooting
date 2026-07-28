// World-space triplanar sci-fi panel shader for arena geometry. Draws recessed
// panel-line grooves and thin glowing seams onto any primitive (walls, floor,
// cover blocks) with consistent scale regardless of the object's dimensions —
// no UVs, no stretching. Lit by the URP main light + neon accent point lights.
Shader "PhotonArena/SciFiPanel"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.14, 0.16, 0.24, 1)
        _PanelColor ("Panel Groove Color", Color) = (0.05, 0.06, 0.10, 1)
        [HDR] _SeamColor ("Seam Glow Color", Color) = (0.2, 0.9, 1, 1)
        _Tiling ("Panel Size (world units)", Float) = 3
        _SeamGlow ("Seam Glow Strength", Float) = 1.5
        _PatternMode ("Pattern Mode (0 plates,1 hazard,2 fine,3 bolts)", Float) = 0
        [HDR] _AccentColor ("Accent Color", Color) = (1, 0.6, 0.1, 1)
        [Toggle] _ObjectSpace ("Lock Pattern To Object (moving parts)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PanelColor;
                float4 _SeamColor;
                float _Tiling;
                float _SeamGlow;
                float _PatternMode;
                float4 _AccentColor;
                float _ObjectSpace;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 texPos : TEXCOORD2;
                float3 patternNormal : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                float3 normalWS = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.normalWS = normalWS;

                // World space (default) tiles the panel grid continuously across
                // separate objects — right for walls and floors. Object space
                // locks it to the mesh, which is required for anything that
                // MOVES: a cover block sliding in world space swims through its
                // own plating. The scale multiply keeps a panel the same size in
                // metres whatever the block's dimensions.
                float3 scale = float3(
                    length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20)),
                    length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21)),
                    length(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22)));

                float objectSpace = saturate(_ObjectSpace);
                OUT.texPos = lerp(p.positionWS, IN.positionOS.xyz * scale, objectSpace);
                OUT.patternNormal = lerp(normalWS, IN.normalOS, objectSpace);
                return OUT;
            }

            // Returns (groove, seam) masks for one planar projection.
            float2 PanelPattern(float2 uv)
            {
                float2 g = frac(uv);
                float2 d = min(g, 1.0 - g);
                float edge = min(d.x, d.y);
                float groove = 1.0 - smoothstep(0.0, 0.025, edge);
                float seam = smoothstep(0.025, 0.04, edge) - smoothstep(0.05, 0.07, edge);
                // A few brighter "rivet" nodes at panel corners.
                float corner = (1.0 - smoothstep(0.0, 0.06, length(d))) * 0.6;
                return float2(groove, saturate(seam + corner));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wpos = IN.texPos / max(_Tiling, 0.01);
                // Blend weights follow whichever space the pattern is in.
                float3 n = abs(normalize(IN.patternNormal));
                n /= (n.x + n.y + n.z + 1e-4);

                float2 pX = PanelPattern(wpos.zy);
                float2 pY = PanelPattern(wpos.xz);
                float2 pZ = PanelPattern(wpos.xy);
                float groove = pX.x * n.x + pY.x * n.y + pZ.x * n.z;
                float seam   = pX.y * n.x + pY.y * n.y + pZ.y * n.z;

                float3 albedo = lerp(_BaseColor.rgb, _PanelColor.rgb, groove);

                // Pattern variety for a spaceship-interior feel (not just neon).
                float3 accentEmit = float3(0, 0, 0);
                if (_PatternMode > 0.5 && _PatternMode < 1.5)
                {
                    // Hazard stripes across the plates. Object space, like the
                    // panels themselves, or the stripes crawl as the block moves.
                    float s = frac((IN.texPos.x + IN.texPos.z) * 0.5);
                    float stripe = step(0.6, s) * (1.0 - groove);
                    albedo = lerp(albedo, _AccentColor.rgb * 0.5, stripe * 0.5);
                    accentEmit += _AccentColor.rgb * stripe * 0.12;
                }
                else if (_PatternMode >= 1.5 && _PatternMode < 2.5)
                {
                    // Fine secondary grid — denser tech plating.
                    float2 f = frac(wpos.zx * 3.0);
                    float2 fd = min(f, 1.0 - f);
                    float fine = 1.0 - smoothstep(0.0, 0.04, min(fd.x, fd.y));
                    albedo *= 1.0 - fine * 0.45;
                }
                else if (_PatternMode >= 2.5)
                {
                    // Glowing bolt/rivet nodes on hull plates.
                    accentEmit += _AccentColor.rgb * seam * 0.6;
                }

                float3 nWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float3 lighting = albedo * (mainLight.color * saturate(dot(nWS, mainLight.direction)) + 0.28);

            #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, IN.positionWS);
                    lighting += albedo * l.color * saturate(dot(nWS, l.direction)) * l.distanceAttenuation;
                }
            #endif

                // Phase from object space too, so the seam pulse does not change
                // rhythm as the block slides.
                float pulse = 0.85 + 0.15 * sin(_Time.y * 2.0 + IN.texPos.x * 0.3);
                float3 emission = _SeamColor.rgb * seam * _SeamGlow * pulse + accentEmit;

                return half4(lighting + emission, 1.0);
            }
            ENDHLSL
        }
    }
}
