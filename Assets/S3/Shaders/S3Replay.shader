// The instant replay's view of S^3 (after the replay shaders on main): an ordinary 3D camera outside the world.
//
// The ground is the great 2-sphere y = 0, so dropping y leaves a plain unit 2-sphere, and a point h radians up
// sits over the same ground point, pulled in towards the centre: the playable hemisphere becomes a solid unit
// ball, ground on the outside and the pole at the very centre, so a whole flight shows at once. How far in is
// 1 - h + Bend h^2, which falls one for one with height at the ground (props keep their proportions there) and
// still reaches the centre at the pole. S3Replay.ToBall must match it.
//
// Geometry is either local metres placed by _S3Model (the scenery, ground, flag, like S3/Object), or already in
// ball coordinates (_FromS3 = 0: the trail, and the ball and cup markers, offset and scaled by _Marker).
// Facets are flat-shaded from screen-space derivatives; the shell is a glass sphere with a bright rim.
Shader "S3/Replay"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient", Float) = 0.6
        _Alpha ("Alpha", Float) = 1
        _Shell ("Glass shell", Float) = 0
        _FromS3 ("Geometry on S^3 (1) or in ball coordinates (0)", Float) = 1
        _Reveal ("Trail: drawn up to this sample", Float) = 1000000
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "S3Replay"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Ambient, _Alpha, _Shell, _FromS3, _Reveal, _Cull, _ZWrite, _SrcBlend, _DstBlend;
            CBUFFER_END

            float4x4 _S3Model;      // per renderer: SO(4) placement of local-metre geometry
            float _S3InvR;
            float4 _Marker;         // per renderer, ball-coordinate geometry: xyz offset, w scale
            float4 _ReplayLight;    // global: light direction in ball coordinates

            static const float Bend = (1.5707963 - 1) / (1.5707963 * 1.5707963);

            struct Attributes
            {
                float3 pos    : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;   // trail: the sample index
            };

            struct Varyings
            {
                float4 clip   : SV_POSITION;
                float4 color  : COLOR;
                float3 world  : TEXCOORD0;
                float sample  : TEXCOORD1;
            };

            float3 ToBall(float4 p)
            {
                float3 g = float3(p.x, p.w, p.z);
                float l = length(g), h = atan2(p.y, l);
                return l > 1e-6 ? g / l * (1 - h + Bend * h * h) : float3(0, 0, 0);
            }

            Varyings Vert(Attributes a)
            {
                float3 q;
                if (_FromS3 > 0.5)
                {
                    // exponential map at e_w (as S3/Object), then placed by the frame
                    float3 u = a.pos * _S3InvR;
                    float r = length(u);
                    float3 d = r > 1e-6 ? u / r : float3(0, 0, 0);
                    q = ToBall(mul(_S3Model, float4(d * sin(r), cos(r))));
                }
                else q = _Marker.xyz + a.pos * _Marker.w;
                Varyings o;
                o.clip = TransformWorldToHClip(q);
                o.world = q;
                o.color = a.color;
                o.sample = a.uv.x;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                if (i.sample > _Reveal) discard;
                float3 toEye = normalize(_WorldSpaceCameraPos - i.world);
                float3 facet = cross(ddx(i.world), ddy(i.world));
                float3 n = _Shell > 0.5 ? normalize(i.world) : dot(facet, facet) > 0 ? normalize(facet) : toEye;
                if (dot(n, toEye) < 0) n = -n;
                float3 col = i.color.rgb * _Tint.rgb * (_Ambient + (1 - _Ambient) * saturate(dot(n, _ReplayLight.xyz)));
                float alpha = _Alpha;
                if (_Shell > 0.5)
                {
                    float rim = pow(1 - abs(dot(n, toEye)), 2.5);   // glass: edge-on is bright and opaque
                    col = lerp(col, float3(0.85, 0.94, 1.0), rim * 0.7);
                    alpha = _Alpha + (1 - _Alpha) * rim * 0.8;
                }
                // colours are authored in sRGB, like S3/Object's
                #ifndef UNITY_COLORSPACE_GAMMA
                    col = max(col, 0);
                    col = col <= 0.04045 ? col / 12.92 : pow((col + 0.055) / 1.055, 2.4);
                #endif
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
}
