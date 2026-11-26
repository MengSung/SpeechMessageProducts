/*
檔案整體說明
目的與定位：FS 真正公開 plan／apply／verify 的 UI 整合測試，避免純 XML／ledger helper
通過卻沒有建立、加入方案或讀回已發布 savedquery 的假完成。
主要流程：每案建立僅含 account 欄位及一個公用檢視的臨時分層 repo；CLI 使用真實結構
規劃器與引擎，SDK 外部邊界接到獨立合成 metadata／UI 資料庫。資料庫從實際 request
建立下一輪回應，已發布與未發布集合分開，未知 SDK 操作拒絕，不從被測結果產生期望值。
故障注入：計畫後 XML 漂移、計畫外未發布表單、同名多 ID、發布未完成及建立後加入方案失敗。
完整 metadata 回應由本案資料庫脫離複製，補齊關聯與權限集合；供 schema/UI 發布邊界測試
在真引擎及公開 CLI 注入標籤、cascade、原 SDK 故障與取消，不把缺少 RetrieveEntity 當成拒絕成功。
表單狀態依 Microsoft systemform 參考的 Picklist 與 SDK OptionSetValue 契約合成；
已發布／未發布來源都驗證零與一的原值，缺欄、null、整數、文字及其他錯型別一律拒絕。
續頁故障以合成 SDK 精確計數，驗十六頁與十六 KiB UTF-8 cookie 的邊界及空頁／循環拒絕；
來源身份缺漏或型別替換不得被當成無差異，新表原生 ID 僅沿本次建立的既有授權流程發布。
決定性斷言：零差異重跑不再寫入、同一 SavedQueryId 被保留、發布真正讀回、錯誤前零寫入，
部分失敗重新規劃只補缺項，且每個公開命令最後歸還唯一 lease。
資源與隔離：所有端點、ID、XML 及列均為合成；不讀 User Secrets、不連 D365 或 LINE。
fixture 恢復 cwd 後刪自己的隨機目錄，沒有背景 task／timer／共用快取；所有 stream 已由 CLI 關閉。
這些離線證據不能冒充三個測試組織的實機發布或 UI 匯出入成功。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using SpeechMessage.Dynamics.Deployment.Tests.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>以真 CLI 及 SDK 純合成資料庫驗證 FS UI 生命週期與失敗即拒絕；cwd 共用邊界由集合序列化。</summary>
[Collection("Deployment working directory")]
public sealed class UiViewDeploymentTests
{
    /// <summary>
    /// 已發布與未發布查詢必須各有唯一正式主鍵排序；替身在每次 SDK 入場時獨立擷取排序，
    /// 不允許重用 QueryExpression 時累加同一 ORDER BY。兩來源均完成且零寫入、租約歸還；
    /// 此測試證明查詢組裝缺陷，不能替代實機 FaultException 根因的觀測。
    /// </summary>
    /// <param name="type">本案獨立核對的 UI metadata 類型。</param>
    [Theory]
    [InlineData("systemform")]
    [InlineData("savedquery")]
    public void Ui_PublicPlan_EachPublishedSource_HasOneStablePrimaryKeyOrder(string type)
    {
        using var fixture = new UiFixture();
        var observed = new List<(bool Unpublished, string[] Orders)>();
        fixture.Service.Page = (query, unpublished) =>
        {
            if (query.EntityName == type)
                observed.Add((unpublished, query.Orders.Select(order => order.AttributeName).ToArray()));
            return new EntityCollection();
        };

        Assert.Equal(0, fixture.Run("plan", "--org", "construction"));
        Assert.Equal(2, observed.Count);
        Assert.False(observed[0].Unpublished);
        Assert.True(observed[1].Unpublished);
        var expected = type == "systemform" ? "formid" : "savedqueryid";
        Assert.All(observed, source => Assert.Equal(expected, Assert.Single(source.Orders)));
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(1, fixture.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    /// 以官方 Picklist 的 SDK OptionSetValue 合成零／一，分別覆蓋已發布與未發布盤點。
    /// 公開 plan 必須成功且投影保留原值／正式 ID，不能因 nullable 整數拆箱而失敗；
    /// 只讀期間結構及 UI 均零寫入，命令結束歸還唯一租約，不代表已驗證實機故障原因。
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Ui_PublicPlan_FormActivationChoice_ProjectsSdkValueAndReturnsLease(int activationState, bool published)
    {
        using var fixture = new UiFixture();
        var form = Form("account");
        form["formactivationstate"] = new OptionSetValue(activationState);
        fixture.Service.PendingForms.Add(form);
        var prior = Form("account");
        prior.Id = form.Id;
        prior["formactivationstate"] = new OptionSetValue(published ? activationState : 1 - activationState);
        fixture.Service.PublishedForms.Add(prior);

        var scope = fixture.ReadScope(published);
        Assert.Equal(0, fixture.Run("plan", "--org", "construction"));
        var projected = Assert.Single(published ? scope.Published : scope.Unpublished);
        Assert.Equal(form.Id, projected.Id);
        Assert.Equal(activationState, projected.State);
        if (published) Assert.Empty(UiViewDeployment.Pending(scope));
        else Assert.Equal(form.Id, Assert.Single(UiViewDeployment.Pending(scope)));
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(1, fixture.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    /// 向兩種發布來源注入缺欄、null 及非 OptionSetValue；即使文字／整數看似合法選項也不得轉型放行。
    /// 公開 plan 必須以受控 InvalidDataException 失敗，原例外交給命令診斷且零寫入、租約歸還；
    /// 再直接盤點核對拒絕型別，避免僅憑任意失敗結束碼掩蓋錯誤來源。
    /// </summary>
    [Theory]
    [InlineData("missing", false)]
    [InlineData("null", false)]
    [InlineData("integer", false)]
    [InlineData("text", false)]
    [InlineData("boolean", false)]
    [InlineData("long", false)]
    [InlineData("missing", true)]
    [InlineData("null", true)]
    [InlineData("integer", true)]
    [InlineData("text", true)]
    [InlineData("boolean", true)]
    [InlineData("long", true)]
    public void Ui_PublicPlan_FormActivationMissingOrWrongType_RefusesAndReturnsLease(string fault, bool published)
    {
        using var fixture = new UiFixture();
        var form = Form("account");
        if (fault == "missing") form.Attributes.Remove("formactivationstate");
        else form["formactivationstate"] = fault switch
        {
            "null" => null!,
            "integer" => 1,
            "text" => "1",
            "boolean" => true,
            "long" => 1L,
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        var counterpart = Form("account");
        counterpart.Id = form.Id;
        fixture.Service.PublishedForms.Add(published ? form : counterpart);
        fixture.Service.PendingForms.Add(published ? counterpart : form);

        Assert.Equal(1, fixture.Run("plan", "--org", "construction"));
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
        Assert.Throws<InvalidDataException>(() => fixture.ReadScope(published));
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(1, fixture.Returned);
    }

    /// <summary>
    /// 產生兩類 UI、兩種發布來源的獨立分頁故障，不建立外部資源；
    /// 最大讀取次數是契約期望值，替身第十七頁會結束，讓缺頁數守門的舊程式確定失敗而不長跑。
    /// </summary>
    public static IEnumerable<object[]> PagingFaults()
    {
        foreach (var type in new[] { "systemform", "savedquery" })
        foreach (var unpublished in new[] { false, true })
        foreach (var (fault, reads) in new[]
        {
            ("page-limit", 16), ("empty-page", 1), ("missing-cookie", 1), ("repeat-cookie", 2),
            ("cycle-cookie", 3), ("large-cookie", 1), ("large-final-cookie", 1), ("multibyte-cookie", 1)
        })
            yield return new object[] { type, unpublished, fault, reads };
    }

    /// <summary>
    /// 真正公開 plan 讀取一列續頁，或收到空頁、遺失／重複／循環／超長 cookie；
    /// 第十六頁仍有更多資料就停止，UTF-8 超過十六 KiB 的末頁 cookie 也不得保留。
    /// 以實際 SDK 次數、原 InvalidDataException、零寫入與租約歸還斷言，不以任意錯誤代替有界性。
    /// </summary>
    [Theory]
    [MemberData(nameof(PagingFaults))]
    public void Ui_PublicPlan_IncompletePaging_RefusesWithinBound(string type, bool unpublished, string fault, int maximumReads)
    {
        using var fixture = new UiFixture();
        var reads = 0;
        fixture.Service.Page = (query, source) =>
        {
            if (query.EntityName != type || source != unpublished) return new EntityCollection();
            reads++;
            var page = query.PageInfo.PageNumber;
            var rows = fault == "empty-page" ? Array.Empty<Entity>() : new[] { PagedComponent(type, page) };
            return new EntityCollection(rows)
            {
                MoreRecords = fault switch
                {
                    "page-limit" => page < 17,
                    "large-final-cookie" => false,
                    "repeat-cookie" or "cycle-cookie" or "empty-page" => true,
                    _ => page < 2
                },
                PagingCookie = fault switch
                {
                    "missing-cookie" => "",
                    "repeat-cookie" => "same-cookie",
                    "cycle-cookie" => "cycle-" + (page % 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "large-cookie" or "large-final-cookie" => new string('x', 16 * 1024 + 1),
                    "multibyte-cookie" => new string('界', 5462),
                    _ => "cookie-" + page.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            };
        };

        var result = fixture.Run("plan", "--org", "construction");
        Assert.Equal(maximumReads, reads);
        Assert.Equal(1, result);
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(1, fixture.Returned);
    }

    /// <summary>
    /// 兩來源恰十六頁、一列或二百五十六列／頁，cookie 恰十六 KiB（含多位元組文字）均可完整盤點。
    /// 斷言兩來源各十六次、公開 plan 成功、零寫入及租約歸還，防止上限採嚴格小於而拒絕合法邊界。
    /// </summary>
    [Theory]
    [InlineData("systemform", 1)]
    [InlineData("savedquery", 1)]
    [InlineData("systemform", 256)]
    [InlineData("savedquery", 256)]
    public void Ui_PublicPlan_PagingLimits_ExactBoundarySucceeds(string type, int pageSize)
    {
        using var fixture = new UiFixture();
        var reads = new int[2];
        fixture.Service.Page = (query, unpublished) =>
        {
            if (query.EntityName != type) return new EntityCollection();
            reads[unpublished ? 1 : 0]++;
            var page = query.PageInfo.PageNumber;
            var suffix = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var cookie = pageSize == 1 ? new string('x', 16 * 1024 - suffix.Length) + suffix
                : new string('界', 5460) + new string('x', 4 - suffix.Length) + suffix;
            return new EntityCollection(Enumerable.Range((page - 1) * pageSize + 1, pageSize)
                .Select(index => PagedComponent(type, index)).ToList())
            { MoreRecords = page < 16, PagingCookie = cookie };
        };

        Assert.Equal(0, fixture.Run("plan", "--org", "construction"));
        Assert.All(reads, count => Assert.Equal(16, count));
        Assert.Empty(fixture.Failures);
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(1, fixture.Returned);
    }

    /// <summary>
    /// 在任一來源刪去正式 ID，或以相同 ID 偷換元件型別；兩集合不完整仍須拒絕，
    /// 不能只掃未發布來源而忽略已發布殘留，亦不能將新增元件例外泛化成任意缺漏可通過。
    /// 直接 Pending 及公開 plan 均須受控失敗，所有來源列保持原樣且零寫入、租約歸還。
    /// </summary>
    [Theory]
    [InlineData("systemform", "missing-unpublished")]
    [InlineData("savedquery", "missing-unpublished")]
    [InlineData("systemform", "missing-published")]
    [InlineData("savedquery", "missing-published")]
    [InlineData("systemform", "changed-type")]
    [InlineData("savedquery", "changed-type")]
    public void Ui_PublicPlan_SourceIdentityDrift_RefusesBeforeWrites(string type, string fault)
    {
        using var fixture = new UiFixture();
        var published = PagedComponent(type, 1);
        var unpublished = PagedComponent(fault == "changed-type"
            ? type == "systemform" ? "savedquery" : "systemform" : type, 1);
        if (fault != "missing-published")
        {
            if (type == "systemform") fixture.Service.PublishedForms.Add(published);
            else fixture.Service.Published.Add(published);
        }
        if (fault != "missing-unpublished")
        {
            if (unpublished.LogicalName == "systemform") fixture.Service.PendingForms.Add(unpublished);
            else fixture.Service.Views.Add(unpublished);
        }

        var scope = fixture.ReadScope(false);
        Assert.Throws<InvalidDataException>(() => UiViewDeployment.Pending(scope));
        Assert.Equal(1, fixture.Run("plan", "--org", "construction"));
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(1, fixture.Returned);
    }

    /// <summary>
    /// 真 CLI 建新表時，合成 SDK 自動產生同時存在兩來源的原生表單與預設檢視；
    /// 將其未發布 XML 設為差異，驗既有引擎僅授權本輪新表 nativeIds，仍能定向發布並零差異驗收。
    /// 若在擷取 nativeIds 之後才插入另一個新表元件，即使實體相同也必須拒絕，不可把新表當作整體豁免。
    /// 所有 ID 由替身建立回應產生，未放寬舊表未知元件，fixture 結束刪本案輸出。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ui_PublicApply_NewTableNativeIds_StayWithinCreatedEntityScope(bool lateUnknownComponent)
    {
        using var fixture = new UiFixture(newEntity: true);
        fixture.Service.AddLateNativeForm = lateUnknownComponent;
        var result = fixture.Run("apply", "--org", "construction", "--plan", fixture.Plan());
        Assert.Empty(fixture.Service.FullMetadataReads);
        if (lateUnknownComponent)
        {
            Assert.Equal(1, result);
            Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
            Assert.Equal("", fixture.Service.LastPublishXml);
            Assert.Equal(2, fixture.Service.PendingForms.Count);
            Assert.Equal(2, fixture.Returned);
            return;
        }
        Assert.Equal(0, result);
        Assert.Equal(0, fixture.Run("verify", "--org", "construction"));
        var scope = fixture.ReadScope(false);
        Assert.Equal(3, scope.Unpublished.Count);
        Assert.Equal(3, scope.Published.Count);
        Assert.Empty(UiViewDeployment.Pending(scope));
        Assert.All(scope.Unpublished, row => Assert.Equal("new_example", row.Entity));
        Assert.Single(fixture.Service.PublishedForms);
        Assert.Equal(2, fixture.Service.Published.Count);
        Assert.Equal(1, fixture.Service.ViewCreates);
        Assert.Contains("<entity>new_example</entity>", fixture.Service.LastPublishXml, StringComparison.Ordinal);
        Assert.DoesNotContain("account", fixture.Service.LastPublishXml, StringComparison.Ordinal);
        Assert.Equal(3, fixture.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    /// 單欄都在一 MiB 內仍可能累積過量 XML；公開 plan 必須依整次盤點總預算拒絕，
    /// 並歸還租約，不能保留數千份大型表單讓命令耗盡記憶體。
    /// </summary>
    [Fact]
    public void Ui_PublicPlan_TotalXmlBudget_RefusesAndReturnsLease()
    {
        using var fixture = new UiFixture();
        for (var index = 0; index < 20; index++)
        {
            var form = Form("account");
            form["formxml"] = new string('x', 900_000);
            fixture.Service.PendingForms.Add(form);
        }
        Assert.Equal(1, fixture.Run("plan", "--org", "construction"));
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal(1, fixture.Returned);
    }

    /// <summary>
    /// plan 後首次更新檢視時注入同一正式 ID 的 XML 漂移；發布前必須重新確認寫入內容，
    /// 不可以僅因 ID 在本次元件清冊裡，就連同別人的新修改一起發布。
    /// </summary>
    [Fact]
    public void Ui_PublicApply_SameIdDriftsAfterWrite_RefusesPublication()
    {
        using var fixture = new UiFixture();
        fixture.Service.DriftAfterViewWrite = true;
        Assert.Equal(1, fixture.Run("apply", "--org", "construction", "--plan", fixture.Plan()));
        Assert.Equal("", fixture.Service.LastPublishXml);
        Assert.NotEqual(Assert.Single(fixture.Service.Views).GetAttributeValue<string>("fetchxml"),
            Assert.Single(fixture.Service.Published).GetAttributeValue<string>("fetchxml"));
        Assert.Equal(2, fixture.Returned);
    }

    /// <summary>公開三階段與第二次零寫入往返，必須保留正式 ID，並從已發布集合讀回 XML 與 Active。</summary>
    [Fact]
    public void Ui_PublicPlanApplyVerify_TwoRunsKeepStableIdentityAndReturnEveryLease()
    {
        using var fixture = new UiFixture();
        var plan = fixture.Plan();
        Assert.Equal(0, fixture.Run("apply", "--org", "construction", "--plan", plan));
        Assert.Equal(0, fixture.Run("verify", "--org", "construction"));
        var id = Assert.Single(fixture.Service.Views).Id;
        Assert.Equal(1, fixture.Service.ViewCreates);
        var writes = fixture.Service.UiWrites;
        var second = fixture.Plan();
        Assert.Equal(0, fixture.Run("apply", "--org", "construction", "--plan", second));
        Assert.Equal(writes, fixture.Service.UiWrites);
        Assert.Equal(id, Assert.Single(fixture.Service.Views).Id);
        Assert.Contains("id=\"accountid\"", Assert.Single(fixture.Service.Published).GetAttributeValue<string>("layoutxml"), StringComparison.Ordinal);
        Assert.Contains("object=\"1\"", Assert.Single(fixture.Service.Published).GetAttributeValue<string>("layoutxml"), StringComparison.Ordinal);
        Assert.Equal(5, fixture.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>發布訊息被呼叫但 savedquery 更新僅在未發布來源時，apply／verify 必須失敗，不能把舊已發布 XML 當完成。</summary>
    [Fact]
    public void Ui_PublicVerify_RejectsUnpublishedOnlyViewUpdate()
    {
        using var fixture = new UiFixture();
        fixture.Service.SuppressViewPublication = true;
        var id = Guid.NewGuid();
        fixture.Service.Views.Add(View(id));
        fixture.Service.Published.Add(View(id));
        var plan = fixture.Plan();
        Assert.Equal(1, fixture.Run("apply", "--org", "construction", "--plan", plan));
        Assert.NotEmpty(fixture.Service.Views);
        Assert.NotEqual(Assert.Single(fixture.Service.Views).GetAttributeValue<string>("fetchxml"),
            Assert.Single(fixture.Service.Published).GetAttributeValue<string>("fetchxml"));
        Assert.Equal(1, fixture.Run("verify", "--org", "construction"));
        Assert.Equal(3, fixture.Returned);
    }

    /// <summary>核准 plan 後插入 account 的未發布表單，整個 apply 必須在任何結構／UI 寫入前拒絕。</summary>
    [Fact]
    public void Ui_PublicApply_UnplannedPendingForm_RejectsBeforeAnyWrite()
    {
        using var fixture = new UiFixture();
        var plan = fixture.Plan();
        var published = Form("account");
        fixture.Service.PublishedForms.Add(published);
        var pending = Form("account");
        pending.Id = published.Id;
        pending["formxml"] = "<form><tabs /></form>";
        fixture.Service.PendingForms.Add(pending);
        Assert.Equal(1, fixture.Run("apply", "--org", "construction", "--plan", plan));
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(2, fixture.Returned);
    }

    /// <summary>計畫時已存在的計畫外未發布表單也不得被順帶發布；不能只攔「計畫之後」的漂移。</summary>
    [Fact]
    public void Ui_PublicApply_PreExistingPendingForm_RejectsBeforeAnyWrite()
    {
        using var fixture = new UiFixture();
        var published = Form("account");
        fixture.Service.PublishedForms.Add(published);
        var pending = Form("account");
        pending.Id = published.Id;
        pending["formxml"] = "<form><tabs /></form>";
        fixture.Service.PendingForms.Add(pending);
        var plan = fixture.Plan();
        Assert.Equal(1, fixture.Run("apply", "--org", "construction", "--plan", plan));
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(0, fixture.Service.UiWrites);
    }

    /// <summary>其他實體未發布變更在本次 account 範圍之外；套用仍只發布 account，保留原待發布集合。</summary>
    [Fact]
    public void Ui_PublicApply_UnrelatedEntityRemainsUnpublished()
    {
        using var fixture = new UiFixture();
        var published = Form("contact");
        fixture.Service.PublishedForms.Add(published);
        var pending = Form("contact");
        pending.Id = published.Id;
        pending["formxml"] = "<form><tabs /></form>";
        fixture.Service.PendingForms.Add(pending);
        var plan = fixture.Plan();
        Assert.Equal(0, fixture.Run("apply", "--org", "construction", "--plan", plan));
        Assert.Single(fixture.Service.PendingForms);
        Assert.Equal("<form />", Assert.Single(fixture.Service.PublishedForms).GetAttributeValue<string>("formxml"));
        Assert.DoesNotContain("contact", fixture.Service.LastPublishXml, StringComparison.Ordinal);
    }

    /// <summary>正式 ID 不變但 XML 在 plan 後被改，也必須整體拒絕；計畫不可以只按名稱或 ID 判定仍相同。</summary>
    [Fact]
    public void Ui_PublicApply_ExistingXmlFingerprintDrift_RejectsBeforeWrites()
    {
        using var fixture = new UiFixture();
        Assert.Equal(0, fixture.Run("apply", "--org", "construction", "--plan", fixture.Plan()));
        var plan = fixture.Plan();
        var writes = fixture.Service.UiWrites;
        fixture.Service.Views[0]["fetchxml"] = "<fetch><entity name=\"account\" /></fetch>";
        Assert.Equal(1, fixture.Run("apply", "--org", "construction", "--plan", plan));
        Assert.Equal(writes, fixture.Service.UiWrites);
    }

    /// <summary>檢視已建立但 AddSolutionComponent 失敗；重讀現況並重新 plan 後，僅補缺項，不能重建另一個同名 ID。</summary>
    [Fact]
    public void Ui_PublicApply_PartialFailure_ReplanKeepsCreatedIdentity()
    {
        using var fixture = new UiFixture();
        fixture.Service.FailNextInclude = true;
        Assert.Equal(1, fixture.Run("apply", "--org", "construction", "--plan", fixture.Plan()));
        var id = Assert.Single(fixture.Service.Views).Id;
        var retry = fixture.Run("apply", "--org", "construction", "--plan", fixture.Plan());
        Assert.True(retry == 0, string.Join("\n", fixture.Failures.Select(failure => failure.ToString())));
        Assert.Equal(id, Assert.Single(fixture.Service.Views).Id);
        Assert.Equal(1, fixture.Service.ViewCreates);
        Assert.Equal(0, fixture.Run("verify", "--org", "construction"));
        Assert.Equal(5, fixture.Returned);
    }

    /// <summary>相同名稱的不同正式 ID 都保留，配置不能任選其中一個覆蓋；計畫必須明確拒絕歧義。</summary>
    [Fact]
    public void Ui_PublicPlan_AmbiguousSameNameIds_RefusesWithoutDeletingEither()
    {
        using var fixture = new UiFixture();
        fixture.Service.Views.Add(View(Guid.NewGuid()));
        fixture.Service.Views.Add(View(Guid.NewGuid()));
        foreach (var row in fixture.Service.Views) fixture.Service.Published.Add(View(row.Id));
        Assert.Equal(1, fixture.Run("plan", "--org", "construction"));
        Assert.Equal(2, fixture.Service.Views.Count);
        Assert.Equal(0, fixture.Service.UiWrites);
    }

    /// <summary>
    /// 只含結構／XML 的合成表單；狀態採官方 Picklist 對應的 OptionSetValue，不能用 int 隱藏產品拆箱錯誤。
    /// 不含使用者或租戶內容，identity 為本案正式測試 ID，存活期間由 fixture 集合擁有。
    /// </summary>
    private static Entity Form(string entity) => new("systemform", Guid.NewGuid())
    {
        ["objecttypecode"] = entity, ["name"] = "Synthetic pending form", ["type"] = new OptionSetValue(2),
        ["formactivationstate"] = new OptionSetValue(1), ["ismanaged"] = false, ["formxml"] = "<form />"
    };

    /// <summary>產生同名候選來注入歧義；兩筆不同 ID 均須由產品保留且拒絕任選。</summary>
    private static Entity View(Guid id) => new("savedquery", id)
    {
        ["returnedtypecode"] = "account", ["name"] = "Synthetic public view", ["querytype"] = 0,
        ["isdefault"] = false, ["statecode"] = new OptionSetValue(0), ["ismanaged"] = false,
        ["fetchxml"] = "<fetch />", ["layoutxml"] = "<grid />"
    };

    /// <summary>
    /// 依列序產生可在兩來源重現的合成正式 ID，名稱只用於區分測試頁，不作身份或去重依據；
    /// 固定 account 範圍與小 XML 讓測試只觸發分頁／身份契約，避免碰到其他容量或名稱歧義。
    /// </summary>
    private static Entity PagedComponent(string type, int index)
    {
        var row = type == "systemform" ? Form("account") : View(Guid.NewGuid());
        row.Id = new Guid(index, 0, 0, new byte[8]);
        row["name"] = "Synthetic paged component " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return row;
    }

    /// <summary>唯一臨時 repo 與租約 owner；每個命令使用同案資料庫，計數器能驗證失敗路徑的確定釋放。</summary>
    internal sealed class UiFixture : IOrganizationConnector, IDisposable
    {
        private readonly SchemaLayerFixture repository;
        private readonly string previous = Environment.CurrentDirectory;
        /// <summary>本案獨占的合成 SDK；只供真引擎與公開 CLI 借用。</summary>
        internal UiService Service { get; }
        /// <summary>每個公開命令歸還的租約數量，不包含直接引擎呼叫。</summary>
        internal int Returned { get; private set; }
        /// <summary>本案命令原例外，Dispose 清除參照，不保存外部錯誤或個資。</summary>
        internal List<Exception> Failures { get; } = new();
        /// <summary>只屬此 fixture 的臨時 repo；案例可獨立檢查 before 證據。</summary>
        internal string Root => repository.Root;
        /// <summary>每次由正式分層載入器重新讀取合成清單，沒有共用可變定義。</summary>
        internal SchemaDefinition Definition => SchemaFiles.LoadForOrganization(repository.Root, "construction");
        /// <summary>
        /// 先建立獨立 owner 再切換 cwd；預設只擴充 account，可選全新表以驗 server 原生 UI ID 授權。
        /// existingSchema 先用無 UI 真引擎完成合成結構，供零 schema 操作回歸；其寫入計數不歸零。
        /// 整個測試集合序列執行，Dispose 一定先復原 cwd，沒有實際 SDK 連線或秘密。
        /// </summary>
        internal UiFixture(bool newEntity = false, bool existingSchema = false)
        {
            repository = SchemaLayerFixture.Create(
                newEntity
                    ? """{"schemaVersion":1,"organizations":{"construction":["platform"]},"views":{"platform":[{"entity":"new_example","name":"Synthetic public view","columns":["new_name"],"filter":[],"order":[],"isDefault":false}]}}"""
                    : """{"schemaVersion":1,"organizations":{"construction":["platform"]},"views":{"platform":[{"entity":"account","name":"Synthetic public view","columns":["new_tenantcode"],"filter":[],"order":[],"isDefault":false}]}}""",
                new Dictionary<string, string>
                {
                    ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(newEntity
                        ? SchemaLayerFixture.NewEntity("new_example", "OrganizationOwned", false)
                        : SchemaLayerFixture.Account("new_tenantcode"))
                });
            try
            {
                Service = new UiService(Path.Combine(repository.Root, "artifacts", "d365"));
                if (existingSchema) Service.SeedSchema(Definition);
                Environment.CurrentDirectory = repository.Root;
            }
            catch
            {
                // 建構失敗不會進入using的Dispose；合成schema準備失敗也須由此唯一owner清理本案目錄。
                repository.Dispose();
                throw;
            }
        }
        /// <summary>公開 CLI 只借本案 SDK；摘要不輸出，但任一原例外保留供必要斷言。</summary>
        internal int Run(params string[] args)
        {
            using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            return DeploymentCli.Run(args, this, output, Failures.Add, Path.Combine(repository.Root, "artifacts", "d365"));
        }
        /// <summary>公開 plan 真正落檔後取得本次唯一新增檔，不能從引擎直接偽造核准資料。</summary>
        internal string Plan()
        {
            var before = Directory.GetFiles(repository.Root, "schema-plan.json", SearchOption.AllDirectories).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(0, Run("plan", "--org", "construction"));
            return Assert.Single(Directory.GetFiles(repository.Root, "schema-plan.json", SearchOption.AllDirectories).Where(path => !before.Contains(path)));
        }
        /// <summary>
        /// 借用本案合成 SDK 與正式分層載入器盤點 UI 純值，以直接斷言狀態及發布來源。
        /// 不另建連線或租約；資料僅留在 fixture，publishedOnly 同正式 verify 僅讀已發布來源。
        /// </summary>
        internal UiViewScope ReadScope(bool publishedOnly)
        {
            var definition = SchemaFiles.LoadForOrganization(repository.Root, "construction");
            var snapshot = SchemaMetadata.Read(Service, OrganizationCatalog.LoadDefault().GetTest("construction"), CancellationToken.None);
            return UiViewDeployment.ReadScope(Service, definition, snapshot, CancellationToken.None, publishedOnly);
        }
        /// <summary>只允許本案指定組織；lease 歸還即遞增，不自行持有任何真實連線。</summary>
        public OrganizationLease Connect(string name)
        {
            Assert.Equal("construction", name);
            return new OrganizationLease(Service, () => Returned++);
        }
        /// <summary>先還原全域 cwd，再只刪除本 fixture 的隨機輸出根；所有檔案 owner 已由命令結束釋放。</summary>
        public void Dispose()
        {
            Environment.CurrentDirectory = previous;
            Service.ClearCallbacks();
            Failures.Clear();
            repository.Dispose();
        }
    }

    /// <summary>合成 SDK 只將真請求映射至本案列；metadata 沿用嚴格 Store，UI 與 published 狀態獨立。</summary>
    internal sealed class UiService(string directory) : IOrganizationService
    {
        private readonly SchemaEngineTests.Store metadata = new(directory);
        private Entity? solution;
        private readonly List<Entity> components = new();
        internal List<Entity> Views { get; } = new();
        internal List<Entity> Published { get; } = new();
        internal List<Entity> PendingForms { get; } = new();
        /// <summary>獨立已發布表單來源；每次查詢複製列，與待發布集合比較時不從產品結果反推期望。</summary>
        internal List<Entity> PublishedForms { get; } = new();
        internal int ViewCreates { get; private set; }
        internal int UiWrites { get; private set; }
        internal int MetadataWrites => metadata.WriteCount;
        internal bool SuppressViewPublication { get; set; }
        internal bool FailNextInclude { get; set; }
        internal bool DriftAfterViewWrite { get; set; }
        internal string LastPublishXml { get; private set; } = "";
        /// <summary>只存活本 fixture 的有界續頁故障委派；只替代 UI 查詢回應，不建立 task 或外部來源。</summary>
        internal Func<QueryExpression, bool, EntityCollection>? Page { get; set; }
        /// <summary>在本輪平台檢視建立時才注入未知原生表單；不能被先前已擷取的 nativeIds 授權。</summary>
        internal bool AddLateNativeForm { get; set; }
        /// <summary>完整 metadata 的實體與發布來源紀錄；僅含純值，不能持有 SDK 回應。</summary>
        internal List<(string Entity, bool Unpublished)> FullMetadataReads { get; } = new();
        /// <summary>每次完整回應脫離資料庫後的同步故障注入；owner 結束時清除閉包。</summary>
        internal Action<EntityMetadata, bool>? OnFullMetadataRead { get; set; }
        /// <summary>只在 savedquery 成功建立／更新後執行的同步注入，不包含方案或其他 metadata 寫入。</summary>
        internal Action? AfterViewWrite { get; set; }

        /// <summary>
        /// 先以真 schema 引擎完成無 UI 的合成結構，讓後續計畫確實零 schema 操作；
        /// 設定期寫入計數保留供案例比較，不偽裝為受測 apply 的零寫入。
        /// </summary>
        internal void SeedSchema(SchemaDefinition definition)
        {
            var engine = new SchemaDeploymentEngine(metadata, Path.Combine(directory, "seed"), new SchemaEngineTests.Clock());
            var schema = definition with { Views = Array.Empty<SchemaView>() };
            Assert.Empty(engine.Apply(engine.Plan(schema, default), schema, default).Operations);
        }

        /// <summary>fixture 唯一 owner 同步排空後清除案例回呼與純值紀錄；服務沒有背景工作或外部連線。</summary>
        internal void ClearCallbacks()
        {
            OnFullMetadataRead = null;
            AfterViewWrite = null;
            Page = null;
            FullMetadataReads.Clear();
        }

        /// <summary>
        /// 回應正式 SDK 請求；新表建立回應同時建立兩來源原生表單／檢視，再注入內容差異以測 nativeIds 授權。
        /// 只有精確平台 UI 方案元件可被本替身攔截，其他 metadata 仍走嚴格來源，不接觸業務資料。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveCurrentOrganizationRequest)
            {
                var response = (RetrieveCurrentOrganizationResponse)metadata.Execute(request);
                response.Detail.UniqueName = "construction";
                return response;
            }
            if (request is RetrieveUnpublishedMultipleRequest unpublished)
                return new RetrieveUnpublishedMultipleResponse
                { Results = { ["EntityCollection"] = Select((QueryExpression)unpublished.Query, true) } };
            if (request is CreateEntityRequest created)
            {
                var response = metadata.Execute(request);
                var form = Form(created.Entity.LogicalName);
                PublishedForms.Add(Copy(form));
                form["formxml"] = "<form><tabs /></form>";
                PendingForms.Add(form);
                var view = View(Guid.NewGuid());
                view["returnedtypecode"] = created.Entity.LogicalName;
                view["name"] = "Synthetic native view";
                Published.Add(Copy(view));
                view["fetchxml"] = "<fetch><entity name=\"new_example\" /></fetch>";
                Views.Add(view);
                return response;
            }
            if (request is RetrieveEntityRequest entity)
            {
                Assert.True(entity.LogicalName is "account" or "new_example");
                var row = metadata.Entities.Single(item => item.LogicalName == entity.LogicalName);
                SchemaMetadataTests.MetadataService.SetResponse(row, "ObjectTypeCode", entity.LogicalName == "account" ? 1 : 10321);
                if (entity.EntityFilters == EntityFilters.All)
                {
                    Assert.Equal(Guid.Empty, entity.MetadataId);
                    FullMetadataReads.Add((entity.LogicalName, entity.RetrieveAsIfPublished));
                    row = CompleteMetadata(row);
                    OnFullMetadataRead?.Invoke(row, entity.RetrieveAsIfPublished);
                }
                else Assert.Equal(EntityFilters.Entity, entity.EntityFilters);
                return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = row } };
            }
            if (request is AddSolutionComponentRequest { SolutionUniqueName: "SpeechMessagePlatformUi" } include)
            {
                BeforeUiWrite();
                if (FailNextInclude) { FailNextInclude = false; throw new IOException("Synthetic include failure."); }
                Assert.True(include.ComponentType is 1 or 26);
                if (include.ComponentType == 26)
                {
                    Assert.Contains(Views, row => row.Id == include.ComponentId);
                    components.Add(new Entity("solutioncomponent", Guid.NewGuid())
                    { ["objectid"] = include.ComponentId, ["componenttype"] = new OptionSetValue(26) });
                }
                return new AddSolutionComponentResponse();
            }
            if (request is PublishXmlRequest publish)
            {
                BeforeUiWrite();
                Assert.Contains("<entities>", publish.ParameterXml, StringComparison.Ordinal);
                Assert.DoesNotContain("<all", publish.ParameterXml, StringComparison.OrdinalIgnoreCase);
                LastPublishXml = publish.ParameterXml;
                if (!SuppressViewPublication)
                {
                    var names = System.Xml.Linq.XElement.Parse(publish.ParameterXml).Element("entities")!.Elements("entity")
                        .Select(element => element.Value).ToHashSet(StringComparer.Ordinal);
                    Published.RemoveAll(row => names.Contains(row.GetAttributeValue<string>("returnedtypecode")));
                    Published.AddRange(Views.Where(row => names.Contains(
                        row.GetAttributeValue<string>("returnedtypecode"))).Select(Copy));
                    PublishedForms.RemoveAll(row => names.Contains(row.GetAttributeValue<string>("objecttypecode")));
                    PublishedForms.AddRange(PendingForms.Where(row => names.Contains(
                        row.GetAttributeValue<string>("objecttypecode"))).Select(Copy));
                }
                return new PublishXmlResponse();
            }
            return metadata.Execute(request);
        }

        /// <summary>公開查詢區分兩個方案與 UI 發布層，模擬 server 過濾而非把其他實體回給產品。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            if (expression.EntityName is "savedquery" or "systemform") return Select(expression, false);
            if (expression.EntityName == "solution" && expression.Criteria.Conditions.Any(condition =>
                    condition.AttributeName == "uniquename" && condition.Values.Contains("SpeechMessagePlatformUi")))
                return new EntityCollection(solution is null ? new List<Entity>() : new List<Entity> { Copy(solution) });
            if (expression.EntityName == "solutioncomponent" && solution is not null &&
                expression.Criteria.Conditions.Any(condition => condition.AttributeName == "solutionid" && condition.Values.Contains(solution.Id)))
                return new EntityCollection(components.Select(Copy).ToList());
            return metadata.RetrieveMultiple(query);
        }

        /// <summary>
        /// 建立正式 SavedQueryId 及平台 UI 方案；新增檢視依平台語義同時存在兩來源，更新才等待 PublishXml。
        /// 可在 nativeIds 擷取後注入另一個新表待發布元件，驗不能擴大授權；其他建立僅走 metadata 白名單。
        /// </summary>
        public Guid Create(Entity entity)
        {
            if (entity.LogicalName == "solution" && entity.GetAttributeValue<string>("uniquename") == "SpeechMessagePlatformUi")
            {
                BeforeUiWrite();
                Assert.Null(solution);
                solution = Copy(entity);
                solution.Id = Guid.NewGuid();
                return solution.Id;
            }
            if (entity.LogicalName != "savedquery") return metadata.Create(entity);
            BeforeUiWrite();
            Assert.Equal(Guid.Empty, entity.Id);
            var row = Copy(entity);
            row.Id = Guid.NewGuid();
            row["statecode"] = new OptionSetValue(0);
            row["ismanaged"] = false;
            Views.Add(row);
            Published.Add(Copy(row));
            if (AddLateNativeForm)
            {
                var form = Form("new_example");
                PublishedForms.Add(Copy(form));
                form["formxml"] = "<form><tabs /></form>";
                PendingForms.Add(form);
            }
            if (DriftAfterViewWrite) row["fetchxml"] = "<fetch><entity name=\"account\" /></fetch>";
            ViewCreates++;
            AfterViewWrite?.Invoke();
            return row.Id;
        }

        /// <summary>既有檢視必須按同一正式 ID 更新，不能另建同名替代；只有本案已有列可修改。</summary>
        public void Update(Entity entity)
        {
            Assert.Equal("savedquery", entity.LogicalName);
            BeforeUiWrite();
            var row = Assert.Single(Views, item => item.Id == entity.Id);
            foreach (var item in entity.Attributes) row[item.Key] = item.Value;
            AfterViewWrite?.Invoke();
        }

        /// <summary>
        /// SDK 序列化往返深複製真合成 DB 的 ID、欄位與鍵；單次 stream 在返回前關閉。
        /// 補齊規劃 Store 省略的正式主鍵欄與空集合，只借用純合成工具的關聯形狀；
        /// 主鍵欄 ID 是明列的固定合成值，不以隨機每讀新建造成假漂移，不取代資料庫表 ID。
        /// 不以該工具的其他欄位取代實際資料庫欄位，
        /// metadata ID 仍與 schema snapshot 一致，後續 UI 寫入不得改動已回傳物件或基準。
        /// </summary>
        private static EntityMetadata CompleteMetadata(EntityMetadata source)
        {
            var serializer = new DataContractSerializer(typeof(EntityMetadata));
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, source);
            stream.Position = 0;
            var result = Assert.IsType<EntityMetadata>(serializer.ReadObject(stream));
            var synthetic = UiImportMetadataBoundaryTests.Metadata(source.LogicalName);
            if (!result.Attributes.Any(field => field.LogicalName == result.PrimaryIdAttribute))
                SchemaMetadataTests.MetadataService.SetResponse(result, "Attributes", result.Attributes.Append(
                    new UniqueIdentifierAttributeMetadata
                    {
                        MetadataId = source.LogicalName == "account"
                            ? new Guid("11c5aef1-6525-4a81-bdb1-a25afef9a101")
                            : new Guid("11c5aef1-6525-4a81-bdb1-a25afef9a102"),
                        LogicalName = result.PrimaryIdAttribute
                    }).ToArray());
            SchemaMetadataTests.MetadataService.SetResponse(result, "OneToManyRelationships", synthetic.OneToManyRelationships);
            SchemaMetadataTests.MetadataService.SetResponse(result, "ManyToOneRelationships", Array.Empty<OneToManyRelationshipMetadata>());
            SchemaMetadataTests.MetadataService.SetResponse(result, "ManyToManyRelationships", Array.Empty<ManyToManyRelationshipMetadata>());
            SchemaMetadataTests.MetadataService.SetResponse(result, "Privileges", Array.Empty<SecurityPrivilegeMetadata>());
            return result;
        }

        /// <summary>只委派 metadata 唯讀單列查詢，任何業務表內容仍由嚴格來源拒絕。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => metadata.Retrieve(entityName, id, columnSet);
        /// <summary>流程不允許刪除任何既有或部分完成元件。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>UI 部署不使用任意關聯寫入。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>UI 部署不解除任何舊關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

        /// <summary>依 SDK 明確實體條件選擇資料，沒有跨範圍回傳或未發布／已發布集合共用可變列。</summary>
        private EntityCollection Select(QueryExpression query, bool unpublished)
        {
            if (Page is not null) return Page(query, unpublished);
            var scope = query.Criteria.Conditions.Single().Values.Cast<string>().ToHashSet(StringComparer.Ordinal);
            var rows = query.EntityName == "savedquery" ? unpublished ? Views : Published
                : unpublished ? PendingForms : PublishedForms;
            return new EntityCollection(rows.Where(row => scope.Contains(row.GetAttributeValue<string>(
                query.EntityName == "savedquery" ? "returnedtypecode" : "objecttypecode"))).Select(Copy).ToList());
        }

        /// <summary>UI 第一次寫入前必須已存在關閉可讀的 before 證據；同案重規劃有多個操作目錄，全部仍應完整。</summary>
        private void BeforeUiWrite()
        {
            var evidence = Directory.GetFiles(directory, "before-metadata.json", SearchOption.AllDirectories);
            Assert.NotEmpty(evidence);
            foreach (var path in evidence)
            {
                using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                Assert.True(file.Length > 0);
            }
            UiWrites++;
        }

        /// <summary>每次查詢回傳獨立列容器，防止產品 mutation 直接改到本案資料庫。</summary>
        private static Entity Copy(Entity source)
        {
            var row = new Entity(source.LogicalName, source.Id);
            row.Attributes.AddRange(source.Attributes);
            return row;
        }
    }
}
