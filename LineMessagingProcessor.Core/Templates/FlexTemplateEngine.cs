/*
 * 檔案整體說明
 *
 * 目的與定位：雙目標LINE Flex純範本引擎，借用ITextCatalog解析已授權範圍的文字，
 * 不讀D365、網路、檔案或任意CLR型別；輸出正式FlexMessage供Core單次推播凍結。
 * 主要功能與流程：先驗有界JSON、唯一屬性及altText取字key，只在字串值單次替換
 * {{t:Key}}與{{v:name}}，以JSON token輸出確保引號、換行與插入模板不改變結構。
 * 使用邊界與維護：每次候選與值由呼叫操作獨占，不保留context、資料、快照或憑證；
 * 模板32Ki字元、資料256項、字串32Ki、結果256Ki，沒有快取、timer、queue或Task。
 * 缺鍵、壞語法、重複／超限皆在發布前失敗，真例外交最終主機共用入口。語系不是授權，
 * 呼叫端必須先驗完整組織／產品／教會；本元件不接管catalog生命週期。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Line.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;

namespace LineMessagingProcessor.Core.Templates;

/// <summary>無操作狀態的Flex替換服務，只借用無request狀態的字典。</summary>
public sealed class FlexTemplateEngine
{
    private readonly ITextCatalog catalog;
    /// <summary>借用呼叫端擁有的完整scope字典，不Dispose或保留其當次查詢結果。</summary>
    public FlexTemplateEngine(ITextCatalog catalog) =>
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>完整候選通過後才發布正式Flex訊息；資料插入一次，不重新當模板解譯。</summary>
    public FlexMessage Fill(string template, TextContext context, IReadOnlyDictionary<string, string> values)
    {
#if NETFRAMEWORK
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (values is null) throw new ArgumentNullException(nameof(values));
#else
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(values);
#endif
        if (string.IsNullOrEmpty(template) || template.Length > 32768 || values.Count > 256)
            throw new FormatException("Template input exceeds its budget.");
        foreach (var pair in values)
            if (!ValidName(pair.Key) || pair.Value is null || pair.Value.Length > 32768)
                throw new FormatException("Invalid bounded template value.");
        JObject candidate;
        try
        {
            using var input = new StringReader(template);
            using var reader = new JsonTextReader(input) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            candidate = JObject.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                CommentHandling = CommentHandling.Load
            });
            if (reader.Read()) throw new FormatException("Unexpected trailing template data.");
        }
        catch (JsonException) { throw new FormatException("Invalid template JSON."); }
        if (candidate["type"]?.Type != JTokenType.String || candidate["type"]!.Value<string>() != "flex" ||
            candidate.Properties().Any(p => p.Name is not ("type" or "altText" or "contents")) ||
            candidate["contents"] is not JObject contents ||
            contents["type"]?.Type != JTokenType.String || contents["type"]!.Value<string>() is not ("bubble" or "carousel"))
            throw new FormatException("Invalid Flex template envelope.");
        var alt = candidate["altText"]?.Type == JTokenType.String ? candidate["altText"]!.Value<string>() : null;
        if (alt is null || !alt.StartsWith("{{t:", StringComparison.Ordinal) ||
            !alt.EndsWith("}}", StringComparison.Ordinal) || !ValidName(alt.Substring(4, alt.Length - 6)))
            throw new FormatException("An altText translation key is required.");
        // 總文字預算32Ki，連同JSON最壞六倍跳脫及原結構仍小於256Ki；不先建立無界輸出再檢查。
        var remaining = 32768;
        ReplaceValues(candidate, context, values, ref remaining);
        var resolvedAlt = candidate["altText"]!.Value<string>()!;
        if (string.IsNullOrWhiteSpace(resolvedAlt) || resolvedAlt.Length > 400)
            throw new FormatException("Invalid resolved altText size.");
        return new FlexMessage(string.Empty) { AltText = resolvedAlt, Contents = new TemplateContainer(contents) };
    }

    /// <summary>只遍歷候選JSON原始節點；替換後文字不再進入此遞迴，防止資料被二次解譯。</summary>
    private void ReplaceValues(JToken node, TextContext context, IReadOnlyDictionary<string, string> values, ref int remaining)
    {
        if (node is JProperty property)
        {
#if NETFRAMEWORK
            if (property.Name.IndexOf("{{", StringComparison.Ordinal) >= 0)
#else
            if (property.Name.Contains("{{", StringComparison.Ordinal))
#endif
                throw new FormatException("Template property names cannot be substituted.");
            ReplaceValues(property.Value, context, values, ref remaining);
        }
        else if (node is JValue value && value.Type == JTokenType.String)
        {
            var text = ReplaceText(value.Value<string>()!, context, values);
            remaining -= text.Length;
            if (remaining < 0) throw new FormatException("Template text budget exceeded.");
            value.Value = text;
        }
        else if (node is JContainer container)
        {
            foreach (var child in container.Children()) ReplaceValues(child, context, values, ref remaining);
        }
        else if (node.Type == JTokenType.Comment) throw new FormatException("Template comments are not supported.");
    }

    /// <summary>解析原字串一次，以有界builder組候選；缺鍵與不完整標記不回傳部分資料。</summary>
    private string ReplaceText(string input, TextContext context, IReadOnlyDictionary<string, string> values)
    {
        var result = new StringBuilder(Math.Min(input.Length, 1024));
        var offset = 0;
        while (offset < input.Length)
        {
            var open = input.IndexOf("{{", offset, StringComparison.Ordinal);
            if (open < 0) { Append(result, input.Substring(offset)); break; }
            Append(result, input.Substring(offset, open - offset));
            var close = input.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) throw new FormatException("Unclosed template placeholder.");
            var expression = input.Substring(open + 2, close - open - 2);
            if (expression.Length < 3 || expression[1] != ':' || !ValidName(expression.Substring(2)))
                throw new FormatException("Invalid template placeholder.");
            var name = expression.Substring(2);
            string replacement;
            if (expression[0] == 't') replacement = catalog.GetText(context, new TextKey(name));
            else if (expression[0] == 'v' && values.TryGetValue(name, out var data)) replacement = data;
            else throw new FormatException("Unknown or missing template placeholder.");
            if (replacement is null) throw new FormatException("A template value is missing.");
            Append(result, replacement);
            offset = close + 2;
        }
        return result.ToString();
    }

    /// <summary>寫入前先驗單字串32Ki預算，避免惡意重複標記造成無界配置。</summary>
    private static void Append(StringBuilder builder, string value)
    {
        if (value.Length > 32768 - builder.Length) throw new FormatException("Template string budget exceeded.");
        builder.Append(value);
    }

    /// <summary>固定ASCII名稱文法，不允許路徑、運算式、巢狀模板或以欄位名注入JSON結構。</summary>
    private static bool ValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128) return false;
        foreach (var character in name)
            if (character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and
                not '_' and not '.' and not '-') return false;
        return true;
    }

    /// <summary>由已驗候選擁有的私有Flex投影；不暴露可變JToken或來源，序列化保留協定欄位。</summary>
    private sealed class TemplateContainer : IFlexContainer
    {
        [JsonExtensionData]
        private readonly Dictionary<string, JToken> fields = new(StringComparer.Ordinal);
        /// <summary>僅在bubble／carousel包裝驗證通過後建構，候選不再被其他owner使用。</summary>
        internal TemplateContainer(JObject contents)
        {
            Type = contents["type"]!.Value<string>() == "bubble" ? FlexContainerType.Bubble : FlexContainerType.Carousel;
            foreach (var property in contents.Properties())
                if (property.Name != "type") fields.Add(property.Name, property.Value);
        }
        /// <summary>正式容器型別；其餘欄位只由JSON serializer從私有extension data讀取。</summary>
        public FlexContainerType Type { get; }
    }
}
