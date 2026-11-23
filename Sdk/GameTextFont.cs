namespace Gkms.Sdk;

/// <summary>Text hooks request DMM font timing; the host owns the font for the entire session.</summary>
public static class GameTextFont
{
    public static Action<object>? ApplyFont { get; set; }
    public static bool TranslationHooksActive { get; set; }
    public static void Apply(object text) => ApplyFont?.Invoke(text);

    public static void Refresh(object text)
    {
        var type = Interop.TypeOf(text);
        type.GetMethod("UpdateMeshPadding", Type.EmptyTypes)?.Invoke(text, null);
        type.GetMethod("SetAllDirty", Type.EmptyTypes)?.Invoke(text, null);
        type.GetMethod("ForceMeshUpdate", new[] { typeof(bool), typeof(bool) })?.Invoke(text, new object[] { true, true });
    }
}
