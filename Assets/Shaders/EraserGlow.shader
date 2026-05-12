Shader "Custom/EraserGlow"
{
    // Additive glow used for the brush cursor in eraser mode.
    // Set _PulseMin = 1 for a steady glow; lower values pulse between _PulseMin and 1.
    Properties
    {
        _GlowColor ("Glow Color", Color) = (0.2, 0.8, 1.0, 0.8)
        _PulseSpeed ("Pulse Speed", Float) = 2.0
        _PulseMin ("Pulse Min", Range(0, 1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" }

        Blend SrcAlpha One  // Additive: adds glow light on top of existing color.
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _GlowColor;
            float _PulseSpeed;
            float _PulseMin;

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
                float pulse = lerp(_PulseMin, 1.0, 0.5 + 0.5 * sin(_Time.y * _PulseSpeed));
                return fixed4(_GlowColor.rgb, _GlowColor.a * pulse);
            }
            ENDCG
        }
    }

    FallBack Off
}
