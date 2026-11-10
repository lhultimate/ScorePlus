// WPF ShaderEffect input: the video or image being keyed.
sampler2D Input : register(s0);

// WPF registers the effect's KeyColor (c0) and Tolerance (c1.x).
float4 KeyColor : register(c0);
float Tolerance : register(c1);

// WPF expects shader output to be premultiplied. Clearing alpha alone leaves the original
// green in the RGB channels, and the compositor adds it back over the scorebug, so keyed
// pixels must have their colour zeroed as well.
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(Input, uv);
    float3 delta = color.rgb - KeyColor.rgb;
    float dist = sqrt(dot(delta, delta));
    // A narrow soft edge hides the jaggies a hard step leaves on compressed footage.
    color.a *= smoothstep(Tolerance, Tolerance + 0.02, dist);
    color.rgb *= color.a;
    return color;
}
