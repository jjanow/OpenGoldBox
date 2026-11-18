using GoldBox.Core.Ecl;
using GoldBox.Data.Module.Por;

namespace GoldBox.Data.Por;

/// <summary>
/// A shipped save of the content (PoR: slots A and J) as the program reads it, from wherever the content keeps it: a
/// game folder's original <c>SAVGAMx.DAT</c> with its <c>CHRDAT</c> files, or a module's engine save <c>saves/x.json</c>
/// (docs/module-format.md §15; a module holds no copy of the original files, the engine save rebuilds them).
/// </summary>
public static class PorShippedSave
{
    /// <summary>The save of slot <paramref name="slot"/> and its party, or null when the content has none.</summary>
    public static (PorSaveGame Save, EclParty Party)? Open(IPorContent content, char slot)
    {
        if (content.SaveFolder is not { } folder || !Directory.Exists(folder))
            return null;
        var dir = new PorGameDirectory(folder);
        if (dir.Find(PorSaveGame.FileName(slot)) is { } dat)
        {
            var save = PorSaveGame.Open(dat);
            return (save, PorParty.Load(save, folder));
        }
        if (dir.Find(PorEngineSave.FileName(slot)) is { } json)
        {
            var doc = PorEngineSave.Read(json);
            return (PorEngineSave.ToSaveGame(doc), PorEngineSave.Party(doc) ?? new EclParty());
        }
        return null;
    }
}
