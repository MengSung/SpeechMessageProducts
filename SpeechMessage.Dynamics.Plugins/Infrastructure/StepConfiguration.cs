/*
 * 檔案整體說明
 *
 * 目的與定位：解析 Plugin step 的非機密 unsecure JSON 與 secure 設定，建立不可變
 * 註冊設定。解析只發生於 Plugin 建構式，不把原始 JSON、秘密或可變 JObject
 * 留在 reusable plugin instance，也不在錯誤訊息回顯輸入。
 *
 * 主要功能與流程：限制 JSON 長度 4,096、拒絕重複屬性與未知模式；mode 只接受
 * Shadow／Live，Shadow 必須沒有 secure config。其他 scalar 選項以 detached
 * 字串字典保存，供各模組讀取。
 *
 * 使用邊界與維護：secure config 只做存在性邊界判定，內容不寫 trace、log、測試
 * 輸出或回報。所有設定是註冊步驟的唯讀值，沒有跨使用者或跨組織共享。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpeechMessage.Dynamics.Plugins.Infrastructure;

/// <summary>步驟執行模式。</summary>
internal enum PluginExecutionMode
{
    /// <summary>只記錄，不寫入 Dataverse 或推播。</summary>
    Shadow,
    /// <summary>允許規則依其契約使用真服務。</summary>
    Live
}

/// <summary>不可變的 Plugin step 設定。</summary>
internal sealed class StepConfiguration
{
    /// <summary>解析並驗證 step 設定。</summary>
    public StepConfiguration(string unsecureConfig, string secureConfig)
    {
        if (string.IsNullOrWhiteSpace(unsecureConfig) || unsecureConfig.Length > 4096)
            throw InvalidConfigurationException();
        try
        {
            using var text = new StringReader(unsecureConfig);
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 8 };
            var json = JObject.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (reader.Read()) throw InvalidConfigurationException();
            var properties = json.Properties().ToArray();
            if (properties.Any(property => !string.Equals(property.Name, "mode", StringComparison.Ordinal) &&
                                           !string.Equals(property.Name, "options", StringComparison.Ordinal)))
                throw InvalidConfigurationException();

            if (json["mode"]?.Type != JTokenType.String) throw InvalidConfigurationException();
            var mode = json.Value<string>("mode");
            var parsedMode = mode switch
            {
                "Shadow" => PluginExecutionMode.Shadow,
                "Live" => PluginExecutionMode.Live,
                _ => throw InvalidConfigurationException()
            };
            if (parsedMode == PluginExecutionMode.Shadow && !string.IsNullOrWhiteSpace(secureConfig))
                throw InvalidConfigurationException();

            Mode = parsedMode;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (json["options"] is not null && json["options"] is not JObject) throw InvalidConfigurationException();
            if (json["options"] is JObject options)
            {
                foreach (var option in options.Properties())
                {
                    if (option.Value.Type is not (JTokenType.String or JTokenType.Boolean or JTokenType.Integer or JTokenType.Float))
                        throw InvalidConfigurationException();
                    values.Add(option.Name, (option.Value.Type == JTokenType.String ? option.Value.Value<string>()! : option.Value.ToString(Formatting.None)));
                }
            }
            Options = new ReadOnlyDictionary<string, string>(values);
            SecureConfig = secureConfig ?? string.Empty;
        }
        catch (JsonException)
        {
            throw InvalidConfigurationException();
        }
        catch (ArgumentException)
        {
            throw InvalidConfigurationException();
        }
    }

    /// <summary>目前步驟模式。</summary>
    public PluginExecutionMode Mode { get; }
    /// <summary>非機密 scalar 選項快照。</summary>
    public IReadOnlyDictionary<string, string> Options { get; }
    /// <summary>Live 模式下由外殼持有的 secure 字串；不應進追蹤。</summary>
    public string SecureConfig { get; }

    /// <summary>回傳不含輸入內容的固定繁中設定錯誤。</summary>
    public static InvalidPluginExecutionException InvalidConfigurationException() =>
        new(new SystemTextTable().GetText("zh-TW", new TextKey("Common.Plugin.InvalidConfiguration")));
}
