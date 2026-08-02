// Rewrites a robot's albedo into a team's colours. Blit-only: TeamPaint runs
// one pass of this over the imported baseColorTexture and hands the result back
// to the untouched glTF material, so the robots keep their normal lit shading
// and only their paint changes.
//
// HUE IS ROTATED, VALUE IS KEPT. Every coloured pixel keeps its own brightness,
// so panel lines, shading and wear survive intact — a multiply tint (what this
// replaces) can only darken, and turns an orange robot muddy instead of turning
// it cyan.
//
// FOLDED, NOT REPLACED. Driving every coloured pixel to the SAME team hue
// collapsed a two-tone robot into one colour: the bolt is blue and yellow, and
// both came out the same magenta, so the away-team bolt read as a solid purple
// toy with no markings while the home-team bolt still had two.
//
// Instead each pixel keeps its DISTANCE from _AnchorHue — the robot's own
// dominant hue, measured off its albedo at build time. That distance is taken as
// an absolute value and added to the team hue, so:
//   * the dominant colour lands exactly ON the team hue, and
//   * every other colour fans out to one side of it, as far as it was different.
// The bolt's blue body goes purple and its yellow trim goes orange; a robot that
// only ever had one colour still comes out as one colour.
//
// ABSOLUTE, and per robot. Both matter. A signed offset sends some robots'
// accents cool instead of warm — the bolt's yellow trim came out blue-violet,
// competing with the cyan team it exists to contrast against. And a single
// global pivot cannot work at all: five of the nine robots are cool-dominant and
// four are warm, so whichever hue is chosen, one group sits at the fold's fixed
// point and is barely repainted. The racer, which is almost entirely orange,
// came out identical to its home-team twin that way.
//
// GREYS ARE LEFT ALONE. The blend is weighted by the source pixel's own
// saturation, so white and grey armour stays neutral metal and only the painted
// panels change team. That is what keeps two robots on opposite teams
// recognisable as the same robot rather than as two coloured blobs.
Shader "PhotonArena/TeamRecolor"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _TeamColor ("Team Color", Color) = (1, 1, 1, 1)
        _Strength ("Strength", Range(0, 1)) = 1
        // Saturation below the cutoff is left untouched; the softness is the
        // band over which the repaint fades in, so there is no hard edge
        // between a robot's grey plating and its painted panels.
        _GreyCutoff ("Grey Cutoff", Range(0, 1)) = 0.15
        _GreySoftness ("Grey Softness", Range(0.001, 1)) = 0.25
        // Floor under the repainted saturation, so a washed-out panel still
        // comes out unmistakably team-coloured rather than faintly tinted.
        _SaturationFloor ("Saturation Floor", Range(0, 1)) = 0.5
        // How much team colour the greys the rule above skipped pick up. Zero
        // leaves white and grey armour untouched; a light wash is needed because
        // hue replacement cannot repaint white at all, and the ranger is nearly
        // all white plating. TeamPaint.NeutralWash is the one that ships — this
        // default only matters if the material is used by hand.
        _NeutralWash ("Neutral Wash", Range(0, 1)) = 0.22
        // The robot's own dominant hue — the colour that becomes the team colour
        // exactly. TeamPaint supplies it per robot; this default is only used if
        // the material is driven by hand.
        _AnchorHue ("Anchor Hue", Range(0, 1)) = 0.519
        // How much of a pixel's distance from the anchor survives. 0 collapses
        // every colour onto the team hue — the old behaviour, and the bug. High
        // values push the accent so far round the wheel that it arrives back at
        // the home team's own trim colour: at 0.7 the bolt's yellow came out
        // yellow again, making both teams' markings match.
        _HueSpread ("Hue Spread", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _TeamColor;
            half _Strength;
            half _GreyCutoff;
            half _GreySoftness;
            half _SaturationFloor;
            half _NeutralWash;
            half _AnchorHue;
            half _HueSpread;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float3 RgbToHsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 HsvToRgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 src = tex2D(_MainTex, i.uv);
                float3 rgb = src.rgb;

                // Hue and saturation are perceptual quantities, so the swap is
                // done in gamma space whatever the project renders in.
                #ifndef UNITY_COLORSPACE_GAMMA
                    rgb = LinearToGammaSpace(rgb);
                    float3 team = LinearToGammaSpace(_TeamColor.rgb);
                #else
                    float3 team = _TeamColor.rgb;
                #endif

                float3 hsv = RgbToHsv(rgb);
                float teamHue = RgbToHsv(team).x;

                float weight = _Strength * smoothstep(_GreyCutoff, _GreyCutoff + _GreySoftness, hsv.y);

                // Distance from the anchor, the short way round and unsigned, so
                // the fan is always to the same side of the team hue. Short-path
                // matters: measured in one fixed direction, the wrap sits right
                // where a dominant colour usually is, and near-neighbour hues
                // would land at opposite ends of the palette.
                float delta = abs(frac(hsv.x - _AnchorHue + 0.5) - 0.5);
                float hue = frac(teamHue + _HueSpread * delta);

                float3 painted = HsvToRgb(float3(hue, max(hsv.y, _SaturationFloor), hsv.z));
                rgb = lerp(rgb, painted, weight);

                // Whatever the hue rule left alone — white plating, black
                // panels — takes the wash instead, at its own brightness so
                // shading and panel lines still read through it.
                float3 wash = HsvToRgb(float3(teamHue, _SaturationFloor, hsv.z));
                rgb = lerp(rgb, wash, _NeutralWash * (1.0 - weight));

                #ifndef UNITY_COLORSPACE_GAMMA
                    rgb = GammaToLinearSpace(rgb);
                #endif

                return half4(rgb, src.a);
            }
            ENDCG
        }
    }
}
