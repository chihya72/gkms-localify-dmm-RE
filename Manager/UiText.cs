namespace Gkms.Manager;

internal static class UiText
{
    public static string Language { get; private set; } = "zh-CN";
    private static string file = "";
    public static readonly string[] Languages = { "zh-CN", "zh-TW", "en", "ja" };
    public static void Load(string root)
    {
        file = Path.Combine(root, "language.txt");
        if (File.Exists(file) && Languages.Contains(File.ReadAllText(file).Trim())) Language = File.ReadAllText(file).Trim();
    }
    public static void Select(string language) { Language = language; File.WriteAllText(file, language); }
    public static string T(string cn, string tw, string en, string ja) => Language switch
        { "zh-TW" => tw, "en" => en, "ja" => ja, _ => cn };
    public static string Save => T("保存", "儲存", "Save", "保存");
    public static string Cancel => T("取消", "取消", "Cancel", "キャンセル");
    public static string Error => T("操作失败", "操作失敗", "Operation failed", "操作失敗");
    public static string Offline => T("未连接游戏，已保存的配置将在下次加载时应用。", "未連接遊戲，已儲存的設定將在下次載入時套用。",
        "Game is offline. Saved settings will apply on the next load.", "ゲームに未接続です。保存した設定は次回起動時に適用されます。");
    public static string Applied => T("已在运行中应用。", "已在執行中套用。", "Applied to the running game.", "実行中のゲームに適用しました。");
}
