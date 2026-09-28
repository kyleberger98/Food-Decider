// Lit shader that takes its albedo from mesh vertex colours (Built-in Render Pipeline), multiplied
// by _Color (per-renderer tint for selection and damage). Used by the low-poly map, props, units and
// cities. For URP, swap for a Shader Graph with a Vertex Color node.
Shader "Crucible/VertexColorLit"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        half _Glossiness;
        fixed4 _Color;

        struct Input
        {
            float4 vertColor;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertColor = v.color;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = IN.vertColor.rgb * _Color.rgb;
            o.Metallic = 0;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
