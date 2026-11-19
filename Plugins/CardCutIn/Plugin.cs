using System.Reflection;
using Gkms.Sdk;

namespace Gkms.Plugins.CardCutIn;

public sealed class Plugin : ILocalifyPlugin
{
    private static MethodInfo master = null!, rows = null!, inventory = null!, owned = null!, cardId = null!;
    private static object? source, sort;
    private static Dictionary<string, List<(string Id, int Rank)>> choices = new(StringComparer.Ordinal);

    public void Start(IPluginContext context)
    {
        const string game = "Assembly-CSharp";
        master = context.Require(game, "Campus.Common.Master.MasterManager", "get_ProduceCardSkinMaster");
        rows = master.ReturnType.GetMethods(GameMethods.Declared)
            .Single(m => m.Name == "GetAllWithSortByKey" && m.GetParameters().Length == 1);
        sort = Activator.CreateInstance(rows.GetParameters()[0].ParameterType);
        var user = context.RequireType(game, "Campus.Common.User.UserDataManager");
        inventory = user.GetProperty("UserProduceCardSkinList", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetGetMethod() ?? throw new MissingMemberException("UserProduceCardSkinList");
        owned = context.Require(game, "Campus.Common.User.UserProduceCardSkinCollection", "IsOwned", "System.String");
        const string card = "Campus.InGame.Card.ExamCardData";
        cardId = context.Require(game, card, "get_Id");
        context.Patch(context.Require(game, card, "get_ProduceCardSkinId"), postfix: GameMethods.Handler<Plugin>(nameof(SelectSkin)));
    }

    private static void Index(object table)
    {
        var list = rows.Invoke(table, new[] { sort });
        if (list == null) return;
        var count = Convert.ToInt32(list.GetType().GetProperty("Count")!.GetValue(list));
        var item = list.GetType().GetProperty("Item")!;
        var next = new Dictionary<string, List<(string Id, int Rank)>>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if (item.GetValue(list, new object[] { i }) is not { } row) continue;
            var type = row.GetType();
            var rank = Convert.ToInt32(type.GetProperty("CutInType")!.GetValue(row));
            if (rank <= 0 || type.GetProperty("ProduceCardId")!.GetValue(row) is not string card ||
                type.GetProperty("Id")!.GetValue(row) is not string skin) continue;
            if (!next.TryGetValue(card, out var variants)) next[card] = variants = new();
            variants.Add((skin, rank));
        }
        foreach (var variants in next.Values) variants.Sort((a,b) => b.Rank.CompareTo(a.Rank));
        choices = next;
        source = count == 0 ? null : table;
    }

    public static void SelectSkin(object __instance, ref string? __result)
    {
        if (!string.IsNullOrEmpty(__result) || cardId.Invoke(__instance, null) is not string id ||
            master.Invoke(null, null) is not { } table || inventory.Invoke(null, null) is not { } collection) return;
        if (!Equals(table, source)) Index(table);
        if (!choices.TryGetValue(id, out var variants)) return;
        foreach (var (skin, _) in variants)
            if ((bool)owned.Invoke(collection, new object[] { skin })!)
            {
                __result = skin;
                return;
            }
    }

    public void Dispose() { source = null; choices.Clear(); }
}
