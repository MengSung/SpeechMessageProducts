/*
 * 檔案整體說明
 *
 * 目的與定位：保護共通層 1.3 F8 權限／修改紀錄的離線 schema 與後台存取文件契約，
 * 不以產品程式的常數反推預期值，也不把 JSON 可解析冒充 D365 已部署。
 * 主要功能與流程：先讀本工作樹正式 JSON，以獨立欄位、選項及唯一鍵清單核對四張表；
 * 掃描平台資料夾的完整欄位身份，並反射呼叫已建置 Deployment 的正式分層解析器與
 * CLI 純參數解析器，驗證三產品組合及拒絕單檔覆寫，不呼叫 CLI Main、Run 或 connector。
 * 故障注入與斷言：schema 缺檔須明確失敗；臨時候選先證明可載入，再注入跨檔重複欄位，
 * 必須得到 InvalidDataException，不允許最後一筆覆蓋、顯示名稱去重或部分發布。
 * 使用邊界與隔離：只讀 repo schema、文件及本機組件；不讀設定、User Secrets、業務資料，
 * 不啟動 Host、不連 D365／LINE／VM。反射測試要求同組態 Deployment 事先建置，
 * 缺組件必須失敗而非跳過；組件載入僅在測試程序存活，沒有產品請求或跨租戶狀態。
 * 資源與維護驗證：每份 JsonDocument 由單案 using 釋放，檔案讀取同步關閉；
 * 唯一臨時 repo 只保存兩份不含秘密的 schema，finally 刪除並斷言目錄不存在。
 * 無背景工作、計時器、網路或共享可變集合。文件與位元組測試保護 D17、Group 待補、
 * FS Step 3b 零寫入及 UTF-8 無 BOM／CRLF；離線通過不代表 Active 索引或實機隔離驗收。
 */
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SpeechMessage.Access.Tests;

/// <summary>
/// 以正式檔案與部署器純解析入口保護 F8 metadata 契約；所有預期身份均獨立列出，
/// 不借用實機服務、憑證、產品權限快取或部署測試內部 fixture。
/// </summary>
public sealed class AccessSchemaTests
{
    private static readonly JsonSerializerOptions SchemaJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    /// <summary>
    /// 缺少 F8 檔案、固定身份漂移或四張表的擁有權變更必須失敗；
    /// 此案在新增 schema 前執行，讓 RED 指向缺檔而不是編譯或連線失敗。
    /// </summary>
    [Fact]
    public void Schema_IdentityAndOwnership_MatchF8Contract()
    {
        using var document = ReadSchema();
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(8, root.GetProperty("activePhase").GetInt32());
        Assert.Equal("SpeechMessageCore", root.GetProperty("solution").GetString());
        Assert.Equal("SpeechMessagePublisher", root.GetProperty("publisher").GetString());
        Assert.Equal("new", root.GetProperty("prefix").GetString());
        Assert.Equal(4, root.GetProperty("entities").GetArrayLength());
        foreach (var name in new[] { "new_role", "new_rolepermission", "new_roleassignment", "new_auditentry" })
        {
            var entity = Entity(root, name);
            Assert.True(entity.GetProperty("isNew").GetBoolean());
            Assert.Equal(name == "new_auditentry" ? "OrganizationOwned" : "UserOwned",
                entity.GetProperty("ownershipType").GetString());
            Assert.Equal("new_name", entity.GetProperty("primaryName").GetString());
        }
    }

    /// <summary>
    /// 逐表獨立列出完整欄位與容量，防止產品／牧養欄位、手造 statecode、
    /// 錯誤 lookup 或不相容 phase 混入；文件中的工作包 F8 不等於欄位 phase 8。
    /// </summary>
    /// <param name="name">本次核對的正式實體名稱。</param>
    [Theory]
    [InlineData("new_role")]
    [InlineData("new_rolepermission")]
    [InlineData("new_roleassignment")]
    [InlineData("new_auditentry")]
    public void Schema_ExactFieldsAndLookups_AreProductNeutral(string name)
    {
        using var document = ReadSchema();
        var entity = Entity(document.RootElement, name);
        AssertField(entity, "new_name", "string", length: 200, required: true);
        var expectedCount = name switch
        {
            "new_role" => 4,
            "new_rolepermission" => 5,
            "new_roleassignment" => 3,
            "new_auditentry" => 11,
            _ => throw new InvalidDataException("Unknown test entity.")
        };
        Assert.Equal(expectedCount, entity.GetProperty("fields").GetArrayLength());
        switch (name)
        {
            case "new_role":
                AssertField(entity, "new_tenantid", "lookup", required: true, target: "account");
                AssertField(entity, "new_code", "string", length: 64, required: true);
                AssertField(entity, "new_isadmin", "choice");
                break;
            case "new_rolepermission":
                AssertField(entity, "new_roleid", "lookup", required: true, target: "new_role");
                AssertField(entity, "new_feature", "string", length: 64, required: true);
                AssertField(entity, "new_operation", "choice", required: true);
                AssertField(entity, "new_scope", "choice", required: true);
                break;
            case "new_roleassignment":
                AssertField(entity, "new_contactid", "lookup", required: true, target: "contact");
                AssertField(entity, "new_roleid", "lookup", required: true, target: "new_role");
                break;
            case "new_auditentry":
                AssertField(entity, "new_eventid", "string", length: 36, required: true);
                AssertField(entity, "new_entity", "string", length: 128);
                AssertField(entity, "new_recordid", "string", length: 36);
                AssertField(entity, "new_field", "string", length: 128);
                AssertField(entity, "new_before", "memo", length: 4000);
                AssertField(entity, "new_after", "memo", length: 4000);
                AssertField(entity, "new_actorcontactid", "lookup", target: "contact");
                AssertField(entity, "new_tenantid", "lookup", required: true, target: "account");
                AssertField(entity, "new_correlationid", "string", length: 128);
                var at = AssertField(entity, "new_at", "datetime");
                Assert.Equal("UserLocal", at.GetProperty("behavior").GetString());
                Assert.Equal("DateAndTime", at.GetProperty("format").GetString());
                break;
        }
    }

    /// <summary>
    /// 選項值須與 D365 寫入 literal 完全相符，不能以 enum 序號或顯示文字推算；
    /// 同時比對每個 label，避免讀取與寫入對同一數值解讀不同。
    /// </summary>
    [Fact]
    public void Schema_ChoiceLiterals_PreserveOperationAndScopeMeaning()
    {
        using var document = ReadSchema();
        AssertChoices(Entity(document.RootElement, "new_role"), "new_isadmin",
            (100000000, "否"), (100000001, "是"));
        var permission = Entity(document.RootElement, "new_rolepermission");
        AssertChoices(permission, "new_operation",
            (100000000, "Read"), (100000001, "Create"), (100000002, "Update"), (100000003, "Delete"));
        AssertChoices(permission, "new_scope",
            (100000000, "Self"), (100000001, "Group"), (100000002, "Tenant"));
    }

    /// <summary>
    /// 角色tuple及稽核租戶／事件各有資料庫alternate key，所有鍵欄位非空；
    /// 稽核鍵來自權威修改事件，絕不按前後值等內容去重合法的不同事件。
    /// </summary>
    [Fact]
    public void Schema_AtomicAlternateKeys_DoNotDeduplicateAuditEvents()
    {
        using var document = ReadSchema();
        AssertKey(Entity(document.RootElement, "new_role"), "new_ak_role", "new_tenantid", "new_code");
        AssertKey(Entity(document.RootElement, "new_rolepermission"), "new_ak_rolepermission",
            "new_roleid", "new_feature", "new_operation", "new_scope");
        AssertKey(Entity(document.RootElement, "new_roleassignment"), "new_ak_roleassignment",
            "new_contactid", "new_roleid");
        AssertKey(Entity(document.RootElement, "new_auditentry"), "new_ak_auditevent", "new_tenantid", "new_eventid");
    }

    /// <summary>
    /// 正式平台資料夾可延伸實體，但同一 entity／attribute 不可在另一檔重複；
    /// F8 四表僅由 F8 檔擁有，避免工作包互相重定義而覆蓋他人的 metadata。
    /// </summary>
    [Fact]
    public void Platform_CrossFileAttributeIdentities_AreUnique()
    {
        using var access = ReadSchema();
        var owned = access.RootElement.GetProperty("entities").EnumerateArray()
            .Select(entity => entity.GetProperty("logicalName").GetString()!).ToHashSet(StringComparer.Ordinal);
        var identities = new HashSet<(string Entity, string Attribute)>();
        var folder = Path.Combine(RepositoryRoot(), "d365", "schema", "platform");
        foreach (var path in Directory.EnumerateFiles(folder, "*.schema.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 32 });
            foreach (var entity in document.RootElement.GetProperty("entities").EnumerateArray())
            {
                var name = entity.GetProperty("logicalName").GetString()!;
                if (owned.Contains(name))
                    Assert.Equal("f8-access.schema.json", Path.GetFileName(path));
                foreach (var field in entity.GetProperty("fields").EnumerateArray())
                    Assert.True(identities.Add((name, field.GetProperty("logicalName").GetString()!)),
                        "Platform contains a repeated entity/attribute identity.");
            }
        }
    }

    /// <summary>
    /// 透過已建置部署器的真正分層入口讀取每個產品，不能只測寬鬆 JSON 解析；
    /// 錯欄位、未知屬性、缺 lookup 目標或錯 ownership 都會在任何 SDK 連線前暴露。
    /// </summary>
    /// <param name="organization">layers.json 已核定的測試組織名稱，僅用於離線組合。</param>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void FormalSchema_AllOrganizations_LoadF8ThroughDeployment(string organization)
    {
        using var expected = ReadSchema();
        var merged = LoadFormalSchema(RepositoryRoot(), organization);
        var json = JsonSerializer.SerializeToElement(merged, merged.GetType(),
            SchemaJson);
        foreach (var entity in expected.RootElement.GetProperty("entities").EnumerateArray())
        {
            var actual = Entity(json, entity.GetProperty("logicalName").GetString()!);
            Assert.Equal(entity.GetProperty("ownershipType").GetString(), actual.GetProperty("ownershipType").GetString());
            Assert.Equal(entity.GetProperty("fields").GetArrayLength(), actual.GetProperty("fields").GetArrayLength());
        }
    }

    /// <summary>
    /// 先用 core＋F8 的合法臨時候選通過正式解析，再加入完全相同的第二份 F8；
    /// 必須因跨檔 attribute 衝突拒絕，而非因 fixture 原本缺欄或 lookup 壞掉假通過。
    /// </summary>
    [Fact]
    public void FormalSchema_DuplicateAttributesAcrossFiles_RejectsEntireCandidate()
    {
        using var document = ReadSchema();
        var root = Path.Combine(Path.GetTempPath(), "f8-schema-" + Guid.NewGuid().ToString("N"));
        var platform = Path.Combine(root, "d365", "schema", "platform");
        try
        {
            Directory.CreateDirectory(platform);
            var source = Path.Combine(RepositoryRoot(), "d365", "schema", "platform");
            foreach (var name in new[] { "00-core.schema.json", "f8-access.schema.json" })
                File.Copy(Path.Combine(source, name), Path.Combine(platform, name));
            File.WriteAllText(Path.Combine(root, "d365", "schema", "layers.json"),
                "{\"schemaVersion\":1,\"organizations\":{\"construction\":[\"platform\"]}}\r\n",
                new UTF8Encoding(false));
            Assert.NotNull(LoadFormalSchema(root, "construction"));
            File.Copy(Path.Combine(platform, "f8-access.schema.json"), Path.Combine(platform, "zz-duplicate.schema.json"));
            Assert.Throws<InvalidDataException>(() => LoadFormalSchema(root, "construction"));
        }
        finally
        {
            // 路徑只由本案的 TempPath＋固定前綴＋GUID 建立，無 caller、repo 或外部目錄輸入。
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    /// <summary>
    /// 真正 CLI 純解析器必須接受三產品的分層 plan／verify／apply，
    /// 並拒絕用 F8 單檔繞過完整組合；僅解析參數，不執行命令、鎖、連線或寫入。
    /// </summary>
    /// <param name="organization">僅供參數驗證的合成目標名稱。</param>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void FormalCli_LayeredCommands_RejectSingleFileOverride(string organization)
    {
        var method = DeploymentType("SchemaDeploymentCli").GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var command in new[] { "plan", "verify", "apply" })
        {
            var args = new List<string> { command, "--org", organization };
            if (command == "apply") args.AddRange(["--plan", "synthetic-reviewed-plan.json"]);
            object?[] parameters = [args.ToArray(), organization, null, null, null, null];
            Assert.Equal(true, method.Invoke(null, parameters));
            Assert.Null(parameters[3]);
            args.AddRange(["--schema", Path.Combine("d365", "schema", "platform", "f8-access.schema.json")]);
            parameters = [args.ToArray(), organization, null, null, null, null];
            Assert.Equal(false, method.Invoke(null, parameters));
        }
    }

    /// <summary>
    /// 文件不能把應用 Tenant 範圍當營運後門，或把尚無來源的 Group、租戶直接登入、
    /// FS 實機部署寫成完成；只保護可持續維護的關鍵決策，不做全文排版快照。
    /// </summary>
    [Fact]
    public void Documentation_D17AndDeploymentGates_RemainExplicit()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "architecture", "d365-backoffice-access.md");
        Assert.True(File.Exists(path), "Missing F8 back-office access document.");
        var text = File.ReadAllText(path);
        foreach (var required in new[]
        {
            "D17", "音訊科技營運", "LINE／LIFF／網頁", "最小權限", "稽核",
            "後門", "F0 metadata", "Group", "FS Step 3b", "零 D365 寫入",
            "BackOffice_TenantAUser_CannotSeeTenantB", "之後再說"
        })
            Assert.Contains(required, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 三個交付檔都必須嚴格 UTF-8、無 BOM、只有 CRLF 且有末尾 CRLF；
    /// 無法解碼、置換字元及裸 CR／LF 都是失敗，不以編輯器顯示代替位元組證據。
    /// </summary>
    /// <param name="relativePath">本 sidecar 唯一可新增的來源路徑。</param>
    [Theory]
    [InlineData("d365/schema/platform/f8-access.schema.json")]
    [InlineData("docs/architecture/d365-backoffice-access.md")]
    [InlineData("SpeechMessage.Access.Tests/AccessSchemaTests.cs")]
    public void Files_Utf8WithoutBomAndCrLf_AreRequired(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), relativePath));
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        var stripped = text.Replace("\r\n", "", StringComparison.Ordinal);
        Assert.DoesNotContain('\r', stripped);
        Assert.DoesNotContain('\n', stripped);
        Assert.DoesNotContain('\uFFFD', text);
    }

    /// <summary>只找目前測試輸出所屬的第一個方案根，不回退另一工作樹或存取使用者設定。</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Test worktree root is missing.");
    }

    /// <summary>缺檔先給明確斷言；返回的短命 JSON owner 必須由每個測試 using 釋放。</summary>
    private static JsonDocument ReadSchema()
    {
        var path = Path.Combine(RepositoryRoot(), "d365", "schema", "platform", "f8-access.schema.json");
        Assert.True(File.Exists(path), "Missing F8 schema: f8-access.schema.json");
        return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 32 });
    }

    /// <summary>正式邏輯 ID 必須恰好一筆；不按顯示文字去重或接受最後一筆覆蓋。</summary>
    private static JsonElement Entity(JsonElement root, string name) =>
        Assert.Single(root.GetProperty("entities").EnumerateArray(),
            entity => entity.GetProperty("logicalName").GetString() == name);

    /// <summary>以獨立期望核對完整欄位，鍵／主名稱的 phase=1 是現行正式部署器契約。</summary>
    private static JsonElement AssertField(JsonElement entity, string name, string type,
        int length = 0, bool required = false, string? target = null)
    {
        var field = Assert.Single(entity.GetProperty("fields").EnumerateArray(),
            candidate => candidate.GetProperty("logicalName").GetString() == name);
        Assert.Equal(type, field.GetProperty("type").GetString());
        Assert.Equal(1, field.GetProperty("phase").GetInt32());
        Assert.Equal(length, field.TryGetProperty("maxLength", out var value) ? value.GetInt32() : 0);
        Assert.Equal(required, field.TryGetProperty("required", out value) && value.GetBoolean());
        Assert.Equal(target, field.TryGetProperty("target", out value) ? value.GetString() : null);
        return field;
    }

    /// <summary>數量與每一個 literal／label 均須精確相符，不容重複值被字典覆寫。</summary>
    private static void AssertChoices(JsonElement entity, string name, params (int Value, string Label)[] expected)
    {
        var field = Assert.Single(entity.GetProperty("fields").EnumerateArray(),
            candidate => candidate.GetProperty("logicalName").GetString() == name);
        var options = field.GetProperty("options").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, options.Length);
        foreach (var option in expected)
        {
            var actual = Assert.Single(options, candidate => candidate.GetProperty("value").GetInt32() == option.Value);
            Assert.Equal(option.Label, actual.GetProperty("label").GetString());
        }
    }

    /// <summary>唯一鍵是完整欄位集合，不以清單順序或名稱相同代表唯一性已成立。</summary>
    private static void AssertKey(JsonElement entity, string name, params string[] attributes)
    {
        var key = Assert.Single(entity.GetProperty("keys").EnumerateArray());
        Assert.Equal(name, key.GetProperty("logicalName").GetString());
        Assert.Equal(attributes.Order(StringComparer.Ordinal),
            key.GetProperty("attributes").EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
        foreach (var attribute in attributes)
        {
            var field = Assert.Single(entity.GetProperty("fields").EnumerateArray(),
                candidate => candidate.GetProperty("logicalName").GetString() == attribute);
            Assert.True(field.GetProperty("required").GetBoolean());
        }
    }

    /// <summary>
    /// 只載入同工作樹、同組態的已建置 Deployment；不呼叫進入點或建立組件依賴。
    /// 不存在即阻擋驗證，不能靜默 skip，也不回退別組態或其他工作樹的舊產物。
    /// </summary>
    private static Type DeploymentType(string name)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var path = Path.Combine(RepositoryRoot(), "SpeechMessage.Dynamics.Deployment", "bin",
            configuration, "net10.0", "SpeechMessage.Dynamics.Deployment.dll");
        Assert.True(File.Exists(path), "Build SpeechMessage.Dynamics.Deployment in the same configuration before schema tests.");
        return Assembly.LoadFrom(path).GetType("SpeechMessage.Dynamics.Deployment.Schema." + name, throwOnError: true)!;
    }

    /// <summary>保留正式解析器的真例外型別與堆疊，不能把任何反射失敗都當成預期資料拒絕。</summary>
    private static object LoadFormalSchema(string root, string organization)
    {
        var method = DeploymentType("SchemaFiles").GetMethod("LoadForOrganization", BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return method.Invoke(null, [root, organization])!; }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
