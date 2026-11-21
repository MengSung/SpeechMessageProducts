/*
檔案整體說明
目的與定位：Deployment Live 工作流程樣板功能的純記憶體 XML 投影器，將停用流程中唯一命中舊自訂活動 catalog 的 ActivityReference 結構轉成可審查的脫敏形狀。
主要流程：接受有界 XAML 字串，以禁止 DTD 與外部資源的 XmlReader 解析；一般投影僅接受單一 ActivityReference，catalog 投影可從多個參照中精確選出唯一舊自訂活動，且只複製目標及其祖先路徑。輸出元素／屬性結構、namespace、參數鍵及型別標記；只有最近所屬參數為 InArgument 的文字與巢狀 Value 屬性才分類為固定輸入或主要記錄欄位繫結，OutArgument 及其他文字只保留脫敏佔位符，不得用來滿足輸入 shape 條件。
依賴與邊界：呼叫端必須先用正式 Deployment CLI 的 goodshepherdback 白名單及唯讀 lease 取得來源。此類別本身不連線、不寫檔、不記錄輸入、不認證組織，也不能證明來源流程停用或 XAML 為實機資料。
資源與隔離：XML reader、文件與投影樹均為單次呼叫區域資源，處理完成即釋放；輸入限 256 KiB、巢狀深度 64 層及 2,048 個元素，不建立快取、背景工作或跨命令狀態。
失敗與驗證：超限、DTD、無效 XML、結構過深、元素過多、一般投影的 ActivityReference 數量非一或 catalog 投影的唯一命中數非一，均以固定 InvalidDataException 或 null 拒絕，不帶入原始本文。測試以合成內容驗證脫敏與限制；正式 golden 仍須由核准組織的唯讀流程產生。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using SpeechMessage.Dynamics.Deployment.Switching;

namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>不含實際型別或組件名稱的舊活動身份比較結果，僅供安全診斷計數。</summary>
internal readonly record struct WorkflowActivityIdentityMatch(
    bool HasIdentity,
    bool TypeMatched,
    bool AssemblyMatched,
    bool ReversedMatched,
    bool RegisteredMatch);

/// <summary>將一般來源 XAML 的單一活動參照，或舊 catalog 唯一命中節點，投影為脫敏有限結構。</summary>
internal static class WorkflowActivityShapeProjector
{
    /// <summary>解析前的 UTF-16 字元上限；部署情境使用的流程定義遠低於此限制。</summary>
    internal const int MaximumInputCharacters = 256 * 1024;
    private const int MaximumDepth = 64;
    private const int MaximumElements = 2048;
    private const string FixedValueMarker = "[FIXED_VALUE]";
    private const string RecordFieldMarker = "[RECORD_FIELD]";

    /// <summary>
    /// 只保留唯一 ActivityReference 的節點形狀及必要型別標籤；來源文字與一般屬性值不會輸出。
    /// DTD、外部實體、超限文件及多重活動均拒絕，確保呼叫端不會把不完整或含額外副作用的流程當作樣板。
    /// </summary>
    /// <param name="xaml">單次讀取的流程定義；不得由此方法寫入紀錄或持久化原文。</param>
    /// <returns>固定格式、已脫敏且僅含單一活動參照的 XML 字串。</returns>
    /// <exception cref="InvalidDataException">來源無效、超出界限或不符合唯一活動參照契約。</exception>
    internal static string Project(string xaml)
    {
        return ProjectCore(xaml, null) ?? throw Invalid();
    }

    /// <summary>
    /// 從多個 ActivityReference 中只投影完整名稱與組件簡名唯一命中舊活動 catalog 的節點。
    /// 其他參照不會複製到結果；識別值僅在此同步呼叫內比對，不由回傳值、例外或輸出 writer 洩漏。
    /// </summary>
    /// <param name="xaml">單次讀取且不得保存原文的流程定義。</param>
    /// <param name="legacyActivities">同一已核對 snapshot 的舊自訂活動 catalog。</param>
    /// <returns>catalog 精確命中的結構投影；零個或多個舊活動命中時回傳 null。</returns>
    internal static string? ProjectLegacyActivity(
        string xaml,
        IReadOnlyList<WorkflowActivityReference> legacyActivities)
    {
        ArgumentNullException.ThrowIfNull(legacyActivities);
        return ProjectCore(xaml, legacyActivities);
    }

    /// <summary>回傳身份比對布林值，不回傳或記錄 XAML 中的型別、組件、流程識別或業務內容。</summary>
    internal static WorkflowActivityIdentityMatch CompareLegacyActivityIdentity(
        string xaml,
        IReadOnlyList<WorkflowActivityReference> legacyActivities)
    {
        ArgumentNullException.ThrowIfNull(legacyActivities);
        var references = WorkflowRepointer.ReadReferences(xaml);
        if (references.Count == 0)
            return default;

        var typeMatched = references.Any(reference => legacyActivities.Any(old =>
            string.Equals(reference.Type, old.Type, StringComparison.Ordinal)));
        var assemblyMatched = references.Any(reference => legacyActivities.Any(old =>
            string.Equals(reference.Assembly, old.Assembly, StringComparison.OrdinalIgnoreCase)));
        var reversedMatched = references.Any(reference => legacyActivities.Any(old =>
            string.Equals(reference.Type, old.Assembly, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(reference.Assembly, old.Type, StringComparison.Ordinal)));
        var registeredMatch = references.Any(reference => legacyActivities.Any(old =>
            string.Equals(reference.Type, old.Type, StringComparison.Ordinal) &&
            string.Equals(reference.Assembly, old.Assembly, StringComparison.OrdinalIgnoreCase)));
        return new(true, typeMatched, assemblyMatched, reversedMatched, registeredMatch);
    }

    /// <summary>共用有界解析路徑；未提供catalog時要求唯一參照，提供時另驗其舊活動身份。</summary>
    private static string? ProjectCore(
        string xaml,
        IReadOnlyList<WorkflowActivityReference>? legacyActivities)
    {
        if (string.IsNullOrEmpty(xaml) || xaml.Length > MaximumInputCharacters)
            throw Invalid();

        try
        {
            var document = ReadDocument(xaml);
            var elements = document.Descendants().Prepend(document.Root ?? throw Invalid()).ToArray();
            if (elements.Length > MaximumElements || elements.Any(element => Depth(element) > MaximumDepth))
                throw Invalid();

            var references = elements.Where(element => element.Name.LocalName == "ActivityReference").ToArray();
            XElement target;
            if (legacyActivities is null)
            {
                if (references.Length != 1)
                    throw Invalid();
                target = references[0];
            }
            else
            {
                var registered = references.Where(reference => IsRegisteredLegacyActivity(reference, legacyActivities)).ToArray();
                if (registered.Length != 1)
                    return null;
                target = registered[0];
            }

            var projected = ClonePath(document.Root!, target);
            return projected.ToString(SaveOptions.DisableFormatting);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or ArgumentException)
        {
            throw Invalid();
        }
    }

    /// <summary>以固定 XML 限制解析單份文件；reader 僅在同步呼叫內持有並釋放。</summary>
    private static XDocument ReadDocument(string xaml)
    {
        if (string.IsNullOrEmpty(xaml) || xaml.Length > MaximumInputCharacters)
            throw Invalid();
        using var input = new StringReader(xaml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumInputCharacters,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        if (document.Root is null)
            throw Invalid();
        return document;
    }

    /// <summary>
    /// 複製元素名稱及必要 namespace／型別／參數鍵；最近的參數邊界為 InArgument 時，
    /// 其文字與巢狀 Value 屬性才保留輸入繫結種類。OutArgument 及其他文字一律脫敏。
    /// </summary>
    private static XElement CloneSanitized(XElement source)
    {
        var clone = CloneHeader(source);
        foreach (var node in source.Nodes())
        {
            if (node is XElement child)
            {
                clone.Add(CloneSanitized(child));
            }
            else if (node is XText text && !string.IsNullOrWhiteSpace(text.Value))
            {
                var argument = source.AncestorsAndSelf().FirstOrDefault(element =>
                    element.Name.LocalName is "InArgument" or "OutArgument");
                clone.Add(argument?.Name.LocalName == "InArgument"
                    ? IsRecordFieldBinding(text.Value) ? RecordFieldMarker : FixedValueMarker
                    : "[REDACTED]");
            }
        }

        return clone;
    }

    /// <summary>保留文件根至目標活動的唯一路徑，丟棄其餘流程節點以免保留舊執行內容。</summary>
    private static XElement ClonePath(XElement source, XElement target)
    {
        if (ReferenceEquals(source, target))
            return CloneSanitized(source);

        var clone = CloneHeader(source);
        var next = source.Elements().SingleOrDefault(child =>
            ReferenceEquals(child, target) || target.Ancestors().Any(ancestor => ReferenceEquals(ancestor, child)));
        if (next is null)
            throw Invalid();
        clone.Add(ClonePath(next, target));
        return clone;
    }

    /// <summary>只以完整型別及組件簡名比對舊 catalog；其他參照只保留為不輸出的背景節點。</summary>
    private static bool IsRegisteredLegacyActivity(
        XElement reference,
        IReadOnlyList<WorkflowActivityReference> legacyActivities)
    {
        var identity = reference.Attributes().SingleOrDefault(attribute =>
            attribute.Name.LocalName == "AssemblyQualifiedName")?.Value;
        var parts = identity?.Split(',');
        return parts is { Length: >= 2 } &&
            legacyActivities.Any(old =>
                string.Equals(parts[0].Trim(), old.Type, StringComparison.Ordinal) &&
                string.Equals(parts[1].Trim(), old.Assembly, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>複製節點名稱及安全屬性；未知屬性值一律脫敏。</summary>
    private static XElement CloneHeader(XElement source)
    {
        var clone = new XElement(source.Name);
        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                clone.Add(new XAttribute(attribute.Name, attribute.Value));
            else if (attribute.Name.LocalName is "TypeArguments" or "Key")
                clone.Add(new XAttribute(attribute.Name, attribute.Value));
            else if (attribute.Name.LocalName == "AssemblyQualifiedName")
                clone.Add(new XAttribute(attribute.Name,
                    "[ACTIVITY_TYPE], [ASSEMBLY], Version=[VERSION], Culture=[CULTURE], PublicKeyToken=[TOKEN]"));
            else if (attribute.Name.LocalName == "Value" &&
                source.AncestorsAndSelf().FirstOrDefault(element =>
                    element.Name.LocalName is "InArgument" or "OutArgument")?.Name.LocalName == "InArgument")
                clone.Add(new XAttribute(attribute.Name,
                    IsRecordFieldBinding(attribute.Value) ? RecordFieldMarker : FixedValueMarker));
            else
                clone.Add(new XAttribute(attribute.Name, "[REDACTED]"));
        }

        return clone;
    }

    /// <summary>辨識傳統方括號記錄欄位 expression，只保留其種類、不保留實體或欄位名稱。</summary>
    private static bool IsRecordFieldBinding(string value)
    {
        var candidate = value.Trim();
        return candidate.Length >= 3 && candidate[0] == '[' && candidate[^1] == ']';
    }

    /// <summary>以父節點數計算深度，避免遞迴處理不受限的 XAML 樹。</summary>
    private static int Depth(XElement element)
    {
        var depth = 0;
        for (var current = element; current is not null; current = current.Parent)
        {
            if (++depth > MaximumDepth)
                return depth;
        }

        return depth;
    }

    /// <summary>固定錯誤不包含來源 XML、名稱、業務值或 XML parser 的原始訊息。</summary>
    private static InvalidDataException Invalid() =>
        new("Workflow activity shape could not be projected safely.");
}
