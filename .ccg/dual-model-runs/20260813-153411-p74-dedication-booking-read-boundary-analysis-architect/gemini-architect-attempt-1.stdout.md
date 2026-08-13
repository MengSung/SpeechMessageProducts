# P7.4 認獻單讀取 disabled boundary：架構分析報告

本報告針對將已存在的 `IPackage01DedicationBookingReadClient` 整合至 ChurchReport，並建立安全 feature gate 隔離邊界的設計進行審查與架構分析。

---

## 1. 架構評估 (Analysis)

目前 `ChurchReport` 系統中，認獻單的讀取主要依賴 `DonationBookingService.FillBookingList`。這是一個同步的 legacy 路徑，其內部實作使用 `ToolUtilityClass` 進行 FetchXML 查詢，並在迴圈中針對每一筆認獻單執行 N+1 的 `RetrieveEntity` 同步呼叫。

為了安全遷移至 Dynamics ProductClient，我們將引入 `IPackage01DedicationBookingReadClient`，該客戶端已在 P7.1 階段完成，能回傳強型別且唯讀的 `DedicationBookingRecordDto` 集合。本次任務的核心在於建立一個安全的 **Feature Gate 雙重防線**，確保在未授權或開關關閉時，絕不產生任何 outbound I/O 或資源初始化，並在開啟時確保 Profile 隔離。

---

## 2. 架構決策 (Architecture Decision)

### 決策 1：雙重 Gate 級聯控制 (Cascading Gate)
* **方案**：`Package01DedicationBookingReadEnabled` (Sub-gate) 必須嚴格依賴 `Package01FeeReadsEnabled` (Base-gate)。
* **理由**：認獻單讀取屬於費用讀取（Fee Reads）的子功能。若 Base-gate 關閉，所有子功能必須強制關閉，以簡化系統的 Rollback 複雜度。
* **拒絕的替代方案**：獨立控制 Sub-gate。這會導致在 Base-gate 關閉但 Sub-gate 開啟的異常組態下，系統仍需初始化部分 Dynamics 傳輸通道，增加安全漏洞風險。

### 決策 2：Fail-Closed 早期驗證 (Early Validation)
* **方案**：在 `DonationDynamicsAccessBootstrap` 的工廠方法中，不論是否傳入 `injectedClient`，皆必須先執行 `BindOptions` 並透過 `EnsureNonEmptyProductProfile` 驗證 `ProfileAlias` 非空。
* **理由**：防止測試或 runtime 注入的 Mock 客戶端繞過部署設定的 Profile 驗證，確保多租戶/多組織間的資料隔離性。

### 決策 3：單一原子化發布 (Atomic Publication)
* **方案**：新 Service 與 Adapter 必須在記憶體中完成所有 DTO 驗證與 Mapping 後，再以單一操作替換 Request-Local 的 `DonationPaymentFormModel.DedicationBookingList`。
* **理由**：避免在網路中斷、取消（Cancellation）或資料格式錯誤時，將部分解析的認獻單寫入 Model，造成 UI 呈現不完整的髒資料（Partial Publication）。

---

## 3. 實作計畫 (Implementation Plan)

### 步驟 1：於 `DonationDynamicsAccessBootstrap` 新增 Gate 與 Factory
在 `DonationDynamicsAccessBootstrap.cs` 中新增以下方法，嚴格遵循 UTF-8 no BOM 與 CRLF 格式，並撰寫完整繁體中文 XML 註解。

```csharp
/// <summary>
/// 檢查 P7.4 認獻單讀取功能是否啟用。此子閘門必須依賴 Package01FeeReadsEnabled 主閘門。
/// </summary>
public static bool IsPackage01DedicationBookingReadEnabled(IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(configuration);
    if (!IsPackage01Enabled(configuration))
    {
        return false;
    }

    var raw = configuration["DynamicsAccess:Package01DedicationBookingReadEnabled"];
    return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 嘗試建立認獻單讀取客戶端。若閘門關閉或 ProfileAlias 為空，將進行 Fail-Closed 阻斷。
/// </summary>
public static IPackage01DedicationBookingReadClient? TryCreatePackage01DedicationBookingReadClient(
    IConfiguration configuration,
    IPackage01DedicationBookingReadClient? injectedClient = null)
{
    ArgumentNullException.ThrowIfNull(configuration);
    if (!IsPackage01DedicationBookingReadEnabled(configuration))
    {
        return null;
    }

    // 強制先驗證 ProfileAlias，防止 injectedClient 繞過安全邊界
    var productOptions = BindOptions(configuration);
    EnsureNonEmptyProductProfile(productOptions, "Package01 dedication booking read");

    if (injectedClient is not null)
    {
        return injectedClient;
    }

    var processHost = GetStartedProcessHost();
    var executor = CreateGatewayExecutor(productOptions, processHost);

    return new Package01DedicationBookingReadClient(
        executor,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Package01DedicationBookingReadClient>.Instance);
}
```

### 步驟 2：建立 `DonationBookingReadService` 與 Adapter
建立獨立的 Service 負責呼叫 Client 並進行資料驗證與轉換。

```csharp
public sealed class DonationBookingReadService
{
    private readonly IPackage01DedicationBookingReadClient _client;
    private readonly string _profileAlias;

    public DonationBookingReadService(IPackage01DedicationBookingReadClient client, string profileAlias)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _profileAlias = profileAlias;
    }

    public async Task FillBookingListAsync(
        DonationPaymentFormModel model,
        Guid contactId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (contactId == Guid.Empty) throw new ArgumentException("Contact ID cannot be empty.", nameof(contactId));

        // 1. Outbound I/O 讀取 DTO
        var dtos = await _client.RetrieveDedicationBookingsByContactAsync(
            _profileAlias,
            "church-report-service",
            contactId,
            contactName: null,
            cancellationToken).ConfigureAwait(false);

        // 2. 於記憶體中進行驗證與 Mapping，避免 Partial Publication
        var tempBookings = new List<DedicationBooking>();
        foreach (var dto in dtos)
        {
            if (dto == null || dto.DedicationBookingId == null)
            {
                throw new InvalidOperationException("Received an invalid or incomplete dedication booking record.");
            }
            tempBookings.Add(MapToModel(dto));
        }

        // 3. 單一原子化替換
        model.DedicationBookingList.Clear();
        foreach (var booking in tempBookings)
        {
            model.DedicationBookingList.Add(booking);
        }
    }

    private DedicationBooking MapToModel(DedicationBookingRecordDto dto)
    {
        return new DedicationBooking
        {
            EntityId = dto.DedicationBookingId.ToString(),
            DedicationCategory = dto.DedicationCategoryLabel ?? "其他認獻",
            DedicationBookingStatus = dto.DedicationBookingStatusLabel ?? "未處理",
            AmountPerStage = Decimal.Truncate(dto.AmountPerStage ?? 0).ToString(),
            TotalStages = dto.TotalStages ?? "0",
            DedicationAmount = Decimal.Truncate(dto.DedicationAmount ?? 0).ToString(),
            PaidPeriod = dto.PaidPeriod ?? string.Empty,
            RollupPaidFee = Decimal.Truncate(dto.RollupPaidFee ?? 0).ToString(),
            StartDate = (dto.StartDate ?? DateTimeOffset.MinValue).LocalDateTime.ToShortDateString(),
            EndDate = (dto.EndDate ?? DateTimeOffset.MinValue).LocalDateTime.ToShortDateString()
        };
    }
}
```

---

## 4. 審查發現與風險評估 (Findings)

### Critical Findings

#### 1. 注入客戶端繞過 ProfileAlias 驗證漏洞
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
* **原理說明**：在現有的 `TryCreatePackage02ContactProfileClient` 等工廠方法中，若傳入的 `injectedClient` 不為 null，會直接將其回傳，而未對 `ProfileAlias` 進行非空檢查。若在 P7.4 認獻單讀取中沿用此模式，將導致測試或特定執行期路徑在 `ProfileAlias` 為空（未設定）的情況下仍能成功建立連線，繞過多租戶安全隔離邊界。
* **修復建議**：必須在 `TryCreatePackage01DedicationBookingReadClient` 入口處，於 `injectedClient is not null` 判斷之前，強制執行 `BindOptions` 與 `EnsureNonEmptyProductProfile`。

---

### Warning Findings

#### 1. Legacy 同步路徑與新非同步路徑的雙軌並存風險
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationBookingService.cs`
* **原理說明**：現有的 `FillBookingList` 為同步 legacy 路徑，使用 `ToolUtility` 進行 N+1 查詢。若呼叫端（如 `DonationPaymentManager`）在啟用新閘門後，未能完全切斷對舊同步路徑的呼叫，將導致同一個 Request 內同時觸發新舊兩套 I/O 機制，造成效能浪費與資料不一致。
* **修復建議**：在 Controller 或 Manager 呼叫端，必須根據 `IsPackage01DedicationBookingReadEnabled` 進行嚴格的 `if-else` 分流。當閘門為 `true` 時，**絕對禁止**呼叫舊的 `DonationBookingService.FillBookingList`。

#### 2. 異常與取消時的 Partial Publication (部分發布) 風險
* **檔案路徑**：新設計的 Adapter / Service 區塊
* **原理說明**：若在非同步讀取或 Mapping 過程中發生 `OperationCanceledException` 或資料驗證失敗，若直接對傳入的 `DonationPaymentFormModel.DedicationBookingList` 進行逐筆寫入或中途清除，會導致前端 Model 處於不完整的髒狀態。
* **修復建議**：必須使用暫存的 `List<T>` 收集完整且驗證通過的資料後，再對 `model.DedicationBookingList` 進行一次性更新。

---

### Info Findings

#### 1. 資源生命週期與無狀態約束
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
* **原理說明**：新設計的 `DonationBookingReadService` 必須為 Request-Local 且無狀態。它不應自行保存 `IPackage01DedicationBookingReadClient` 的生命週期，亦不得持有 `HttpContext` 或 `Session`。所有的連線池與 HTTP 處理器生命週期應由 Generic Host 註冊的 `IDonationDynamicsAccessProcessHost` 統一管理，並在主機關閉時進行確定性釋放（Deterministic Disposal）。
