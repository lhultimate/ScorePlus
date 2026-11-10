using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Scoreboard.Desktop;

/// <summary>
/// Makes one selected colour transparent in a video picture. The colour and its tolerance are
/// passed to a small pixel shader, so a green screen works on live camera frames, decoded replay
/// frames and file video alike; nothing is left behind as a black or green rectangle.
/// </summary>
public sealed class ChromaKeyEffect : ShaderEffect
{
    static readonly PixelShader Shader = new()
    {
        UriSource = new Uri("pack://application:,,,/ScorePlus;component/Assets/ChromaKey.ps", UriKind.Absolute)
    };

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(
        nameof(Input), typeof(ChromaKeyEffect), 0);

    public static readonly DependencyProperty KeyColorProperty = DependencyProperty.Register(
        nameof(KeyColor), typeof(Color), typeof(ChromaKeyEffect),
        new UIPropertyMetadata(Color.FromRgb(0, 177, 64), PixelShaderConstantCallback(0)));

    /// <summary>RGB distance still treated as the key colour. 0 keys only the exact colour.</summary>
    public static readonly DependencyProperty ToleranceProperty = DependencyProperty.Register(
        nameof(Tolerance), typeof(double), typeof(ChromaKeyEffect),
        new UIPropertyMetadata(0.16, PixelShaderConstantCallback(1)));

    public ChromaKeyEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(KeyColorProperty);
        UpdateShaderValue(ToleranceProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public Color KeyColor
    {
        get => (Color)GetValue(KeyColorProperty);
        set => SetValue(KeyColorProperty, value);
    }

    public double Tolerance
    {
        get => (double)GetValue(ToleranceProperty);
        set => SetValue(ToleranceProperty, Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1));
    }
}