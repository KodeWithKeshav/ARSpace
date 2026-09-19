Shader "ARSpace/ContactShadow"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.0, 0.0, 0.0, 1.0)
        _Intensity ("Intensity", Range(0.0, 1.0)) = 0.5
        _Softness ("Edge Softness", Range(0.5, 4.0)) = 1.6
    }

    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal": "12.0"
        }

        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-30"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ContactShadowPass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _Intensity;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Rounded-rectangle falloff (superellipse) so the blob follows a rectangular footprint.
                float2 p = (input.uv - 0.5) * 2.0;
                float d = pow(pow(abs(p.x), 4.0) + pow(abs(p.y), 4.0), 0.25);
                float a = pow(saturate(1.0 - d), _Softness) * _Intensity;
                return half4(_ShadowColor.rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
