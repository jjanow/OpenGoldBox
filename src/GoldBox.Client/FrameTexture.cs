using GoldBox.Core.Explore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GoldBox.Client;

/// <summary>
/// Shows a 320×200 <see cref="IndexedFrame"/> of EGA indices: maps them through the palette into a texture that the
/// game draws scaled with point sampling (sharp pixels, the original look).
/// </summary>
public sealed class FrameTexture : IDisposable
{
    /// <summary>
    /// The standard EGA hardware palette (the 16 colours of the IBM EGA's default register setting; the DOS game never
    /// reprograms them, por-art-formats.md §3). Not data from the game.
    /// </summary>
    public static readonly Color[] Ega =
    [
        new(0x00, 0x00, 0x00), new(0x00, 0x00, 0xAA), new(0x00, 0xAA, 0x00), new(0x00, 0xAA, 0xAA),
        new(0xAA, 0x00, 0x00), new(0xAA, 0x00, 0xAA), new(0xAA, 0x55, 0x00), new(0xAA, 0xAA, 0xAA),
        new(0x55, 0x55, 0x55), new(0x55, 0x55, 0xFF), new(0x55, 0xFF, 0x55), new(0x55, 0xFF, 0xFF),
        new(0xFF, 0x55, 0x55), new(0xFF, 0x55, 0xFF), new(0xFF, 0xFF, 0x55), new(0xFF, 0xFF, 0xFF),
    ];

    private readonly Texture2D _texture;
    private readonly Color[] _rgba;

    public FrameTexture(GraphicsDevice device, int width, int height)
    {
        _texture = new Texture2D(device, width, height);
        _rgba = new Color[width * height];
    }

    public Texture2D Texture => _texture;

    public void Upload(IndexedFrame frame)
    {
        var px = frame.Pixels;
        for (int i = 0; i < px.Length; i++)
            _rgba[i] = Ega[px[i] & 15];
        _texture.SetData(_rgba);
    }

    public void Dispose() => _texture.Dispose();
}
