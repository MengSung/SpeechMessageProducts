/*
檔案整體說明
目的與定位：P7與ActivityProbe共用的工作流程XML活動參照解析／精準改寫器，不執行或載入XAML中的型別。
主要流程：禁止DTD及外部資源，先驗單份1MiB與深度128，再解析AssemblyQualifiedName及clr-namespace節點。
只比對完整型別與組件簡名（組件忽略大小寫）；改寫時使用呼叫端核對的新版assembly識別，其他正文不變。
資源與隔離：原XAML只在單次命令內保留，XML reader/StringReader確定釋放；無SDK、秘密、快取或背景工作。
失敗與回復：解析錯誤使用固定訊息不洩露原文；回復必用switch-log原始字串，不能用本檔重序列化輸出代替。
驗證：WorkflowRepointerTests保護帶版本、大小寫、混用參照與深度拒絕，既有ActivityProbeTests保持相容。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
namespace SpeechMessage.Dynamics.Deployment.Switching;

/// <summary>只持有組件簡名與完整型別的純值引用，不保存XML樹或活動執行個體。</summary>
/// <param name="Assembly">組件簡名，供已驗證舊組件清冊對照。</param>
/// <param name="Type">精確大小寫的完整CLR型別。</param>
internal sealed record WorkflowActivityReference(string Assembly, string Type);

/// <summary>具名舊新活動對照；新版assembly可包含PE驗出的Version/Culture/PublicKeyToken。</summary>
/// <param name="OldAssembly">舊組件簡名。</param><param name="OldType">舊完整型別。</param>
/// <param name="NewAssembly">新版組件識別。</param><param name="NewType">新版完整型別。</param>
internal sealed record WorkflowActivityReplacement(string OldAssembly, string OldType, string NewAssembly, string NewType);

/// <summary>只對核定XML位置作型別改寫的無狀態入口；呼叫端負責原字串備份與組織授權。</summary>
internal static class WorkflowRepointer
{
    /// <summary>解析活動引用位置，不把正文或任意屬性的相似字串當成活動；回傳獨立純值集合。</summary>
    internal static IReadOnlyList<WorkflowActivityReference> ReadReferences(string xaml)
    {
        var document = Parse(xaml);
        var found = new List<WorkflowActivityReference>();
        foreach (var element in document.Root!.DescendantsAndSelf())
        {
            if (Qualified(element.Attribute("AssemblyQualifiedName")?.Value) is { } qualified) found.Add(qualified);
            if (ClrReference(element.Name) is { } reference) found.Add(reference);
        }
        return found.AsReadOnly();
    }

    /// <summary>每個對照至少命中一處，完整替換型別引用；未核定位置及正文保持原語意。</summary>
    internal static string Rewrite(string xaml, IReadOnlyList<WorkflowActivityReplacement> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.Count is < 1 or > 256) throw Invalid();
        var document = Parse(xaml);
        foreach (var replacement in replacements)
        {
            if (string.IsNullOrWhiteSpace(replacement.OldAssembly) || string.IsNullOrWhiteSpace(replacement.OldType) ||
                string.IsNullOrWhiteSpace(replacement.NewAssembly) || replacement.NewAssembly.Length > 512 ||
                string.IsNullOrWhiteSpace(replacement.NewType) || replacement.NewType.Length > 512) throw Invalid();
            var changed = 0;
            foreach (var element in document.Root!.DescendantsAndSelf())
            {
                var attribute = element.Attribute("AssemblyQualifiedName");
                if (Matches(Qualified(attribute?.Value), replacement))
                {
                    attribute!.Value = replacement.NewType + ", " + replacement.NewAssembly;
                    changed++;
                }
                if (Matches(ClrReference(element.Name), replacement))
                {
                    var separator = replacement.NewType.LastIndexOf('.');
                    var ns = separator < 0 ? "" : replacement.NewType[..separator];
                    var assembly = replacement.NewAssembly.Split(',')[0].Trim();
                    element.Name = XName.Get(replacement.NewType[(separator + 1)..], "clr-namespace:" + ns + ";assembly=" + assembly);
                    changed++;
                }
            }
            if (changed == 0) throw Invalid();
        }
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>型別Ordinal、組件簡名OrdinalIgnoreCase，與CLR組件識別一致但不猜測短型別名。</summary>
    internal static bool SameReference(WorkflowActivityReference reference, string assembly, string type) =>
        reference.Type == type && string.Equals(reference.Assembly, assembly, StringComparison.OrdinalIgnoreCase);

    /// <summary>空引用不命中；避免將任意屬性文字或未核對的組件當成可改寫對象。</summary>
    private static bool Matches(WorkflowActivityReference? reference, WorkflowActivityReplacement replacement) =>
        reference is not null && SameReference(reference, replacement.OldAssembly, replacement.OldType);

    /// <summary>只讀qualified name的型別與組件簡名；版本與簽章由核定的新assembly識別完整取代。</summary>
    private static WorkflowActivityReference? Qualified(string? value)
    {
        var parts = value?.Split(',');
        return parts is { Length: >= 2 } && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1])
            ? new WorkflowActivityReference(parts[1].Trim(), parts[0].Trim()) : null;
    }

    /// <summary>只解釋CLR namespace元素名，不讀namespace宣告、文字或外部資源。</summary>
    private static WorkflowActivityReference? ClrReference(XName name)
    {
        const string prefix = "clr-namespace:";
        var ns = name.NamespaceName;
        if (!ns.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var separator = ns.IndexOf(";assembly=", StringComparison.Ordinal);
        if (separator < prefix.Length) return null;
        var assembly = ns[(separator + 10)..].Split(',')[0].Trim();
        var typeNamespace = ns[prefix.Length..separator];
        return string.IsNullOrWhiteSpace(assembly) ? null : new WorkflowActivityReference(assembly,
            (typeNamespace.Length == 0 ? "" : typeNamespace + ".") + name.LocalName);
    }

    /// <summary>先串流驗深度再建立最多1MiB的XML候選；兩個reader都在返回前釋放，錯誤不含XAML。</summary>
    private static XDocument Parse(string xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml) || xaml.Length > 1024 * 1024 || System.Text.Encoding.UTF8.GetByteCount(xaml) > 1024 * 1024) throw Invalid();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024, MaxCharactersFromEntities = 0 };
        try
        {
            using (var text = new StringReader(xaml))
            using (var reader = XmlReader.Create(text, settings))
                while (reader.Read()) if (reader.Depth > 128) throw Invalid();
            using var source = new StringReader(xaml);
            using var parser = XmlReader.Create(source, settings);
            var result = XDocument.Load(parser, LoadOptions.PreserveWhitespace);
            return result.Root is null ? throw Invalid() : result;
        }
        catch (XmlException) { throw Invalid(); }
    }

    /// <summary>固定安全錯誤不保存parser原例外，避免原始XAML被帶入診斷。</summary>
    private static InvalidDataException Invalid() => new("The activity workflow definition is invalid.");
}
