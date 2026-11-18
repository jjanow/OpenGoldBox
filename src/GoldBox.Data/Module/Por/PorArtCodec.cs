using GoldBox.Data.Dax;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// DOS art blocks ↔ module art (docs/module-format.md §10): a DaxBlock picture or an animation becomes indexed PNG
/// (the items of a frame stacked top to bottom, the standard EGA colours as the PNG palette) plus a JSON sidecar with
/// the header fields; a block that is not a picture (the font, WALLDEF records) is kept raw in the JSON. Every
/// picture is written only when it rebuilds to the exact block bytes; otherwise it is kept raw.
/// </summary>
public static class PorArtCodec
{
    /// <summary>Writes block <paramref name="id"/> of DAX file <paramref name="name"/> (no extension) under
    /// <paramref name="moduleRoot"/>; returns true when it was written as a picture.</summary>
    public static bool Write(string moduleRoot, string variant, string name, int id, byte[] block, bool tryPicture)
    {
        var (container, bank) = ArtDocument.SplitName(name);
        var rel = ArtDocument.PathOf(variant, container, bank, id);
        string Out(string r) => Path.Combine(moduleRoot, r.Replace('/', Path.DirectorySeparatorChar));
        var source = new SourceRef { Container = name + ".DAX", Block = id };
        if (tryPicture && TryFrames(block) is { } pic)
        {
            var (frames, animation) = pic;
            var doc = new ArtDocument
            {
                Animation = animation,
                Frames = [.. frames.Select(f => new ArtFrame
                {
                    Width = f.Width, Height = f.Height, Items = f.Items.Count, X = f.X, Y = f.Y,
                    Delay = animation ? f.Delay : null, Cga = f.Cga,
                })],
                Source = source,
            };
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                if (f.Items.Count == 0 || f.Width == 0 || f.Height == 0)
                    continue;
                var png = Out(FramePath(rel, animation, i));
                Directory.CreateDirectory(Path.GetDirectoryName(png)!);
                File.WriteAllBytes(png, IndexedPng.Encode(f.Width, f.Height * f.Items.Count, [.. f.Items.SelectMany(x => x)],
                    IndexedPng.EgaPalette));
            }
            ModuleJson.Write(Out(rel + ".json"), doc);
            return true;
        }
        ModuleJson.Write(Out(rel + ".json"), new ArtDocument { Raw = block, Source = source });
        return false;
    }

    /// <summary>The PNG of frame <paramref name="frame"/>: <c>&lt;id&gt;.png</c>, or <c>&lt;id&gt;/&lt;frame&gt;.png</c> in an animation.</summary>
    public static string FramePath(string rel, bool animation, int frame) =>
        animation ? $"{rel}/{frame.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png" : rel + ".png";

    /// <summary>The block's frames when it decodes as a picture or animation that encodes back to the same bytes.</summary>
    private static (IReadOnlyList<DaxPicture> Frames, bool Animation)? TryFrames(byte[] block)
    {
        try
        {
            bool anim = DaxPicture.IsAnimation(block);
            var frames = DaxPicture.DecodeFrames(block);
            var back = anim ? DaxPicture.EncodeAnimation(frames) : frames[0].Encode();
            return back.AsSpan().SequenceEqual(block) ? (frames, anim) : null;
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }

    /// <summary>The frames of an art document whose JSON is at <paramref name="jsonPath"/>, or null for a raw block.</summary>
    public static IReadOnlyList<DaxPicture>? ReadFrames(ArtDocument doc, string jsonPath)
    {
        if (doc.Frames is not { } frames)
            return null;
        var rel = jsonPath[..^".json".Length];
        var list = new List<DaxPicture>(frames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            var items = new List<byte[]>(f.Items);
            if (f.Items > 0 && f.Width > 0 && f.Height > 0)
            {
                var (w, h, px) = IndexedPng.Decode(File.ReadAllBytes(FramePath(rel, doc.Animation, i)));
                if (w != f.Width || h != f.Height * f.Items)
                    throw new FormatException($"{jsonPath}: frame {i} PNG is {w}×{h}, the sidecar says {f.Width}×{f.Height}×{f.Items}");
                for (int k = 0; k < f.Items; k++)
                    items.Add(px.AsSpan(k * f.Width * f.Height, f.Width * f.Height).ToArray());
            }
            else
                for (int k = 0; k < f.Items; k++)
                    items.Add(new byte[f.Width * f.Height]);
            list.Add(DaxPicture.Create(f.Width, f.Height, f.X, f.Y, items, f.Cga, f.Delay));
        }
        return list;
    }

    /// <summary>The original block bytes of an art document (round trip).</summary>
    public static byte[] Block(ArtDocument doc, string jsonPath)
    {
        if (doc.Raw is { } raw)
            return raw;
        var frames = ReadFrames(doc, jsonPath)!;
        return doc.Animation ? DaxPicture.EncodeAnimation(frames) : frames[0].Encode();
    }
}
