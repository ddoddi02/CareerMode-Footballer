Shader "Prototype/VisionOverlay"
{
    Properties
    {
        _DarkColor ("Dark Color", Color) = (0.004, 0.008, 0.013, 1)
        _PeripheralDim ("Peripheral Dim", Range(0,1)) = 0.55
        _EdgeDim ("Edge Dim", Range(0,1)) = 0.86
        _BlindDim ("Blind Dim", Range(0,1)) = 0.985
        _FocusSoft ("Focus Soft (deg)", Range(1,40)) = 9
        _PeriphSoft ("Peripheral Soft (deg)", Range(1,40)) = 12
        _EdgeSoft ("Edge Soft (deg)", Range(1,40)) = 12
        _ViewSoft ("View Edge Soft (deg)", Range(0.5,20)) = 3
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Overlay" "Queue" = "Overlay" }

        Pass
        {
            Name "VisionOverlay"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DarkColor;
                float _PeripheralDim;
                float _EdgeDim;
                float _BlindDim;
                float _FocusSoft;
                float _PeriphSoft;
                float _EdgeSoft;
                float _ViewSoft;
            CBUFFER_END

            float4 _ViewerPos;
            float4 _ViewerFwd;
            float4 _VisionCamFwd;
            float4 _ConeParams;
            float4 _ConeParams2;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                if (_ConeParams2.z > 0.5) return half4(0.0, 0.0, 0.0, 0.0);

                // Orthographic camera: every view ray is parallel to the camera
                // forward vector, so one ray/plane intersection recovers the pitch
                // point under this pixel. No depth buffer needed.
                float3 cf = _VisionCamFwd.xyz;
                float denom = cf.y;
                if (abs(denom) < 1e-4) denom = -1e-4;
                float t = (0.0 - IN.positionWS.y) / denom;
                float3 g = IN.positionWS + cf * t;

                float2 d = g.xz - _ViewerPos.xz;
                float dist = length(d);
                float2 dn = d / max(dist, 1e-4);
                float c = clamp(dot(dn, _ViewerFwd.xy), -1.0, 1.0);
                float ang = degrees(acos(c));

                float periph = _ConeParams.y;
                float freeR  = _ConeParams2.x;

                // One zone, one brightness (user, 2026-10-18): everything inside the
                // peripheral angle is shown the same, everything outside is dark - and the
                // angle is the only limit, however far it reaches (user, 2026-10-22: the
                // cone used to stop at 30 m). The focus band is the judgement's alone
                // (PerceptionSystem.Evaluate); this only draws.
                float dim = smoothstep(periph, periph + _ViewSoft, ang) * _BlindDim;

                // You always know what is at your own feet - out to freeRadius, the same
                // radius the judgement uses, so nothing lit here is a body left undrawn.
                float nearBubble = 1.0 - smoothstep(freeR - 0.5, freeR, dist);
                dim *= (1.0 - nearBubble);

                return half4(_DarkColor.rgb, saturate(dim));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
