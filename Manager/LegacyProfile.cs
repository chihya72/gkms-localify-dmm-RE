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
        var subtitles = Path.Combine(data, "local-files", "genericTrans", "genericTrans", "lyrics", "home_voice_subtitles.json");
        var flags = new Dictionary<string, bool>
        {
            ["i18n"] = true, ["text"] = true, ["story"] = true, ["master"] = Flag("useMasterTrans", true),
            ["font"] = Flag("replaceFont", true), ["textures"] = Flag("replaceTexture", true),
            ["subtitles"] = File.Exists(subtitles), ["frame-rate"] = Number("targetFrameRate") > 0,
            ["graphics"] = Flag("useCustomeGraphicSettings"), ["window"] = Number("gameOrientation") != 0 || Flag("dmmUnlockSize"),
            ["camera"] = Flag("enableFreeCamera"), ["gyro"] = Flag("enableGyroEmu"), ["physics"] = Flag("enableBreastParam"),
            ["capture"] = Flag("dumpText"), ["texture-capture"] = Flag("dumpRuntimeTexture"),
            ["unity-log"] = Flag("dbgMode"), ["auto-photo"] = Flag("dbgMode"), ["lyrics"] = Flag("dbgMode") && Flag("unlockAllLive")
        };
        var settings = new Dictionary<string, JsonObject>();
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
            ("bPendulumRange","pendulumRange"), ("bAverage","average"), ("bRootWeight","rootWeight"), ("bUseArmCorrection","useArmCorrection"));
        Map("window", ("dmmUnlockSize","resizable"));
        settings["window"]["orientation"] = Number("gameOrientation") switch { 1 => "portrait", 2 => "landscape", _ => "game" };
        ProfileEditor.Apply(root, packages, flags.Where(p => packages.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => enabled && p.Value), settings);
    }
}
