/*
檔案整體說明
目的與定位：P5-03b 畫面元件離線封裝器，為 Deployment CLI 的 UI 匯出／匯入流程提供有界 ZIP 候選驗證與 D26 表單篩選。
主要功能與流程：以記憶體建立單次候選，嚴格驗證 ZIP 成員路徑、大小、重複身分、方案唯一名稱、未受管理狀態與 RootComponent UI 元件型別白名單；只接受 Entity 外殼（最終匯入內容必須 behavior=2）、檢視、表單及 Web Resource，未知元件與非 shell Entity 根元件在 ImportSolution 前拒絕。最多 128 成員、每成員 8MiB、累計解壓及 ZIP 輸出各 16MiB。解壓前先驗累計宣告長度，再以固定陣列核對實際內容；封裝先驗整體輸入，每次寫入含 ZIP 標頭與中央目錄都受硬上限約束。解析 customizations.xml 時移除 account／contact 的 FormXml，但保留其檢視與其他非表單元件；XML 輸出亦受單檔上限與最終累計預算約束。
信任邊界與資源擁有權：輸入 ZIP、XML 與每個 byte array 只屬本次 operation，不跨命令快取、不保存使用者／組織／憑證資料。所有 stream、ZipArchive、XmlReader 由本方法同步擁有並在返回前釋放；候選完成前不發布任何部分輸出，大小超限、越界、重複、DTD 或格式錯誤皆 fail closed。
適用範圍與驗證：本檔只處理固定五項白名單的離線未受管理 UI 解決方案，不宣稱已連線匯出、匯入或發布；UiSolutionArchiveTests 以合成 XML、惡意路徑、多成員壓縮資料、配置量及真 ZIP 往返驗證，不把資源預算誤稱整個程序的記憶體上限。
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
    /// <summary>匯入在配置前與封裝在每次寫入前共用的 ZIP 位元組上限；不代表 XML 物件圖或程序總記憶體。</summary>
    internal const long MaxArchiveBytes = 16 * 1024 * 1024;
    private const int MaxEntries = 128;
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
    /// 解壓前核對 128 成員、個別 8MiB 及合計 16MiB；實際讀取不允許超過宣告長度，
    /// D26 篩選後再驗證整體候選，任一失敗均釋放所有本次 reader 而不回傳部分結果。
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
            if (zip.Entries.Count is < 1 or > MaxEntries)
                throw InvalidArchive();
            long totalBytes = 0;
            foreach (var entry in zip.Entries)
                AddEntrySize(entry.Length, ref totalBytes);
            foreach (var entry in zip.Entries)
            {
                var name = ValidateName(entry.FullName);
                if (candidate.ContainsKey(name))
                    throw InvalidArchive();
                candidate.Add(name, ReadEntry(entry));
            }

            if (!candidate.TryGetValue("solution.xml", out var solution) ||
                !candidate.TryGetValue("customizations.xml", out var customizations))
                throw InvalidArchive();
            ValidateSolution(solution, solutionName);
            candidate["solution.xml"] = FilterSharedFormRoots(solution, customizations);
            candidate["customizations.xml"] = FilterSharedForms(customizations);
            ValidateFiles(candidate);
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
        catch (EndOfStreamException)
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

    /// <summary>
    /// 先驗證所有成員及累計原文，再以固定 ZIP 順序重建完整候選。
    /// 本次呼叫唯一擁有輸出 stream；包含關閉 ZIP 時的中央目錄也須於寫入前符合 16MiB，
    /// 超額不擴大緩衝區或回傳部分輸出，成功時才複製完整 bytes 交給呼叫端。
    /// </summary>
    /// <param name="files">已通過 Prepare 的 detached 成員。</param>
    /// <returns>新的完整 ZIP bytes。</returns>
    internal static byte[] Pack(IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        ValidateFiles(files);
        using var stream = new BoundedMemoryStream(MaxArchiveBytes);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var name = ValidateName(item.Key);
                using var output = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                output.Write(item.Value);
            }
        }
        return stream.ToArray();
    }

    /// <summary>在建立輸出之前驗證候選數量、路徑身分及整體原文；大小寫相同的身分仍是衝突，不合併或覆蓋。</summary>
    private static void ValidateFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        if (files.Count is < 1 or > MaxEntries)
            throw InvalidArchive();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var item in files)
        {
            if (!names.Add(ValidateName(item.Key)) || item.Value is null)
                throw InvalidArchive();
            AddEntrySize(item.Value.LongLength, ref totalBytes);
        }
    }

    /// <summary>以扣除剩餘額度後再比較的方式驗證單檔與累計預算；不允許整數溢位或以壓縮大小替代原文大小。</summary>
    private static void AddEntrySize(long length, ref long totalBytes)
    {
        if (length < 0 || length > MaxEntryBytes || length > MaxArchiveBytes - totalBytes)
            throw InvalidArchive();
        totalBytes += length;
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

    /// <summary>
    /// 只配置已通過累計預算的宣告長度，讀滿後多探測一個位元組；截斷或額外內容皆拒絕。
    /// 不以 CopyTo 自動擴容，來源 stream 由本方法唯一擁有，成功或失敗均在返回前關閉。
    /// </summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length < 0 || entry.Length > MaxEntryBytes) throw InvalidArchive();
        using var source = entry.Open();
        var bytes = new byte[(int)entry.Length];
        source.ReadExactly(bytes);
        if (source.ReadByte() != -1) throw InvalidArchive();
        return bytes;
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
        ValidateRootComponents(document, manifest, requireEntityShellOnly: false);
    }

    /// <summary>
    /// 匯入會把整個 ZIP 交給 Dataverse，不能只核對稍後用於發布的表單／檢視範圍。
    /// 僅接受本工具 UI 方案契約中的 Entity (1)、Saved Query (26)、System Form (60)
    /// 與 Web Resource (61)；其餘元件型別可能擴大到 Plugin、流程或其他未審部署面，故在 SDK 前拒絕。
    /// 初次檢查先限制型別及穩定 schema name／GUID 身分；D26 篩選後再次要求 Entity behavior=2
    /// （shell only），避免完整或僅根元件行為把未審 schema 一併匯入。候選只存於本次同步呼叫。
    /// </summary>
    private static void ValidateRootComponents(
        XDocument document,
        XElement manifest,
        bool requireEntityShellOnly)
    {
        var containers = manifest.Elements("RootComponents").ToArray();
        if (containers.Length > 1)
            throw InvalidArchive();

        var container = containers.SingleOrDefault();
        var roots = container?.Elements("RootComponent").ToArray() ?? Array.Empty<XElement>();
        var allRoots = document.Descendants("RootComponent").ToArray();
        if (allRoots.Length != roots.Length ||
            (container is not null && container.Elements().Any(element => element.Name != "RootComponent")) ||
            roots.Any(root => !ReferenceEquals(root.Parent, container)))
            throw InvalidArchive();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var type = (string?)root.Attribute("type");
            string identity;
            if (type == "1")
            {
                var schemaName = (string?)root.Attribute("schemaName");
                if (string.IsNullOrWhiteSpace(schemaName) ||
                    (requireEntityShellOnly && !string.Equals(
                        (string?)root.Attribute("behavior"),
                        "2",
                        StringComparison.Ordinal)))
                    throw InvalidArchive();
                identity = "1:" + schemaName;
            }
            else if (type is "26" or "60" or "61")
            {
                if (!Guid.TryParse((string?)root.Attribute("id"), out var id) || id == Guid.Empty)
                    throw InvalidArchive();
                identity = type + ":" + id.ToString("N");
            }
            else
            {
                throw InvalidArchive();
            }

            if (!identities.Add(identity))
                throw InvalidArchive();
        }
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
        return Serialize(document);
    }

    /// <summary>移除共用表單 type 60 根參照並將必要 Entity root 限為 behavior=2；任何最終非 shell Entity 在 ImportSolution 前拒絕。</summary>
    private static byte[] FilterSharedFormRoots(byte[] solutionBytes, byte[] customizationsBytes)
    {
        var customizations = ParseXml(customizationsBytes);
        var removedFormIds = customizations.Descendants("Entity")
            .Where(entity => entity.Element("Name")?.Value is "account" or "contact")
            .Select(entity => entity.Element("FormXml")?.Descendants("formid").SingleOrDefault()?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var solution = ParseXml(solutionBytes);
        var manifest = solution.Descendants("SolutionManifest").SingleOrDefault()
            ?? throw InvalidArchive();
        if (removedFormIds.Count != 0)
        {
            foreach (var component in solution.Descendants("RootComponent")
                .Where(component => string.Equals((string?)component.Attribute("type"), "1", StringComparison.Ordinal))
                .Where(component => (string?)component.Attribute("schemaName") is "account" or "contact"))
                component.SetAttributeValue("behavior", "2");
            foreach (var component in solution.Descendants("RootComponent")
                .Where(component => string.Equals((string?)component.Attribute("type"), "60", StringComparison.Ordinal))
                .Where(component => removedFormIds.Contains((string?)component.Attribute("id") ?? string.Empty))
                .ToArray())
                component.Remove();
        }

        ValidateRootComponents(solution, manifest, requireEntityShellOnly: true);
        return removedFormIds.Count == 0 ? solutionBytes : Serialize(solution);
    }

    /// <summary>以無 BOM、固定換行及每次寫入前的 8MiB 硬上限輸出 XML；逸出與換行擴張也不能擴大候選預算。</summary>
    private static byte[] Serialize(XDocument document)
    {
        using var writer = new BoundedMemoryStream(MaxEntryBytes);
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

    /// <summary>
    /// 單次封裝或 XML writer 擁有的記憶體串流；在任何寫入或長度擴張前拒絕超額，
    /// 並限制幾何擴容的容量，避免最後一次寫入雖未超額卻先配置兩倍上限。
    /// 不持有外部資源或跨命令狀態，由呼叫端 using 先關閉 ZIP／XML writer，再關閉本串流。
    /// </summary>
    private sealed class BoundedMemoryStream(long maximumBytes) : MemoryStream
    {
        /// <summary>陣列寫入保留原有參數檢查，先核對輸出位置與剩餘容量再交給基底類別。</summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            _ = buffer.AsSpan(offset, count);
            ReserveWrite(count);
            base.Write(buffer, offset, count);
        }

        /// <summary>
        /// span 直接複製至本 owner 緩衝區，避免 MemoryStream 對衍生型別的相容路徑配置額外大陣列。
        /// 先有界擴容與延長，再移動位置；上限拒絕發生於任何內容寫入之前。
        /// </summary>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ReserveWrite(buffer.Length);
            var start = (int)Position;
            var end = start + buffer.Length;
            if (end > Length)
                base.SetLength(end);
            buffer.CopyTo(GetBuffer().AsSpan(start, buffer.Length));
            Position = end;
        }

        /// <summary>ZIP 的個別標頭位元組亦共用相同上限，不能透過 WriteByte 繞過批次寫入限制。</summary>
        public override void WriteByte(byte value)
        {
            ReserveWrite(1);
            base.WriteByte(value);
        }

        /// <summary>禁止以 SetLength 繞過寫入限制；基底的自動擴容前先設定不超過上限的容量。</summary>
        public override void SetLength(long value)
        {
            if (value < 0 || value > maximumBytes)
                throw InvalidArchive();
            ReserveCapacity(value);
            base.SetLength(value);
        }

        /// <summary>以目前位置及寫入量計算所需容量，包含 ZIP 回填標頭而非僅計算附加量。</summary>
        private void ReserveWrite(int count)
        {
            if (Position > maximumBytes - count)
                throw InvalidArchive();
            ReserveCapacity(Position + count);
        }

        /// <summary>小輸出維持幾何擴容效率，接近硬上限時只配置剩餘額度，不先產生超額 backing array。</summary>
        private void ReserveCapacity(long required)
        {
            if (required > Capacity)
                Capacity = (int)Math.Min(maximumBytes, Math.Max(required, Math.Max(256L, 2L * Capacity)));
        }
    }

    /// <summary>固定公開格式錯誤，避免把原始成員名稱、XML 或外部路徑放入例外。</summary>
    private static InvalidDataException InvalidArchive() =>
        new("The UI solution archive is invalid.");
}
