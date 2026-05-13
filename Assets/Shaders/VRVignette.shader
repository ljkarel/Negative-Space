Shader "Custom/VRVignette"
{
    // Place on a quad/plane parented to the camera, just in front of it.
    // Radial gradient: transparent at centre, red at edges.
    // _Intensity 0 = invisible, 1 = full vignette.
    Properties
    {
        _Color     ("Vignette Color", Color)       = (1, 0, 0, 1)
        _Intensity ("Intensity",      Range(0, 1)) = 0
        _Sharpness ("Edge Sharpness", Range(1, 8)) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay+100" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Color;
            float  _Intensity;
            float  _Sharpness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Distance from UV centre (0.5, 0.5), remapped so corners = 1.
                float2 d    = (i.uv - 0.5) * 2.0;
                float  dist = saturate(length(d));
                float  edge = pow(dist, _Sharpness);
                return fixed4(_Color.rgb, edge * _Intensity * _Color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
