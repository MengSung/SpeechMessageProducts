/*
 * 檔案整體說明
 * 目的與定位：保護階段7註冊清單的live分級與證據格式，在任何組織連線前拒絕不完整候選。
 * 主要流程：每案以Fixture擁有的暫存目錄寫入完整合成清單，先確認合法輸入可載入，
 * 再注入缺欄、非法分級、活動tokenKey、空理由或偽通過證據，斷言真loader拒絕整份文件。
 * 使用邊界與維護：只使用假SDK與本機暫存檔，不讀User Secrets、不連D365或LINE；沒有跨案例
 * 可變狀態、快取或背景工作。Fixture.Dispose確定性移除案例目錄，文件候選由loader釋放。
 * 驗證限制：這些案例只證明離線格式關卡，不證明任何項目已在測試組織以Live實際執行。
 */
using System;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>用完整合法清單與單點故障驗證分級契約，避免未知欄位拒絕掩蓋真正的檢查缺口。</summary>
public sealed class ManifestLiveTests
{
    /// <summary>缺區塊、非法tier與活動攜帶tokenKey皆須拒絕；先接受控制組以排除其他格式錯誤。</summary>
    /// <param name="fault">唯一注入的分級格式故障。</param>
    [Theory]
    [InlineData("step-missing")]
    [InlineData("activity-missing")]
    [InlineData("step-tier")]
    [InlineData("activity-tier")]
    [InlineData("activity-token")]
    public void Manifest_LiveBlockMissingOrInvalid_Rejected(string fault)
    {
        using var fixture = new Fixture();
        var document = ValidDocument();
        Write(fixture, document);
        _ = RegistrationManifestLoader.LoadDocument(fixture.Root);
        var step = document["assemblies"]![0]!["steps"]![0]!.AsObject();
        var activity = document["assemblies"]![1]!["activities"]![0]!.AsObject();
        switch (fault)
        {
            case "step-missing": step.Remove("live"); break;
            case "activity-missing": activity.Remove("live"); break;
            case "step-tier": step["live"]!["tier"] = "enabled"; break;
            case "activity-tier": activity["live"]!["tier"] = "enabled"; break;
            case "activity-token": activity["live"]!["tokenKey"] = false; break;
            default: throw new ArgumentException("Unknown synthetic fault.", nameof(fault));
        }
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.LoadDocument(fixture.Root));
    }

    /// <summary>以合法提案理由作控制組，再清除理由；步驟與活動的dormant候選都不得發布。</summary>
    /// <param name="activity">是否驗證活動，否則驗證步驟。</param>
    /// <param name="rejectedReason">空白或不含有效提案編號的理由，控制組通過後才注入。</param>
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, " ")]
    [InlineData(false, "untracked reason")]
    [InlineData(true, "P7-PROP-XYZ invalid")]
    public void Manifest_DormantWithoutReason_Rejected(bool activity, string rejectedReason)
    {
        using var fixture = new Fixture();
        var document = ValidDocument();
        var live = document["assemblies"]![activity ? 1 : 0]![activity ? "activities" : "steps"]![0]!["live"]!;
        live["tier"] = "dormant";
        live["reason"] = "P7-PROP-006 synthetic conflict";
        Write(fixture, document);
        _ = RegistrationManifestLoader.LoadDocument(fixture.Root);
        live["reason"] = rejectedReason;
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.LoadDocument(fixture.Root));
    }

    /// <summary>合法pass證據必須完整，變更結果或移除一項必要欄位皆拒絕，不能把空證據當實機成功。</summary>
    /// <param name="fault">改動結果或證據欄位的單點故障。</param>
    [Theory]
    [InlineData("result")]
    [InlineData("file")]
    [InlineData("sha256")]
    [InlineData("at")]
    public void Manifest_EvidenceResultOtherThanPass_Rejected(string fault)
    {
        using var fixture = new Fixture();
        var document = ValidDocument();
        var evidence = document["assemblies"]![0]!["steps"]![0]!["live"]!["evidence"]!;
        evidence["result"] = "pass";
        evidence["file"] = "artifacts/live/synthetic.md";
        evidence["sha256"] = new string('A', 64);
        evidence["at"] = "2026-11-17 13:00 Asia/Taipei (UTC+08:00)";
        Write(fixture, document);
        _ = RegistrationManifestLoader.LoadDocument(fixture.Root);
        evidence[fault] = fault == "result" ? "failed" : "";
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.LoadDocument(fixture.Root));
    }

    /// <summary>診斷不參與組織Live切換：沒有區塊可載入，注入區塊則拒絕，不能隱含啟用診斷。</summary>
    [Fact]
    public void Manifest_DiagnosticsHasNoLiveBlock()
    {
        using var fixture = new Fixture();
        var document = ValidDocument();
        var step = document["assemblies"]![0]!["steps"]![0]!.AsObject();
        step["module"] = "diagnostics";
        step["unsecureConfig"] = "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}";
        var live = step["live"]!.DeepClone();
        step.Remove("live");
        Write(fixture, document);
        _ = RegistrationManifestLoader.LoadDocument(fixture.Root);
        step["live"] = live;
        Write(fixture, document);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.LoadDocument(fixture.Root));
    }

    /// <summary>完整合成資料包含不同組件的單一步驟與活動，不借用受測型別預設值補齊必要欄位。</summary>
    private static JsonObject ValidDocument() => JsonNode.Parse("""
    {"schemaVersion":2,"assemblies":[
      {"assemblyName":"SpeechMessage.Dynamics.Plugins","steps":[{
        "module":"attendance","type":"Sample.Plugin","message":"Create","entity":"task",
        "stage":"PostOperation","mode":"Asynchronous","rank":1,"filteringAttributes":[],"images":[],
        "unsecureConfig":"{\"mode\":\"Shadow\"}","secureConfig":"","enabledByOrganization":{"goodshepherdback":false},
        "legacy":[],"shadowReport":{"file":"","differences":null,"basis":"reading"},
        "live":{"tier":"live","tokenKey":false,"reason":"","evidence":{"file":"","sha256":"","at":"","result":""}}
      }]},
      {"assemblyName":"SpeechMessage.Dynamics.Workflows","activityGroup":"Synthetic","activities":[{
        "module":"care","type":"Sample.Activity","displayName":"Synthetic activity","legacy":[],
        "shadowReport":{"file":"","differences":null,"basis":"reading"},
        "live":{"tier":"live","reason":"","evidence":{"file":"","sha256":"","at":"","result":""}}
      }]}
    ]}
    """)!.AsObject();

    /// <summary>只寫案例owner目錄；候選不含真實名稱、憑證或識別碼，finally由Fixture清理。</summary>
    private static void Write(Fixture fixture, JsonObject document)
    {
        var folder = Path.Combine(fixture.Root, "d365", "registration");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugins.registration.json"), document.ToJsonString());
    }
}
