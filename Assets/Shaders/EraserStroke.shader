Shader "Custom/EraserStroke"
{
    Properties
    {
        _GlowColor ("Glow Color", Color) = (0.2, 0.8, 1.0, 0.6)
        _PulseSpeed ("Pulse Speed", Float) = 2.0
        _PulseMin ("Pulse Min", Range(0, 1)) = 0.2
        // Set to 1 at runtime via ToggleEraserStrokeVisibility() to reveal all eraser strokes.
        _ShowVisible ("Show Eraser Strokes", Float) = 0
    }
    SubShader
    {
        // Render at queue 1990 (Geometry-10) so depth is written before paint strokes (queue 2000) draw.
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" }

        // Pass 1: Depth mask -- invisible but occludes any paint geometry rendered behind it.
        Pass
        {
            Name "DepthMask"
            ColorMask 0     // Write nothing to the color buffer.
            ZWrite On       // Write to depth buffer so geometry behind this fails the depth test.
            Cull Off        // Double-sided, matching the FrontMesh/BackMesh paint approach.
            Offset -1, -1   // Slight depth bias toward camera so eraser wins ties at the same depth.

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            // ColorMask 0 discards this output, but the fragment function must exist to compile.
            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }

        // Pass 2: Pulsing glow -- additive, x-ray (ZTest Always) so it shows through occluded paint.
        // Alpha is driven to 0 when _ShowVisible = 0, so no color is written when hidden.
        Pass
        {
            Name "EraserGlow"
            Blend SrcAlpha One  // Additive blending: adds glow light on top of existing color.
            ZWrite Off
            ZTest Always        // X-ray: visible even through geometry rendered later.
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _GlowColor;
            float _PulseSpeed;
            float _PulseMin;
            float _ShowVisible;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // When hidden, alpha=0 means the additive blend writes nothing.
                float pulse = (_ShowVisible >= 0.5)
                    ? lerp(_PulseMin, 1.0, 0.5 + 0.5 * sin(_Time.y * _PulseSpeed))
                    : 0.0;
                return fixed4(_GlowColor.rgb, _GlowColor.a * pulse);
            }
            ENDCG
        }
    }

    // No fallback -- a compile failure should be a hard error, not a visible white stroke.
    FallBack Off
}
