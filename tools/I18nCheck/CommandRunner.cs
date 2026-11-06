using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace I18nCheck;

/// <summary>
/// 協調單一離線命令；來源只在命令區域持有，輸出只有計數、路徑及固定錯誤代碼。
/// 不建構服務 client、不載入產品設定，不使用背景佇列、使用者快取或程序文化狀態。
/// </summary>
internal static class CommandRunner
{
    /// <summary>固定基準位置，與 verify／CI 使用同一份已審核計數。</summary>
    private const string DefaultBaseline = "tools/I18nCheck/baselines/cjk-literals.json";

    /// <summary>只含固定格式的共用序列化設定，首次使用後由 System.Text.Json 凍結，無使用者資料。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>執行且封閉處理可預期的輸入錯誤，不將任何來源文字或資料值寫入診斷。</summary>
    /// <param name="root">明確選定的方案根，不在缺檔時向父 worktree 尋找。</param>
    /// <param name="args">scan、generate、verify 及其選項。</param>
    /// <param name="output">呼叫者擁有的輸出；本工具不保留也不釋放。</param>
    /// <returns>成功為零，棘輪／產生檔差異為一，輸入／檔案驗證錯誤為二。</returns>
    internal static int Run(string root, string[] args, TextWriter output)
    {
        try
        {
            if (args.Length == 0) throw new InvalidDataException("I18N401: command is required.");
            root = Path.GetFullPath(root);
            return args[0] switch
            {
                "scan" => Scan(root, Parse(args, ["--baseline", "--write-baseline"]), output),
                "generate" => Generate(root, Parse(args, ["--resx", "--namespace", "--class", "--out", "--check"]), output),
                "generate-timezones" => GenerateTimeZones(root, Parse(args, ["--cldr", "--cldr-version", "--out", "--check"]), output),
                "verify" when args.Length == 1 => Verify(root, output),
                _ => throw new InvalidDataException("I18N402: command or options are invalid.")
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or XmlException or JsonException)
        {
            // 這些是檔案／命令的預期拒絕，不發外部告警；不輸出含來源值的 Exception.Message。
            output.WriteLine("I18nCheck rejected input: " + exception.GetType().Name);
            return 2;
        }
    }

    /// <summary>嚴格解析本命令允許的選項，拒絕拼錯、重複或缺值，不默默忽略輸入。</summary>
    /// <param name="args">原命令列。</param>
    /// <param name="allowed">此指令的固定白名單。</param>
    /// <returns>命令區域的選項字典；check 是唯一無值旗標。</returns>
    private static Dictionary<string, string> Parse(string[] args, string[] allowed)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var name = args[index];
            if (!allowed.Contains(name, StringComparer.Ordinal) || options.ContainsKey(name)) throw new InvalidDataException("I18N403: unknown or repeated option.");
            if (name == "--check") { options.Add(name, string.Empty); continue; }
            if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw new InvalidDataException("I18N404: option value is missing.");
            options.Add(name, args[index]);
        }
        return options;
    }

    /// <summary>完整掃描後才寫基準或比較；每個專案獨立判定，不使用全方案總數相抵。</summary>
    /// <param name="root">本次根目錄。</param>
    /// <param name="options">已驗證的選項。</param>
    /// <param name="output">只接收中繼資料的輸出。</param>
    /// <returns>基準建立或棘輪判定。</returns>
    private static int Scan(string root, Dictionary<string, string> options, TextWriter output)
    {
        if (options.ContainsKey("--baseline") && options.ContainsKey("--write-baseline")) throw new InvalidDataException("I18N405: baseline modes are mutually exclusive.");
        var snapshot = RepositoryScanner.Scan(root);
        Validate(snapshot);
        foreach (var project in snapshot.Projects)
        {
            output.WriteLine($"PROJECT {project.Project}: {project.Count}");
            foreach (var file in project.Files) output.WriteLine($"  FILE {file.Path}: {file.Count}");
        }
        if (options.TryGetValue("--write-baseline", out var destination))
        {
            var path = LocalFiles.Resolve(root, destination);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("I18N406: baseline output must be JSON.");
            var json = JsonSerializer.Serialize(snapshot, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
            LocalFiles.WriteAtomic(path, new UTF8Encoding(false, true).GetBytes(json));
            return 0;
        }
        var baselinePath = LocalFiles.Resolve(root, options.GetValueOrDefault("--baseline", DefaultBaseline));
        LocalFiles.CheckFile(baselinePath);
        var baseline = JsonSerializer.Deserialize<ScanSnapshot>(File.ReadAllBytes(baselinePath), JsonOptions) ?? throw new InvalidDataException("I18N407: baseline is empty.");
        Validate(baseline);
        var result = CjkRatchet.Compare(snapshot.Projects.ToDictionary(project => project.Project, project => project.Count, StringComparer.OrdinalIgnoreCase), baseline.Projects.ToDictionary(project => project.Project, project => project.Count, StringComparer.OrdinalIgnoreCase));
        foreach (var project in result.Improvements) output.WriteLine("IMPROVEMENT " + project);
        output.WriteLine(result.Passed ? "CJK ratchet: passed" : "CJK ratchet: count increased");
        return result.Passed ? 0 : 1;
    }

    /// <summary>拒絕重複／缺失身分及不一致總數，避免 JSON 或字典覆寫掩蓋基準衝突。</summary>
    /// <param name="snapshot">完整掃描結果或讀回的基準。</param>
    private static void Validate(ScanSnapshot snapshot)
    {
        if (snapshot.Version != 1 || snapshot.Projects is null || snapshot.Projects.Count == 0) throw new InvalidDataException("I18N408: baseline schema is invalid.");
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in snapshot.Projects)
        {
            if (project is null || string.IsNullOrWhiteSpace(project.Project) || !projects.Add(project.Project) || project.Count < 0 || project.Files is null)
                throw new InvalidDataException("I18N409: baseline project identity is invalid or duplicated.");
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in project.Files)
                if (file is null || string.IsNullOrWhiteSpace(file.Path) || file.Count < 0 || !files.Add(file.Path)) throw new InvalidDataException("I18N410: baseline file identity is invalid or duplicated.");
            if (project.Files.Sum(file => (long)file.Count) != project.Count) throw new InvalidDataException("I18N411: baseline total differs from file counts.");
        }
    }

    /// <summary>可明確產生一個檔，或在 check 模式巡檢方案內全部中性資源；check 永不寫檔。</summary>
    /// <param name="root">選定根目錄。</param>
    /// <param name="options">已驗證的產生器選項。</param>
    /// <param name="output">只輸出檔名與判定。</param>
    /// <returns>所有產生檔均一致時為零，否則一。</returns>
    private static int Generate(string root, Dictionary<string, string> options, TextWriter output)
    {
        if (options.Count == 1 && options.ContainsKey("--check")) return CheckAllGenerated(root, output);
        foreach (var name in new[] { "--resx", "--namespace", "--class", "--out" })
            if (!options.ContainsKey(name)) throw new InvalidDataException("I18N412: explicit generation requires resx, namespace, class and out.");
        var source = LocalFiles.Resolve(root, options["--resx"]);
        var destination = LocalFiles.Resolve(root, options["--out"]);
        if (!source.EndsWith(".resx", StringComparison.OrdinalIgnoreCase) || !destination.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("I18N413: generation file extensions are invalid.");
        var bytes = ResourceGenerator.Generate(ResourceCatalog.Load(source), options["--namespace"], options["--class"]);
        if (options.ContainsKey("--check")) return CompareGenerated(destination, bytes, output);
        LocalFiles.WriteAtomic(destination, bytes);
        output.WriteLine("GENERATED " + RepositoryScanner.Relative(root, destination));
        return 0;
    }

    /// <summary>只檢查中性 resx；語系檔由 ResourceCatalog 一次驗證，不重複當成新來源。</summary>
    /// <param name="root">固定方案邊界。</param>
    /// <param name="output">判定輸出。</param>
    /// <param name="resourceSets">verify已建立的同世代完整候選；獨立generate檢查時才自行載入。</param>
    /// <returns>任一輸出缺失或差異時為一。</returns>
    private static int CheckAllGenerated(string root, TextWriter output, IReadOnlyList<ResourceSet>? resourceSets = null)
    {
        var passed = true;
        var count = 0;
        foreach (var resource in resourceSets ?? ResourceSet.LoadRepository(root))
        {
            var source = resource.Path;
            count++;
            var destination = LocalFiles.Resolve(root, Path.ChangeExtension(source, ".g.cs"));
            var catalog = resource.Catalog;
            if (!File.Exists(destination)) { output.WriteLine("GENERATED MISSING " + RepositoryScanner.Relative(root, destination)); passed = false; continue; }
            var syntax = CSharpSyntaxTree.ParseText(LocalFiles.ReadSource(destination)).GetRoot();
            var namespaceName = syntax.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().SingleOrDefault()?.Name.ToString();
            var className = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().SingleOrDefault()?.Identifier.ValueText;
            if (namespaceName is null || className is null) throw new InvalidDataException("I18N414: generated file namespace or class is missing.");
            if (CompareGenerated(destination, ResourceGenerator.Generate(catalog, namespaceName, className), output) != 0) passed = false;
        }
        output.WriteLine($"Generated resource check: files={count}, passed={passed}");
        return passed ? 0 : 1;
    }

    /// <summary>逐位元組比較候選，不修正差異，也不在診斷印出資源文字。</summary>
    /// <param name="destination">生成檔路徑。</param>
    /// <param name="expected">本次重新產生的候選。</param>
    /// <param name="output">只記錄一致或不同。</param>
    /// <returns>相等零，缺檔或不同一。</returns>
    private static int CompareGenerated(string destination, byte[] expected, TextWriter output)
    {
        if (File.Exists(destination))
        {
            LocalFiles.CheckFile(destination);
            if (File.ReadAllBytes(destination).AsSpan().SequenceEqual(expected)) return 0;
        }
        output.WriteLine("Generated resource check: missing or stale output");
        return 1;
    }

    /// <summary>從明確 CLDR 路徑產生固定時區表；check 只比較，未提供 out 時使用正式 Localization 位置。</summary>
    /// <param name="root">固定本次方案邊界，不向父 worktree 尋檔。</param>
    /// <param name="options">只接受 CLDR、版本、輸出及唯讀 check。</param>
    /// <param name="output">只輸出生成路徑或固定判定，不輸出原始 XML／例外本文。</param>
    /// <returns>一致零、過期一、預期拒絕由外層轉二。</returns>
    private static int GenerateTimeZones(string root, Dictionary<string, string> options, TextWriter output)
    {
        if (!options.TryGetValue("--cldr", out var source)) throw new InvalidDataException("I18N511: CLDR source is required.");
        var destination = LocalFiles.Resolve(root, options.GetValueOrDefault("--out", "SpeechMessage.Localization/Time/IanaWindowsZones.g.cs"));
        if (!source.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || !destination.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("I18N512: time zone file extensions are invalid.");
        var bytes = TimeZoneGenerator.Generate(LocalFiles.Resolve(root, source), options.GetValueOrDefault("--cldr-version", TimeZoneGenerator.PinnedCldrVersion));
        if (options.ContainsKey("--check")) return CompareGenerated(destination, bytes, output);
        LocalFiles.WriteAtomic(destination, bytes);
        output.WriteLine("GENERATED " + RepositoryScanner.Relative(root, destination));
        return 0;
    }

    /// <summary>每包及 CI 的一致入口，固定執行專案棘輪、resx 契約與產生檔比較。</summary>
    /// <param name="root">本次根。</param>
    /// <param name="output">呼叫者輸出。</param>
    /// <returns>所有檢查通過才回傳零。</returns>
    private static int Verify(string root, TextWriter output)
    {
        var scan = Scan(root, new Dictionary<string, string>(StringComparer.Ordinal), output);
        // 生成檔與完整規則共用同一份完整候選，不在兩次走訪之間混入不同世代資源。
        var resourceSets = ResourceSet.LoadRepository(root);
        var generated = CheckAllGenerated(root, output, resourceSets);
        var resources = ResourceVerifier.Verify(root, resourceSets, output);
        // 正式 Localization 存在時來源也必須存在；不能以刪掉 XML 跳過驗證。
        // 沒有 Localization 的既有最小 scanner repo 不需憑空建立時區來源。
        var timeZones = 0;
        if (File.Exists(LocalFiles.Resolve(root, "SpeechMessage.Localization/SpeechMessage.Localization.csproj")) ||
            File.Exists(LocalFiles.Resolve(root, "tools/I18nCheck/data/windowsZones.xml")))
            timeZones = GenerateTimeZones(root, new Dictionary<string, string>(StringComparer.Ordinal)
            { ["--cldr"] = "tools/I18nCheck/data/windowsZones.xml", ["--check"] = string.Empty }, output);
        return scan == 0 && generated == 0 && resources == 0 && timeZones == 0 ? 0 : 1;
    }
}
