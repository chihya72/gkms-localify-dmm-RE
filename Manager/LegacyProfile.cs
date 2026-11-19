using System.Text.Json.Nodes;
using Gkms.Host;

namespace Gkms.Manager;

internal static class LegacyProfile
{
    public static void Import(string root, IReadOnlyDictionary<string, Package> packages)
    {
        var data = Path.GetFullPath(Path.Combine(root, ".."));
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(data, "localizationConfig.json")))!.AsObject();
        bool Flag(string key, bool fallback = false) => (bool?)config[key] ?? fallback;
        int Number(string key) => (int?)config[key] ?? 0;
        var enabled = Flag("enabled", true);
        var flags = new Dictionary<string, bool>
        {
            ["localization"] = true,
            ["textures"] = Flag("replaceTexture", true), ["frame-rate"] = Number("targetFrameRate") > 0,
            ["schedule-sp"] = Flag("dbgMode"), ["card-cut-in"] = Flag("dbgMode"),
            ["purchase-guard"] = Flag("loginAsIOS"),
            ["graphics"] = Flag("useCustomeGraphicSettings"), ["window"] = Number("gameOrientation") != 0 || Flag("dmmUnlockSize"),
            ["camera"] = Flag("enableFreeCamera"), ["gyro"] = Flag("enableGyroEmu"), ["physics"] = Flag("enableBreastParam"),
            ["capture"] = Flag("dumpText") || Flag("dumpRuntimeTexture"),
            ["auto-photo"] = Flag("dbgMode")
        };
        var settings = new Dictionary<string, JsonObject>();
        settings["localization"] = new JsonObject { ["textTest"] = Flag("textTest") };
        void Map(string id, params (string Old, string New)[] pairs)
        {
            var result = new JsonObject();
            foreach (var pair in pairs)
                if (config[pair.Old] is { } value) result[pair.New] = JsonNode.Parse(value.ToJsonString());
            settings[id] = result;
        }
        if (Number("targetFrameRate") > 0) Map("frame-rate", ("targetFrameRate","fps"));
        Map("gyro", ("gyroSensitivity","sensitivity"), ("gyroInvertX","invertX"), ("gyroInvertY","invertY"));
        Map("graphics", ("renderScale","renderScale"), ("qualitySettingsLevel","quality"), ("volumeIndex","volumeIndex"),
            ("maxBufferPixel","maxBufferPixel"), ("reflectionQualityLevel","reflectionQuality"), ("lodQualityLevel","lodQuality"));
        Map("physics", ("bDamping","damping"), ("bStiffness","stiffness"), ("bSpring","spring"), ("bPendulum","pendulum"),
            ("bPendulumRange","pendulumRange"), ("bAverage","average"), ("bRootWeight","rootWeight"), ("bUseArmCorrection","useArmCorrection"),
            ("bUseScale","useScale"), ("bScale","scale"), ("bUseLimit","useLimit"));
        foreach (var axis in new[] { "X", "Y", "Z" })
            if (config[$"bLimit{axis}x"] != null || config[$"bLimit{axis}y"] != null)
                settings["physics"]["axis" + axis] = new JsonArray((float?)config[$"bLimit{axis}x"] ?? 1f, (float?)config[$"bLimit{axis}y"] ?? 1f);
        Map("window", ("dmmUnlockSize","resizable"));
        settings["window"]["orientation"] = Number("gameOrientation") switch { 1 => "portrait", 2 => "landscape", _ => "game" };
        ProfileEditor.Apply(root, packages, flags.Where(p => packages.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => enabled && p.Value), settings);
    }
}
