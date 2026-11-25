/*
檔案整體說明
目的與定位：P5-03b 畫面元件離線封裝器，為 Deployment CLI 的 UI 匯出／匯入流程提供有界 ZIP 候選驗證與 D26 表單篩選。
主要功能與流程：以記憶體建立單次候選，嚴格驗證 ZIP 成員路徑、大小、重複身分、方案唯一名稱與未受管理狀態；解析 customizations.xml 時移除 account／contact 的 FormXml，但保留其檢視與其他非表單元件，再提供安全重壓縮。
信任邊界與資源擁有權：輸入 ZIP、XML 與每個 byte array 只屬本次 operation，不跨命令快取、不保存使用者／組織／憑證資料。所有 stream、ZipArchive、XmlReader 由本方法同步擁有並在返回前釋放；候選完成前不發布任何部分輸出，大小超限、越界、重複、DTD 或格式錯誤皆 fail closed。
適用範圍與驗證：本檔只處理離線 `SpeechMessageUi` 解決方案原始 ZIP，不宣稱已連線匯出、匯入或發布；UiSolutionArchiveTests 以合成 XML、惡意路徑、壓縮資料及真 ZIP 往返驗證。
*/
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>UI 匯入後要核對的實體與正式元件身份；只保存 detached 純值，不持有 ZIP 或 SDK。</summary>
internal sealed record UiArchivePublishScope(
    string SolutionName,
    IReadOnlyList<string> EntityNames,
    IReadOnlyList<Guid> ComponentIds);

/// <summary>封裝離線 UI 解決方案的有限候選；所有公開操作均建立新的 operation-local byte 集合。</summary>
internal static class UiSolutionArchive
{
    private const long MaxEntryBytes = 8 * 1024 * 1024;
    private const long MaxArchiveBytes = 16 * 1024 * 1024;
    private const string LegacySolutionName = "SpeechMessageUi";
    private static readonly string[] AllowedSolutionNames =
        { "SpeechMessageUi", "SpeechMessagePlatformUi", "GoodShepherd2Ui", "GoodButlerUi", "GoodGuildUi" };

    /// <summary>
    /// 讀取並驗證未受管理 UI 解決方案，並依 D26 移除共用 account／contact 的表單。
    /// 任何成員失敗都不回傳部分候選；例外只保留固定分類，不把來源 XML 或路徑內容帶出。
    /// </summary>
    /// <param name="archive">本次命令擁有的完整 ZIP bytes。</param>
    /// <returns>可供匯入或再次封裝的完整成員快照。</returns>
    internal static IReadOnlyDictionary<string, byte[]> Prepare(byte[] archive)
        => Prepare(archive, LegacySolutionName);

    /// <summary>
    /// 讀取白名單中的指定未受管理 UI 解決方案；方案名稱不由 ZIP 外部輸入自行擴權。
    /// </summary>
    /// <param name="archive">本次命令擁有的完整 ZIP bytes。</param>
    /// <param name="solutionName">呼叫端已核定的方案名稱。</param>
    /// <returns>完整且已套用 D26 篩選的 detached 成員。</returns>
    internal static IReadOnlyDictionary<string, byte[]> Prepare(byte[] archive, string solutionName)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (solutionName is null || !AllowedSolutionNames.Contains(solutionName, StringComparer.Ordinal))
            throw InvalidArchive();
        if (archive.LongLength is <= 0 or > MaxArchiveBytes)
            throw InvalidArchive();

        var candidate = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var input = new MemoryStream(archive, writable: false);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in zip.Entries)
            {
                var name = ValidateName(entry.FullName);
                if (!candidate.TryAdd(name, ReadEntry(entry)))
                    throw InvalidArchive();
            }

            if (!candidate.TryGetValue("solution.xml", out var solution) ||
                !candidate.TryGetValue("customizations.xml", out var customizations))
                throw InvalidArchive();
            ValidateSolution(solution, solutionName);
            candidate["solution.xml"] = FilterSharedFormRoots(solution, customizations);
            candidate["customizations.xml"] = FilterSharedForms(customizations);
            return candidate;
        }
        catch (InvalidDataException)
        {
            throw InvalidArchive();
        }
        catch (XmlException)
        {
            throw InvalidArchive();
        }
    }

    /// <summary>判斷方案名稱是否屬於本工具固定白名單，不把 caller 輸入當成 SDK 授權。</summary>
    internal static bool IsAllowedSolutionName(string solutionName) =>
        solutionName is not null && AllowedSolutionNames.Contains(solutionName, StringComparer.Ordinal);

    /// <summary>
    /// 從已通過 Prepare 的 detached ZIP 成員建立 UI 發布範圍。
    /// 只保留 solution.xml 的表單／檢視根元件 GUID，以及 customizations.xml 的實體名稱；
    /// 未列出的元件不能在匯入後被發布。
    /// </summary>
    /// <param name="files">已通過 Prepare 的完整 ZIP 成員快照。</param>
    /// <returns>本次方案的固定發布範圍。</returns>
    internal static UiArchivePublishScope ReadPublishScope(IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!files.TryGetValue("solution.xml", out var solutionBytes) ||
            !files.TryGetValue("customizations.xml", out var customizationsBytes))
            throw InvalidArchive();

        var solution = ParseXml(solutionBytes);
        var customizations = ParseXml(customizationsBytes);
        var solutionName = solution.Descendants("SolutionManifest").SingleOrDefault()?.Element("UniqueName")?.Value;
        if (solutionName is null || !IsAllowedSolutionName(solutionName))
            throw InvalidArchive();

        var entities = customizations.Descendants("Entity")
            .Select(entity => entity.Element("Name")?.Value.Trim() ?? string.Empty)
            .Where(name => name.Length != 0)
            .ToArray();
        if (entities.Length == 0 || entities.Distinct(StringComparer.Ordinal).Count() != entities.Length)
            throw InvalidArchive();

        var componentIds = new List<Guid>();
        foreach (var root in solution.Descendants("RootComponent")
                     .Where(root => (string?)root.Attribute("type") is "26" or "60"))
        {
            if (!Guid.TryParse((string?)root.Attribute("id"), out var id) || id == Guid.Empty ||
                componentIds.Contains(id))
                throw InvalidArchive();
            componentIds.Add(id);
        }

        return new UiArchivePublishScope(
            solutionName,
            Array.AsReadOnly(entities),
            Array.AsReadOnly(componentIds.ToArray()));
    }

    /// <summary>以固定 ZIP 順序重建完整候選；輸出 stream 由本次呼叫唯一擁有並同步關閉。</summary>
    /// <param name="files">已通過 Prepare 的 detached 成員。</param>
    /// <returns>新的完整 ZIP bytes。</returns>
    internal static byte[] Pack(IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var name = ValidateName(item.Key);
                if (item.Value is null || item.Value.LongLength > MaxEntryBytes)
                    throw InvalidArchive();
                using var output = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                output.Write(item.Value);
            }
        }
        if (stream.Length > MaxArchiveBytes)
            throw InvalidArchive();
        return stream.ToArray();
    }

    /// <summary>拒絕路徑穿越、絕對路徑、反斜線、保留名稱及大小寫重複的候選成員。</summary>
    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\', StringComparison.Ordinal) || name.StartsWith('/') ||
            Path.IsPathRooted(name) || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Split('/').Any(part => part is "" or "." or ".." || IsReserved(part)))
            throw InvalidArchive();
        return name;
    }

    /// <summary>讀取成員時同時限制解壓後大小，避免壓縮膨脹留下無界記憶體。</summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length < 0 || entry.Length > MaxEntryBytes) throw InvalidArchive();
        using var source = entry.Open();
        using var target = new MemoryStream((int)entry.Length);
        source.CopyTo(target, 81920);
        if (target.Length > MaxEntryBytes) throw InvalidArchive();
        return target.ToArray();
    }

    /// <summary>依 XML 安全設定解析固定白名單方案，且 Managed 必須為零。</summary>
    private static void ValidateSolution(byte[] bytes, string solutionName)
    {
        var document = ParseXml(bytes);
        var manifest = document.Descendants("SolutionManifest").SingleOrDefault()
            ?? throw InvalidArchive();
        if (!string.Equals(manifest.Element("UniqueName")?.Value, solutionName, StringComparison.Ordinal) ||
            !string.Equals(manifest.Element("Managed")?.Value, "0", StringComparison.Ordinal))
            throw InvalidArchive();
    }

    /// <summary>保留共用實體非表單元件，只移除 D26 禁止打包的 FormXml 節點。</summary>
    private static byte[] FilterSharedForms(byte[] bytes)
    {
        var document = ParseXml(bytes);
        var removedFormIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in document.Descendants("Entity"))
        {
            var name = entity.Element("Name")?.Value;
            if (name is "account" or "contact")
            {
                var form = entity.Element("FormXml");
                var formId = form?.Descendants("formid").SingleOrDefault()?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(formId))
                    removedFormIds.Add(formId);
                form?.Remove();
            }
        }

        if (removedFormIds.Count != 0)
        {
            var solution = document.Descendants("RootComponent")
                .Where(component => string.Equals((string?)component.Attribute("type"), "60", StringComparison.Ordinal))
                .Where(component => removedFormIds.Contains((string?)component.Attribute("id") ?? string.Empty))
                .ToArray();
            foreach (var component in solution)
                component.Remove();
        }
        using var writer = new MemoryStream();
        using (var text = XmlWriter.Create(writer, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = true,
            NewLineChars = "\r\n",
            Indent = false
        }))
        {
            document.Save(text);
        }
        return writer.ToArray();
    }

    /// <summary>同步移除共用表單的 type 60 根參照，避免只刪 XML 而留下不可匯入的孤立元件。</summary>
    private static byte[] FilterSharedFormRoots(byte[] solutionBytes, byte[] customizationsBytes)
    {
        var customizations = ParseXml(customizationsBytes);
        var removedFormIds = customizations.Descendants("Entity")
            .Where(entity => entity.Element("Name")?.Value is "account" or "contact")
            .Select(entity => entity.Element("FormXml")?.Descendants("formid").SingleOrDefault()?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removedFormIds.Count == 0)
            return solutionBytes;

        var solution = ParseXml(solutionBytes);
        foreach (var component in solution.Descendants("RootComponent")
            .Where(component => string.Equals((string?)component.Attribute("type"), "1", StringComparison.Ordinal))
            .Where(component => (string?)component.Attribute("schemaName") is "account" or "contact"))
            component.SetAttributeValue("behavior", "2");
        foreach (var component in solution.Descendants("RootComponent")
            .Where(component => string.Equals((string?)component.Attribute("type"), "60", StringComparison.Ordinal))
            .Where(component => removedFormIds.Contains((string?)component.Attribute("id") ?? string.Empty))
            .ToArray())
            component.Remove();
        return Serialize(solution);
    }

    /// <summary>以無 BOM、固定換行輸出已驗證的 XML 候選，不發布檔案系統副本。</summary>
    private static byte[] Serialize(XDocument document)
    {
        using var writer = new MemoryStream();
        using (var text = XmlWriter.Create(writer, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = true,
            NewLineChars = "\r\n",
            Indent = false
        }))
        {
            document.Save(text);
        }
        return writer.ToArray();
    }

    /// <summary>禁止 DTD、外部解析及保留外部資源的 XML 讀取器；解析結果不與輸入 stream 共用。</summary>
    private static XDocument ParseXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxEntryBytes,
            MaxCharactersFromEntities = 0
        });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>判斷 Windows 封裝成員中的保留裝置名稱，不依輸入資料建立檔案。</summary>
    private static bool IsReserved(string part)
    {
        var stem = part.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 2 && char.IsLetter(stem[0]) && stem[1] == ':');
    }

    /// <summary>固定公開格式錯誤，避免把原始成員名稱、XML 或外部路徑放入例外。</summary>
    private static InvalidDataException InvalidArchive() =>
        new("The UI solution archive is invalid.");
}
