// Rounded border and fill from a signed distance field on one quad, so the corners stay exact at any scale
// and antialias from the screen-space derivative.
//
// The stereo instancing macros are load-bearing. The project renders single-pass instanced, and without
// them the quad only reaches one eye.
Shader "Navian/PanelFrame"
{
    Properties
    {
        _Color ("Border Colour", Color) = (1, 1, 1, 1)
        _Fill ("Fill Colour", Color) = (0, 0, 0, 1)
        _Aspect ("Aspect (width / height)", Float) = 1
        _Radius ("Corner Radius", Range(0, 1)) = 0.18
        _Thickness ("Border Thickness", Range(0, 0.5)) = 0.04

        [Toggle(_ACCENT)] _AccentOn ("Accent Edge", Float) = 0
        _AccentColor ("Accent Colour", Color) = (0.4, 0.78, 1, 1)
        // Measured in half-height units like the radius, so a wide plate needs a proportionally larger value.
        _AccentWidth ("Accent Width", Range(0, 2)) = 0.18
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ACCENT
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _Fill;
            float _Aspect;
            float _Radius;
            float _Thickness;
            fixed4 _AccentColor;
            float _AccentWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Negative inside the shape, positive outside, and the magnitude is the distance to the edge.
            float sdRoundBox (float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Work in half-height units so radius and thickness read the same on any panel shape.
                float2 p = (i.uv - 0.5) * 2.0 * float2(_Aspect, 1.0);
                float2 half_size = float2(_Aspect, 1.0);

                float d = sdRoundBox(p, half_size, _Radius);
                float aa = fwidth(d) + 1e-5;

                float inside = 1.0 - smoothstep(-aa, aa, d);
                float innerInside = 1.0 - smoothstep(-aa, aa, d + _Thickness);
                float border = saturate(inside - innerInside);

                fixed4 col;
                col.rgb = lerp(_Fill.rgb, _Color.rgb, border);
                col.a = max(_Fill.a * innerInside, _Color.a * border);

            #if defined(_ACCENT)
                // Masked by the same field as the plate, so the band picks up the corner radius instead of
                // overhanging the edge.
                float edge = -_Aspect + _AccentWidth;
                float accent = (1.0 - smoothstep(edge - aa, edge + aa, p.x)) * inside;
                col.rgb = lerp(col.rgb, _AccentColor.rgb, accent * _AccentColor.a);
                col.a = max(col.a, _AccentColor.a * accent);
            #endif

                return col;
            }
            ENDCG
        }
    }
}
