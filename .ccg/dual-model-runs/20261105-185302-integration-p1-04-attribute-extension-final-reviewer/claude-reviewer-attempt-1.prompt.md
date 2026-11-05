ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-04-attribute-extension-final

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
Active task: .trellis/tasks/11-05-integration-p1。P1-04最終增量唯讀複審，接續run20261105-184322（雙模型完整，無Critical/High）。禁止修改/擴讀/執行/再調模型；只讀以下固定安全快照，不讀Facade整檔/appsettings/任何秘密。
前輪16份產品與測試快照SHA已全部核對未改；本次只新增離線可執行補證。請核對以下處置，Critical/High/Warning/Info報告；不存在的問題不要為了有建議而硬列。
Claude W2：前輪要求代表性真實caller路徑或合成metadata可執行證據。新增10/10測試真正執行已編譯ContactService.AssignOwner→ToolUtilityClass→Facade→Core→SDK邊界，以及既有35處的7種logical-name全數Retrieve→Assign路徑。未複製業務方法、不重新實作驗證，只替代最末端SDK；另測錯表停止/真SDK例外同一instance不重試/borrowed Dispose0。ContactService ctor會讀LINE設定建client，為避免真憑證/HTTP，本隔離測試用RuntimeHelpers.GetUninitializedObject，只設該方法唯一使用的_toolUtility；因此只證明該方法及真實委派鏈，不代表constructor/DI/auth/live D365。已有明文限制。35處靜態盤點附行號/實體名/來源SHA，不宣稱全部35個業務分支都實際跑過，也不把Mock當實機。W2初始只要求驗logical-name，沒有需要實機；此補證依前輪建議選真代表性方法。
W1 Delete exact案例已確認關閉，本輪不改產品。
Gemini W01保留logger簽章係刻意相容；產品尚無新增caller需改，特性測試使用舊ctor是在驗契約，不是新產品stub。新增正常用途可用無參數ctor，維持現狀。
Gemini W02每extension建立小型局部query物件：有限操作所有權、不持結果、using清參照，避免新增第二套驗證器或shared SDK，並行1000及weakref均綠；無量測退化或可達泄漏，故保留此設計，既有直接query caller無影響，不做無證據的池化或跨scope static。
Nullable Info補完整Directory.Build.targets，真實CoreTargetFrameworkTests亦反射非oblivious；顯式pragma是冗餘但不矛盾，不為風格更動受審產品。
完整00§8結果已解析16TRX2045/2045（未含新增10案，因此需稍後新fresh full solution再驗）；ChurchReport612/612，架構67/67，Debug tracer7/7與正TRX守門；duplicate28project929files0group/new/stale，I18n verify通過，Plugin metadata10/build6，額外工具I18n108、PE27均通過。全方案建置4個既有Obsolete相容多載warning、0error；Core strict0warn/error。這是本機離線結果，非hosted/實機。P1仍2/12，P1-04其他候選未完成。

## 補證摘要
```json
{
  "evidenceKind": "Offline actual compiled business method plus real facade/core chain; external SDK substituted; no VM/LINE/auth/startup claim",
  "representativeTests": {
    "total": "10",
    "executed": "10",
    "passed": "10",
    "failed": "0",
    "error": "0",
    "timeout": "0",
    "aborted": "0",
    "inconclusive": "0",
    "passedButRunAborted": "0",
    "notRunnable": "0",
    "notExecuted": "0",
    "disconnected": "0",
    "warning": "0",
    "completed": "0",
    "inProgress": "0",
    "pending": "0"
  },
  "staticCallsites": [
    {
      "file": "SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs",
      "line": 1141,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs",
      "line": 1224,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Models/PollManager.cs",
      "line": 491,
      "entity": "new_stor_lessons"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs",
      "line": 146,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs",
      "line": 449,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs",
      "line": 482,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs",
      "line": 545,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs",
      "line": 437,
      "entity": "new_stor_lessons"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs",
      "line": 706,
      "entity": "new_fee"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs",
      "line": 461,
      "entity": "new_fee"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs",
      "line": 853,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs",
      "line": 436,
      "entity": "new_group_present_weekly_report"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs",
      "line": 562,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs",
      "line": 591,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs",
      "line": 663,
      "entity": "appointment"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs",
      "line": 793,
      "entity": "list"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.DedicationBooking.cs",
      "line": 142,
      "entity": "new_dedication_booking"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs",
      "line": 661,
      "entity": "new_fee"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs",
      "line": 129,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs",
      "line": 970,
      "entity": "new_fee"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs",
      "line": 229,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs",
      "line": 288,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs",
      "line": 404,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs",
      "line": 967,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs",
      "line": 1082,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs",
      "line": 1224,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs",
      "line": 132,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs",
      "line": 630,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Assignment.cs",
      "line": 95,
      "entity": "contact"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Assignment.cs",
      "line": 208,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs",
      "line": 59,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs",
      "line": 165,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs",
      "line": 195,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs",
      "line": 594,
      "entity": "new_present_record"
    },
    {
      "file": "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs",
      "line": 311,
      "entity": "new_group_present_weekly_report"
    }
  ],
  "callerSourceHashes": {
    "SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs": "441a2ea7e0039556af3470de5ddc05fe18e19c76602e54717dd6a6540f18a77c",
    "SpeechMessageProducts.ChurchReport/Models/PollManager.cs": "f9142793db6fcda75cacc10ba21c49a414e784ae7be2a362f1d906f4fc3dc55b",
    "SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs": "4a6ef1c2b12b5e46783925e70cd981059cb4c6abd37d94c93e2420bd8fe078c7",
    "SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs": "1d73071c339b0663298f1601e8fc59d29611312b8bf33b713dded903df152ca3",
    "SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs": "8a9db9b649467b6ed1c35fb57c3c7dd98abb7db12277110af6661fc70d5a5830",
    "SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs": "6fd5797a73be07b80c611376ab43df6b0f649cb76fcac749f8e86823e51cc43e",
    "SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs": "8df1d9cae3a0fa984a5ac7239e5ff033b6cc6ff5f7f9e596a9285ef5da9e112a",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs": "6d6c547335155ab3ff26d23efe9369dccc08167704db6da9d8ac16daa1e255ce",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs": "e817f7b9c4a066611709dac98682ef74b3815da6a45b89ab81eb28987a05839c",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.DedicationBooking.cs": "04ac6c5b31f4458857080065305bf9d79b0f9ca6a2150b7612b4262ddd8a7595",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs": "c83caa150b715c94d19eb27905094e598d4967c1500b15b1d94d5d216449d46b",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs": "32634e4ddf054fc36003cc262b39e5480313189472e36672093cbec987159e07",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs": "fcdee914ef8c34f0e60a9d2f5e186cae49dace3c7c9022f9883bdb2ec26fab5a",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs": "1bf35fa2adb109ec417636e0dafba4d772edc567d26cf9ce42abb77de10d917d",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs": "6a4e7e837e6b54b5eaa107864685233c452976250bfc3556962ee0d25ded433e",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Assignment.cs": "ffdda0e058841d484e6ea26798ec271dab0916259911f79763474c848f6f0f8c",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs": "b5ea33df12a285a5841521508302e8da8df614f4ee6f79e3863a8937e2de6759",
    "SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs": "b6c66602dec1c4723183acdc34d7e97281aa3b787f0b900eed1b3d4bec99eee9"
  },
  "supplementalFileHashes": {
    "ChurchReport.MemberInfo.Tests/Integration/ContactOwnerRouteTests.cs": "717958cf9c71c4580fa8e9a5fbf543d45168b928c2ba04876e8dc9321873fa1f",
    "Directory.Build.targets": "78eaf4f20616c04afb5d6637584586ae59dbb8240ef7d23fb2ca35d28152ebc4"
  }
}
```

## 真實既有業務方法（僅此安全片段）
```csharp
public void AssignOwner(Guid contactId, Guid ownerId)
        {
            Entity contact = _toolUtility.RetrieveEntity("contact", contactId);
            _toolUtility.AssignOwner("contact", contact, ownerId);
        }
```

## ToolUtility/ToolUtilityPartials/ToolUtilityClass.Attribute.cs 僅AssignOwner委派原文
```csharp
public void AssignOwner(String EntityName, Entity aEntity, Guid OwnerId) => _facade.AssignOwner(EntityName, aEntity, OwnerId);
```

## ToolUtility/Core/ToolUtilityFacade.cs 僅AssignOwner委派原文
```csharp
public void AssignOwner(string entityName, Entity entity, Guid ownerId)
            => _ownerManagementService.Value.AssignOwner(entityName, entity, ownerId);
```

## ChurchReport.MemberInfo.Tests/Integration/ContactOwnerRouteTests.cs
```
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Services.Contact.Impl;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Integration;

/// <summary>
/// 實際執行既有 ContactService.AssignOwner 與 ToolUtilityClass／Facade／Core 委派鏈，
/// 僅替代最末端 SDK I/O。保護 logical name、資料庫 ID、owner 與原始故障不被搬移改壞。
/// </summary>
/// <remarks>
/// ContactService 建構式另建立 LINE client 並讀取設定，與本方法無關；
/// 測試明確略過該建構式，只注入此方法唯一使用的既有 ToolUtilityClass。
/// 因此證據限於已編譯業務方法及真實委派鏈，不代表 DI 啟動、授權或實機 D365 整合。
/// 每個案例獨占 SDK、設定與 utility；不讀應用設定、帳密、不發網路請求或 LINE。
/// </remarks>
public sealed class ContactOwnerRouteTests
{
    /// <summary>
    /// 既有聯絡人業務方法必須先讀 contact，再以相同資料庫 ID 指派。
    /// 注入不同 owner 與目標 GUID，防止測試只因兩個 ID 相同而漏掉錯接。
    /// </summary>
    [Fact]
    public void ContactService_AssignOwner_TraversesActualFacadeWithMatchingIdentity()
    {
        using var fixture = new OwnerRouteFixture();
        var service = CreateContactService(fixture.Utility);

        service.AssignOwner(fixture.RecordId, fixture.OwnerId);

        fixture.AssertSingleAssignment("contact");
    }

    /// <summary>
    /// SDK 回錯資料表時，既有業務方法經 Core 讀取驗證即失敗，不能向錯誤目標繼續指派。
    /// 決定性斷言為讀取一次、Assign 零次，避免以錯 Entity.LogicalName 繞過新檢查。
    /// </summary>
    [Fact]
    public void ContactService_MismatchedRetrievedEntity_StopsBeforeAssignment()
    {
        using var fixture = new OwnerRouteFixture();
        fixture.State.ReturnedEntityName = "account";

        Assert.Throws<InvalidOperationException>(() =>
            CreateContactService(fixture.Utility).AssignOwner(fixture.RecordId, fixture.OwnerId));

        Assert.Equal(1, fixture.State.RetrieveCount);
        Assert.Equal(0, fixture.State.AssignCount);
    }

    /// <summary>
    /// 真實業務／Facade 鏈遇到 SDK 指派逾時時保留同一 Exception，且不隱藏重試。
    /// 此處不執行最終通知邊界，也不把 provider 的原始訊息當成安全輸出。
    /// </summary>
    [Fact]
    public void ContactService_AssignmentFailure_PreservesOriginalExceptionWithoutRetry()
    {
        using var fixture = new OwnerRouteFixture();
        var failure = new TimeoutException("synthetic-assignment-fault");
        fixture.State.AssignmentFailure = failure;

        Assert.Same(failure, Assert.Throws<TimeoutException>(() =>
            CreateContactService(fixture.Utility).AssignOwner(fixture.RecordId, fixture.OwnerId)));

        fixture.AssertSingleAssignment("contact");
    }

    /// <summary>
    /// 依既有 35 處呼叫的七種資料表，實際執行 RetrieveEntity→AssignOwner 公開相容路徑。
    /// 各案例只用合成資料，保留所有 EntityReference 名稱與 GUID；不假稱逐一執行全部業務分支。
    /// </summary>
    /// <param name="entityName">從既有呼叫盤點取得的固定 logical name，不是外部授權資料。</param>
    [Theory]
    [InlineData("contact")]
    [InlineData("new_present_record")]
    [InlineData("new_stor_lessons")]
    [InlineData("new_fee")]
    [InlineData("list")]
    [InlineData("appointment")]
    [InlineData("new_dedication_booking")]
    public void LegacyFacade_RetrieveThenAssign_PreservesEveryObservedEntityName(string entityName)
    {
        using var fixture = new OwnerRouteFixture();
        var entity = fixture.Utility.RetrieveEntity(entityName, fixture.RecordId);

        fixture.Utility.AssignOwner(entityName, entity, fixture.OwnerId);

        fixture.AssertSingleAssignment(entityName);
    }

    /// <summary>
    /// 只為測試隔離無關 LINE 建構副作用；取得真實已編譯 ContactService，
    /// 並明確設入 AssignOwner 唯一存取的依賴。欄位更名會直接令測試失敗。
    /// </summary>
    private static ContactService CreateContactService(ToolUtilityClass utility)
    {
        var service = (ContactService)RuntimeHelpers.GetUninitializedObject(typeof(ContactService));
        var dependency = typeof(ContactService).GetField("_toolUtility", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dependency);
        dependency.SetValue(service, utility);
        return service;
    }

    /// <summary>
    /// 擁有單一案例的 tracer／utility 與 SDK 替身，先釋放 utility 再驗證借入 SDK 未被 Dispose，
    /// 最後由 fixture 釋放自己的 tracer／替身。不存在 static 使用者狀態或外部連線。
    /// </summary>
    private sealed class OwnerRouteFixture : IDisposable
    {
        private readonly NullToolUtilityTracer tracer = new();

        /// <summary>固定合成資料庫 ID；與 owner 不同，能辨識錯誤參數接線。</summary>
        internal Guid RecordId { get; } = new("11111111-1111-1111-1111-111111111111");

        /// <summary>固定合成 owner ID，並不代表真實系統帳戶或授權。</summary>
        internal Guid OwnerId { get; } = new("22222222-2222-2222-2222-222222222222");

        /// <summary>本案例獨占的 SDK 呼叫觀察器，不能與其他案例共用。</summary>
        internal OwnerRouteSdkProxy State { get; }

        /// <summary>正式 ToolUtilityClass，由它建立真實 Facade 與 Core 服務。</summary>
        internal ToolUtilityClass Utility { get; }

        /// <summary>建立無秘密的空設定與純 SDK 邊界，未建立真實 CRM／LINE client。</summary>
        internal OwnerRouteFixture()
        {
            var sdk = DispatchProxy.Create<IOrganizationService, OwnerRouteSdkProxy>();
            State = (OwnerRouteSdkProxy)(object)sdk;
            Utility = new ToolUtilityClass(sdk, tracer, new ConfigurationBuilder().Build());
        }

        /// <summary>比對 SDK 最終請求的完整目標、owner 及唯一呼叫數，不從產品結果推導預期值。</summary>
        internal void AssertSingleAssignment(string entityName)
        {
            Assert.Equal(1, State.RetrieveCount);
            Assert.Equal(1, State.AssignCount);
            var request = Assert.IsType<AssignRequest>(State.Assignment);
            Assert.Equal(entityName, request.Target.LogicalName);
            Assert.Equal(RecordId, request.Target.Id);
            Assert.Equal("systemuser", request.Assignee.LogicalName);
            Assert.Equal(OwnerId, request.Assignee.Id);
            Assert.Equal(RecordId, State.RetrievedId);
        }

        /// <summary>utility 不接管借入 SDK；即使斷言失敗，finally 仍回收 fixture 自有物件。</summary>
        public void Dispose()
        {
            try
            {
                Utility.Dispose();
                Assert.Equal(0, State.DisposeCount);
            }
            finally
            {
                tracer.Dispose();
                State.Dispose();
            }
        }
    }
}

/// <summary>
/// 僅替代 SDK 的兩個 I/O 方法；不重作 logical-name／ID 驗證、不操作檔案或網路。
/// 所有觀察資料只屬單一測試 fixture，Dispose 確定性清除暫存請求與故障參照。
/// </summary>
public class OwnerRouteSdkProxy : DispatchProxy, IDisposable
{
    /// <summary>讀取呼叫次數；本替身僅供單執行緒的有界案例使用。</summary>
    public int RetrieveCount { get; private set; }

    /// <summary>指派呼叫次數，故障亦計入，藉此發現隱藏重試。</summary>
    public int AssignCount { get; private set; }

    /// <summary>測試可注入錯誤回應表名；null 表示沿用原請求值。</summary>
    public string? ReturnedEntityName { get; set; }

    /// <summary>本次讀取要求的資料庫 ID，不保存實體或 caller 服務。</summary>
    public Guid RetrievedId { get; private set; }

    /// <summary>捕捉 SDK 最終指派封包，fixture Dispose 時清除。</summary>
    public OrganizationRequest? Assignment { get; private set; }

    /// <summary>只屬本案例的合成故障，不包含 provider／使用者資料。</summary>
    public Exception? AssignmentFailure { get; set; }

    /// <summary>借入 SDK 被處置次數；utility 結束前應保持零。</summary>
    public int DisposeCount { get; private set; }

    /// <summary>
    /// 只回傳原查詢名稱／ID 或記錄原 AssignRequest；未知呼叫立即拒絕，
    /// 避免業務新增 I/O 後被寬鬆替身默默接受。AllColumns 亦由獨立斷言保護。
    /// </summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Assert.NotNull(args);
        if (targetMethod?.Name == nameof(IOrganizationService.Retrieve))
        {
            RetrieveCount++;
            RetrievedId = Assert.IsType<Guid>(args[1]);
            Assert.True(Assert.IsType<Microsoft.Xrm.Sdk.Query.ColumnSet>(args[2]).AllColumns);
            return new Entity(ReturnedEntityName ?? Assert.IsType<string>(args[0]), RetrievedId);
        }
        if (targetMethod?.Name == nameof(IOrganizationService.Execute))
        {
            AssignCount++;
            Assignment = Assert.IsType<AssignRequest>(args[0]);
            if (AssignmentFailure is not null) throw AssignmentFailure;
            return new AssignResponse();
        }
        throw new InvalidOperationException("Unexpected SDK method in owner route test.");
    }

    /// <summary>只清本 fixture 的觀察參照；沒有需釋放的真實 SDK／連線。</summary>
    public void Dispose()
    {
        DisposeCount++;
        Assignment = null;
        AssignmentFailure = null;
    }
}

```

## Directory.Build.targets
```
<Project>
  <!-- 嚴格規則只套用到明確啟用的新專案；舊專案同時開啟會引入大量既有警告。
       SDK 在 csproj 本體之前讀取 Directory.Build.props，因此必須在 targets 才判斷
       SpeechMessageStrictCode，不能靠命令列覆寫讓測試假通過。 -->
  <PropertyGroup Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisMode>Recommended</AnalysisMode>
    <!-- net48 預設只稽核直接相依；嚴格專案須在還原時稽核全部相依，與 net10 保持一致。 -->
    <NuGetAuditMode>all</NuGetAuditMode>
    <!-- SDK 只為 .NETCoreApp 自動選分析層級；net48 空值會讓 Recommended 規則集不載入。
         使用者 2026-11-03 核定：嚴格模式只在空值時明確採 latest，使雙目標確實同規則；
         net10 原有層級不變，舊專案不啟用嚴格模式也不受影響。 -->
    <AnalysisLevel Condition="'$(AnalysisLevel)' == ''">latest</AnalysisLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <WarningsAsErrors>$(WarningsAsErrors);CA1307;CA1309;CA1310;RS0030</WarningsAsErrors>
  </PropertyGroup>

  <!-- 使用者 2026-11-03 核定的永久規則，適用本計畫所有階段：
       只有嚴格模式、明確測試標記、專案名稱以 .Tests 結尾三者同時成立，
       才允許計畫要求的底線測試命名；僅排除 CA1707，其他分析器與警告視為錯誤不變。
       必須集中在此設定，不得在個別測試專案重複加入其他 NoWarn。 -->
  <PropertyGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And '$(IsTestProject)' == 'true' And $([System.String]::Copy('$(MSBuildProjectName)').EndsWith('.Tests', System.StringComparison.Ordinal))">
    <NoWarn>$(NoWarn);CA1707</NoWarn>
  </PropertyGroup>

  <!-- SDK 在匯入本檔之前已依文件開關決定 DocumentationFile 與輸出複製行為。
       因此 GenerateDocumentationFile 必須在 csproj 本體與嚴格模式一同啟用，
       不能在此補設開關或改寫 SDK 私有屬性；編譯前拒絕空的實際文件路徑，避免假綠燈。 -->
  <Target Name="ValidateSpeechMessageDocumentation" BeforeTargets="CoreCompile" Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <Error Condition="'$(DocumentationFile)' == ''" Code="SMQ0001" Text="嚴格專案必須在 csproj 本體同時設定 SpeechMessageStrictCode=true 與 GenerateDocumentationFile=true；目前 DocumentationFile 為空，無法驗證 XML 文件。" />
  </Target>

  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
    <!-- WarningsAsErrors 不會自行啟用預設停用的規則，另以專用全域設定確保分析器真的執行。 -->
    <GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)build/StrictCode.globalconfig" />
  </ItemGroup>

  <!-- Plugin 與 Workflow 都在 D365 Sandbox 執行，額外拒絕 GDI；一般主機不受此限制。
       分析器依 BannedSymbols.txt 檔名辨識清單，故 Plugin 清單保留同名並置於獨立目錄。 -->
  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And ('$(MSBuildProjectName)' == 'SpeechMessage.Dynamics.Plugins' Or '$(MSBuildProjectName)' == 'SpeechMessage.Dynamics.Workflows')">
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)build/Plugin/BannedSymbols.txt" />
  </ItemGroup>

  <!-- 補丁只補 .NET Framework 缺少的編譯器中繼資料，不在 net10 重複宣告框架型別。
       使用連結檔確保所有雙目標程式庫共用一份定義，不建立額外狀態或組件相依。 -->
  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And ('$(TargetFramework)' == 'net48' Or '$(TargetFramework)' == 'net462')">
    <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/IsExternalInit.cs" Link="Polyfills/IsExternalInit.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/NotNullWhenAttribute.cs" Link="Polyfills/NotNullWhenAttribute.cs" />
  </ItemGroup>
</Project>

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.