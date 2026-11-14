/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-03a 註冊清單第二版的實際載入及規劃邊界。以 Fixture 的
 * 私有暫存目錄寫合成 JSON，使用正式 loader 及 DeploymentEngine，不建立真實
 * SDK 連線、不讀 User Secrets、不推播。Fixture 在每個案例結束時釋放暫存檔。
 *
 * 主要功能與流程：同型別多步驟應只規劃一次型別建立；舊版本、缺必要欄位、
 * 重複身分、不合法映像與 Shadow secure 設定在 SDK 寫入前拒絕。測試直接
 * 斷言計畫操作、載入結果或安全失敗，不以文字搜尋代替產品行為。
 *
 * 使用邊界與維護：本檔只驗證離線合成契約，不能作為映像或活動實機註冊證據。
 * 新增欄位時須保留這些拒絕案例，並保持每案例檔案、服務與候選集合互不共享。
 */
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>透過真實 loader 與 planner 驗證清單第二版的格式與穩定身份契約。</summary>
public sealed class ManifestV2Tests
{
    /// <summary>固定合成診斷步驟；完整列出欄位，避免預設值掩蓋缺漏。</summary>
    private const string Document = """
{
  "schemaVersion":2,
  "assemblies":[{
    "assemblyName":"SpeechMessage.Dynamics.Plugins",
    "steps":[{
      "module":"diagnostics",
      "type":"SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin",
      "message":"Create","entity":"task","stage":"PostOperation","mode":"Asynchronous","rank":1,
      "filteringAttributes":[],"images":[],
      "unsecureConfig":"{\"mode\":\"trace\",\"culture\":\"zh-TW\"}","secureConfig":"",
      "enabledByOrganization":{"goodshepherdback":false},
      "legacy":[],"shadowReport":{"file":"","differences":null,"basis":""}
    }]
  }]
}
""";

    /// <summary>同一公開型別註冊兩個順位，真實計畫須只建立一個型別及兩個獨立步驟。</summary>
    [Fact]
    public void Manifest_V2_MultipleStepsPerType_Planned()
    {
        using var fixture = new Fixture();
        var document = JsonNode.Parse(Document)!.AsObject();
        var steps = document["assemblies"]![0]!["steps"]!.AsArray();
        var second = steps[0]!.DeepClone();
        second["rank"] = 2;
        steps.Add(second);
        Write(fixture, document);
        var manifest = RegistrationManifestLoader.Load(fixture.Root);

        var plan = fixture.Engine.Plan(manifest, fixture.Image, Fixture.Organization);

        Assert.Single(plan.Operations.Where(operation => operation.Kind == "CreateType"));
        Assert.Equal(2, plan.Operations.Count(operation => operation.Kind == "CreateStep"));
        Assert.Empty(fixture.Service.Writes);
    }

    /// <summary>舊格式必須拒絕，不能自動以預設值升級而漏掉註冊內容。</summary>
    [Fact]
    public void Manifest_V1_Refused()
    {
        using var fixture = new Fixture();
        Write(fixture, JsonNode.Parse("""{"schemaVersion":1,"assemblyName":"SpeechMessage.Dynamics.Plugins","steps":[]}""")!);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Load(fixture.Root));
    }

    /// <summary>漏掉必要欄位不得由 DTO 預設值補成有效步驟。</summary>
    /// <param name="missing">故障注入要刪除的必要欄位。</param>
    [Theory]
    [InlineData("module")]
    [InlineData("type")]
    [InlineData("message")]
    [InlineData("rank")]
    [InlineData("enabledByOrganization")]
    public void Manifest_MissingRequiredField_Refused(string missing)
    {
        using var fixture = new Fixture();
        var document = JsonNode.Parse(Document)!;
        document["assemblies"]![0]!["steps"]!.AsArray()[0]!.AsObject().Remove(missing);
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Load(fixture.Root));
    }

    /// <summary>Update 沒有篩選欄位時在規劃前拒絕，防止不必要的跨記錄觸發。</summary>
    [Fact]
    public void Manifest_UpdateWithoutFilter_Rejected()
    {
        using var fixture = new Fixture();
        var document = JsonNode.Parse(Document)!;
        document["assemblies"]![0]!["steps"]![0]!["message"] = "Update";
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Load(fixture.Root));
    }

    /// <summary>映像不列欄位不得擷取整筆記錄；拒絕發生在任何 SDK 讀寫之前。</summary>
    [Fact]
    public void Manifest_ImageWithoutAttributes_Rejected()
    {
        using var fixture = new Fixture();
        var document = JsonNode.Parse(Document)!;
        document["assemblies"]![0]!["steps"]![0]!["images"] = JsonNode.Parse("""[{"name":"PostImage","type":"PostImage","attributes":[]}]""");
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Load(fixture.Root));
    }

    /// <summary>Shadow 即使只有秘密佔位也不可有 secure 設定，避免後續解析或解密。</summary>
    [Fact]
    public void Manifest_ShadowWithSecureConfig_Rejected()
    {
        using var fixture = new Fixture();
        var document = JsonNode.Parse(Document)!;
        var step = document["assemblies"]![0]!["steps"]![0]!;
        step["module"] = "attendance";
        step["unsecureConfig"] = "{\"mode\":\"Shadow\"}";
        step["secureConfig"] = "${secret:TokenKey}";
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Load(fixture.Root));
    }

    /// <summary>完全相同的穩定鍵不能被合併或重建，必須拒絕整份候選。</summary>
    [Fact]
    public void Manifest_DuplicateStepIdentity_Refused()
    {
        using var fixture = new Fixture();
        var document = JsonNode.Parse(Document)!;
        var steps = document["assemblies"]![0]!["steps"]!.AsArray();
        steps.Add(steps[0]!.DeepClone());
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Load(fixture.Root));
    }

    /// <summary>只寫案例自己擁有的固定清單位置，內容皆為不含機密的合成資料。</summary>
    private static void Write(Fixture fixture, JsonNode document)
    {
        var directory = Path.Combine(fixture.Root, "d365", "registration");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugins.registration.json"), document.ToJsonString());
    }
}
