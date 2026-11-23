// Offline inspection only. Does not load a game/plugin DLL or attach to a process.
// Local.cpp:91 LoadJsonDataToMap, :198 IsPureStringValue, :209 SplitByTags,
// :232 ProcessGenericTextLabels, :380 LoadData (original gkms-localify-dmm).
// Build with the original MSVC STL and nlohmann version. Output is evidence for
// this toolchain/resource tree, not a promise about other binary/resource builds.
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <ranges>
#include <regex>
#include <set>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <nlohmann/json.hpp>

std::unordered_map<std::string, std::string> i18nData{}, genericText{}, genericSplitText{}, genericFmtText{};
std::unordered_set<std::string> translatedText{};
const std::string splitTextPrefix = "[__split__]";
void LoadJsonDataToMap(const std::filesystem::path& filePath, std::unordered_map<std::string, std::string>& dict,
    const bool insertToTranslated = false, const bool needClearDict = true, const bool needCheckSplitPrefix = false)
{
    if (!exists(filePath)) return;
    if (needClearDict) dict.clear();
    std::ifstream file(filePath);
    if (!file.is_open()) throw std::runtime_error("Cannot open " + filePath.string());
    std::string fileContent((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    file.close();
    auto fileData = nlohmann::json::parse(fileContent);
    for (auto& i : fileData.items()) {
        const auto& key = i.key();
        const std::string value = i.value();
        if (needCheckSplitPrefix && key.starts_with(splitTextPrefix) && value.starts_with(splitTextPrefix)) {
            static const auto splitTextPrefixLength = splitTextPrefix.size();
            const auto splitValue = value.substr(splitTextPrefixLength);
            genericSplitText[key.substr(splitTextPrefixLength)] = splitValue;
            if (insertToTranslated) translatedText.emplace(splitValue);
        } else {
            dict[key] = value;
            if (insertToTranslated) translatedText.emplace(value);
        }
    }
}
bool IsPureStringValue(const std::string& str) {
    static std::unordered_set<char> notDeeds = {'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ':',
        '/', ' ', '.', '%', ',', '+', '-', 'x', '\n'};
    for (const auto& i : str) if (!notDeeds.contains(i)) return false;
    return true;
}
std::vector<std::string> SplitByTags(const std::string& origText) {
    static const std::regex tagsRe("<.*?>(.*?)</.*?>");
    std::string text = origText;
    std::smatch match;
    std::vector<std::string> ret{};
    std::string lastSuffix;
    while (std::regex_search(text, match, tagsRe)) {
        const auto tagValue = match[1].str();
        if (IsPureStringValue(tagValue)) {
            ret.push_back(match.prefix().str());
            lastSuffix = match.suffix().str();
        }
        text = match.suffix().str();
    }
    if (!lastSuffix.empty()) ret.push_back(lastSuffix);
    return ret;
}
void ProcessGenericTextLabels() {
    std::unordered_map<std::string, std::string> appendsText{};
    for (const auto& i : genericText) {
        const auto origContents = SplitByTags(i.first);
        if (origContents.empty()) continue;
        const auto translatedContents = SplitByTags(i.second);
        if (origContents.size() == translatedContents.size())
            for (const auto& [orig, trans] : std::ranges::views::zip(origContents, translatedContents))
                appendsText.emplace(orig, trans);
    }
    genericText.insert(appendsText.begin(), appendsText.end());
}
int wmain(int argc, wchar_t** argv) {
    try {
        if (argc < 2 || argc > 3) throw std::runtime_error("Usage: DmmDerivedTextProbe.exe <resources> [load-count]");
        const auto loads = argc == 3 ? std::stoi(argv[2]) : 1;
        if (loads < 1 || loads > 10) throw std::runtime_error("load-count must be 1..10");
        const std::filesystem::path root(argv[1]);
        std::map<std::string, std::set<std::string>> firstCandidates;
        for (int pass = 1; pass <= loads; ++pass) {
        if (!std::filesystem::is_regular_file(root / "localization.json")) throw std::runtime_error("Missing localization.json");
        LoadJsonDataToMap(root / "localization.json", i18nData, true);
        LoadJsonDataToMap(root / "generic.json", genericText, true, true, true);
        genericSplitText.clear(); genericFmtText.clear();
        LoadJsonDataToMap(root / "generic.split.json", genericSplitText, true, true, true);
        const auto genericDir = root / "genericTrans";
        if (std::filesystem::exists(genericDir) || std::filesystem::is_directory(genericDir))
            for (const auto& entry : std::filesystem::recursive_directory_iterator(genericDir)) {
                if (!std::filesystem::is_regular_file(entry.path())) continue;
                const auto& currFile = entry.path();
                if (currFile.filename() == "home_voice_subtitles.json") continue;
                auto extension = currFile.extension().string();
                std::transform(extension.begin(), extension.end(), extension.begin(), ::tolower);
                if (extension != ".json") continue;
                if (currFile.filename().string().ends_with(".split.json")) LoadJsonDataToMap(currFile, genericSplitText, true, false, true);
                if (currFile.filename().string().ends_with(".fmt.json")) LoadJsonDataToMap(currFile, genericFmtText, true, false, false);
                else LoadJsonDataToMap(currFile, genericText, true, false, true);
            }
        std::map<std::string, std::set<std::string>> candidates;
        for (const auto& [key, value] : genericText) {
            const auto sources = SplitByTags(key), values = SplitByTags(value);
            if (sources.size() != values.size()) continue;
            for (const auto& [source, translated] : std::ranges::views::zip(sources, values))
                if (!genericText.contains(source)) candidates[source].emplace(translated);
        }
        const auto initialCount = genericText.size();
        if (pass == 1) firstCandidates = candidates;
        ProcessGenericTextLabels();
        nlohmann::json result = {{"load", pass}, {"mscFullVer", _MSC_FULL_VER}, {"stlVersion", _MSVC_STL_UPDATE},
            {"jsonVersion", NLOHMANN_JSON_VERSION_MAJOR * 10000 + NLOHMANN_JSON_VERSION_MINOR * 100 + NLOHMANN_JSON_VERSION_PATCH},
            {"initialExactCount", initialCount}, {"finalExactCount", genericText.size()}, {"conflicts", nlohmann::json::array()}};
        for (const auto& [key, values] : firstCandidates)
            if (values.size() > 1) result["conflicts"].push_back({{"key", key}, {"candidates", values}, {"nativeValue", genericText.at(key)}});
        std::cout << result.dump() << '\n';
        }
        return 0;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
