using Avalonia;

namespace OpenCvSharp
{
    public readonly struct Size
    {
        public readonly int Width;
        public readonly int Height;
        public Size(int width, int height) { Width = width; Height = height; }
        public static implicit operator Avalonia.Size(Size s) => new Avalonia.Size(s.Width, s.Height);
    }
}
