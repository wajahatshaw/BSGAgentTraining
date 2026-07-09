// Depth-tested 3D text: identical to Unity's built-in "GUI/Text Shader" EXCEPT ZTest LEqual (instead of
// Always), so floating TextMesh labels are hidden behind solid geometry and only show when unobstructed.
Shader "BSG/OccludedText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Lighting Off
        Cull Off
        ZWrite Off
        ZTest LEqual
        Fog { Mode Off }
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Color [_Color]
            SetTexture [_MainTex]
            {
                combine primary, texture * primary
            }
        }
    }
}
