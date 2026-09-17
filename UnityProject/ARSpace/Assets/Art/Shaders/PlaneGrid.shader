Shader "ARSpace/PlaneGrid"
{
    Properties
    {
        [Header(Grid Settings)]
        _GridColor ("Primary Grid Color (0.5m)", Color) = (0.85, 0.88, 0.92, 0.18)
        _AccentColor ("Accent Color (1m & Sweep)", Color) = (1.0, 0.42, 0.0, 0.45)
        _PrimarySpacing ("Primary Spacing (m)", Float) = 0.5
        _SecondarySpacing ("Secondary Spacing (m)", Float) = 1.0
        _PrimaryLineWidth ("Primary Line Width (px)", Float) = 1.2
        _SecondaryLineWidth ("Secondary Line Width (px)", Float) = 1.8

        [Header(Distance Fading)]
        _FadeStartDistance ("Distance Fade Start (m)", Float) = 7.0
        _FadeEndDistance ("Distance Fade End (m)", Float) = 12.0

        [Header(Per Plane Alpha)]
        _PlaneAlpha ("Plane Alpha", Range(0.0, 1.0)) = 1.0

        [Header(Scan Sweep)]
        _ScanSweepCenter ("Sweep Center (WS)", Vector) = (0, 0, 0, 0)
        _ScanSweepRadius ("Sweep Radius (m)", Float) = 0.0
        _ScanSweepWidth ("Sweep Width (m)", Float) = 0.35
        _ScanSweepActive ("Sweep Active Alpha", Range(0.0, 1.0)) = 0.0
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
            "Queue" = "Transparent-50"
            "IgnoreProjector" = "True"
            "DisableBatching" = "False"
        }

        Pass
        {
            Name "PlaneGridPass"
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
                float4 _GridColor;
                float4 _AccentColor;
                float _PrimarySpacing;
                float _SecondarySpacing;
                float _PrimaryLineWidth;
                float _SecondaryLineWidth;
                float _FadeStartDistance;
                float _FadeEndDistance;
                float _PlaneAlpha;
                float4 _ScanSweepCenter;
                float _ScanSweepRadius;
                float _ScanSweepWidth;
                float _ScanSweepActive;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.color = input.color;
                return output;
            }

            // Anti-aliased grid line using screen-space derivatives (fwidth)
            float ComputeGridLine(float2 worldXZ, float spacing, float targetPixelWidth)
            {
                float2 coord = worldXZ / max(spacing, 0.001);
                float2 dCoord = fwidth(coord);
                dCoord = max(dCoord, 1e-4);

                // Distance to nearest grid line in [0, 0.5] range
                float2 dist = abs(frac(coord - 0.5) - 0.5);

                // Half-width in coordinate space corresponding to targetPixelWidth pixels
                float2 halfWidth = dCoord * (targetPixelWidth * 0.5);

                // Coverage: 1 on line, 0 outside halfWidth
                float2 intensity = saturate(1.0 - (dist / max(halfWidth, 1e-4)));

                // Anti-moiré fade when lines become too dense on screen (subpixel crowding)
                float moireFade = saturate(1.0 - max(dCoord.x, dCoord.y) * 1.5);

                return max(intensity.x, intensity.y) * moireFade;
            }

            // Expanding scan sweep ring from user camera position
            float ComputeScanSweep(float2 worldXZ, float2 centerXZ, float radius, float width, float activeAlpha)
            {
                if (activeAlpha <= 0.001 || radius <= 0.001)
                    return 0.0;

                float distToCenter = length(worldXZ - centerXZ);
                float ringDist = abs(distToCenter - radius);
                float ring = saturate(1.0 - ringDist / max(width, 0.01));

                // Subtle inner trail behind the expanding ring
                float trail = 0.0;
                if (distToCenter < radius)
                {
                    float trailDist = radius - distToCenter;
                    trail = saturate(1.0 - trailDist / (width * 3.5)) * 0.3;
                }

                float pulse = max(ring, trail);
                return pulse * activeAlpha;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 worldXZ = input.positionWS.xz;

                // 1. Grid lines (0.5m primary and 1.0m secondary)
                float line05 = ComputeGridLine(worldXZ, _PrimarySpacing, _PrimaryLineWidth);
                float line10 = ComputeGridLine(worldXZ, _SecondarySpacing, _SecondaryLineWidth);

                // Secondary 1.0m line takes priority over 0.5m primary
                float purePrimary = saturate(line05 - line10);

                half3 colRGB = _GridColor.rgb * purePrimary * _GridColor.a +
                               _AccentColor.rgb * line10 * _AccentColor.a;
                half colA = purePrimary * _GridColor.a + line10 * _AccentColor.a;

                // 2. Scan sweep pulse (active during Scanning state)
                float sweep = ComputeScanSweep(worldXZ, _ScanSweepCenter.xz, _ScanSweepRadius, _ScanSweepWidth, _ScanSweepActive);
                if (sweep > 0.001)
                {
                    colRGB += _AccentColor.rgb * (sweep * 0.6);
                    colA = max(colA, (half)(sweep * 0.6));
                }

                // Discard early if completely transparent to save pixel bandwidth
                if (colA <= 0.002)
                    discard;

                // 3. Distance fading from camera
                float camDist = length(_WorldSpaceCameraPos.xyz - input.positionWS);
                float distFade = 1.0 - saturate((camDist - _FadeStartDistance) / max(_FadeEndDistance - _FadeStartDistance, 0.001));

                // 4. Boundary feathering from vertex color alpha (interpolated from polygon boundary)
                float boundaryFade = input.color.a;

                // 5. Overall plane alpha (drives appearance fade-in / fade-out)
                half finalAlpha = saturate(colA * distFade * boundaryFade * _PlaneAlpha);

                return half4(colRGB, finalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
