Shader "Sound System Samples/Flat Color"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Input { float4 vertex : POSITION; };
            struct Output { float4 position : SV_POSITION; };
            Output vert(Input input) { Output output; output.position = UnityObjectToClipPos(input.vertex); return output; }
            fixed4 frag(Output input) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
