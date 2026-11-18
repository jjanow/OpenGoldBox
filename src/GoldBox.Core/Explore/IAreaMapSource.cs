namespace GoldBox.Core.Explore;

/// <summary>
/// Where LOADFILES gets a 3D map (the DOS program: <c>GEO&lt;disk&gt;.DAX</c> block <c>id</c>; a module:
/// <c>maps/0/&lt;id&gt;.json</c>). Next to <see cref="Ecl.IEclScriptSource"/> for scripts.
/// </summary>
public interface IAreaMapSource
{
    /// <summary>Map <paramref name="id"/> as read on disk <paramref name="disk"/>, or null when that disk has none
    /// (the program stops with "Unable to load geo").</summary>
    AreaMap? LoadMap(int disk, int id);
}
