Active task: .trellis/tasks/11-05-integration-p1。
P1-04屬性工具/SDK extension/Constants增量唯讀code review，只依以下固定快照。禁止修改、執行、擴讀其他檔案或再呼叫模型；禁止讀Facade/appsettings/任何憑證。Critical/High/Warning/Info各列可達缺陷、精確位置及最小修復；若無明說無。不可用評分替代驗證。
正式P1只抽純SDK，namespace及正常公開簽章保留。產品net462/net10，test宿主net48/net10反射證明。host先授權並擁有借入SDK，Core不Dispose借入，不保存logger/結果/request。無cache/background/notification；測試全合成、Mock非實機。外層固定文字保留真實InnerException，不宣稱原Exception安全；host共用通知邊界禁止記錄Message/ToString/Data。
屬性先12特性綠，安全31案28紅/3綠，修後搬前81綠再git mv；後補null名及精確Query/PageInfo斷言。保留InvalidOperationException wrapper為既有公開相容，無契約禁止Core包裝；ToString失敗以FormatException注入證明。保留null logger拒絕但不保存，新增無參數ctor。
extension舊缺SDK/request/query/異常response可回空，現改fail-closed，是明載安全差異；repo未找到產品extension caller，舊同名測試是instance method。每次局部EntityQueryService並Dispose只清參照，重用唯一根表/整頁驗證，不跨assembly呼internal、不開InternalsVisibleTo。
Constants從host搬Core，21組欄位順序不變，getter clone不共享可變陣列；只有結構字串。Core strict全域啟用Nullable，不必逐檔pragma；attributeName nullable描述既有null語義。
前mutation review20261105-142426雙模型無Critical；Claude W1 Delete：現行空字串保持ArgumentNullException及entityName參數名，已補exact test；純空白改ArgumentException是安全拒絕。ChurchReport搜尋無ArgumentNullException專用catch。W2 AssignOwner：35個有效literal callers逐一沿Entity new/同名Retrieve核對logical-name，未見mismatch；主會話靜態稽核不是實機證據。請核對補測及文件是否關閉前述缺件。不能為相容重新引入敏感包裝或空ID。
最新TRX及每target恰一份coverage由主會話實際解析如下；00§8全方案驗證正在執行，尚未宣稱完成。此review不代表P1-04/P1全包驗收。

## 固定來源與驗證摘要
```json
{
  "scope": "P1-04 attribute-extension slice only; offline, not full P1 acceptance",
  "tests": [
    {
      "path": "artifacts/p1-04-attribute-extension-final-dual/net10.0/net10.0.trx",
      "counters": {
        "total": "395",
        "executed": "395",
        "passed": "395",
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
      }
    },
    {
      "path": "artifacts/p1-04-attribute-extension-final-dual/net48/net48.trx",
      "counters": {
        "total": "314",
        "executed": "314",
        "passed": "314",
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
      }
    }
  ],
  "coverage": [
    {
      "target": "net48",
      "file": "artifacts/p1-04-attribute-extension-final-coverage/net48/9c4450c3-f023-4b22-960f-b5b689f09392/coverage.cobertura.xml",
      "lineRate": 0.9936
    },
    {
      "target": "net10.0",
      "file": "artifacts/p1-04-attribute-extension-final-coverage/net10.0/b4271975-e69b-448c-8609-287bba14f6ac/coverage.cobertura.xml",
      "lineRate": 1.0
    }
  ],
  "sourceHashes": {
    "ToolUtility.Core/AttributeOperations/EntityAttributeUtilityService.cs": "777c6e1a848ba49761dd924eb13db80dabdb02a1257d75c1f218918cfd8faf25",
    "ToolUtility.Core/Interfaces/IEntityAttributeUtilityService.cs": "e59833dfec1541a635725f1589bc52ae9b7cd486506004e4a572c523f3577b74",
    "ToolUtility.Core/Extensions/OrganizationServiceExtensions.cs": "8c1d63c25c849ff64b88dd7ff810d1d6d724b033d18587fa8938b7ad58bcf4a7",
    "ToolUtility.Core/EntityOperations/EntityQueryService.cs": "935320c298a4f541d4872d04c56b1b83aa0b127354e543ceeff2bf684fdbb8e2",
    "ToolUtility.Tests/CoreTargetFrameworkTests.cs": "7ba5f14c0ad355e1f40a6945357f1e61fed82063b0d481b06caa7c81fb26b3b6",
    "ToolUtility.Tests/DataOperations/AttributeExtensionCharacterizationTests.cs": "346e8c1363c0a73cac60f6e34550c302cff32e4ef6facdf73b665dc23d703298",
    "ToolUtility.Tests/DataOperations/AttributeExtensionSafetyTests.cs": "efc4e06b8b12aabf934f40db0beedd1e4eab870a8a24fed21d1a76b469820468",
    "ToolUtility.Core/Constants/CrmEntityColumns.cs": "9d6744a0f07b060027a8c8fa02e2272c63db7d40f31b906b21d3ec8160b48b4d",
    "ToolUtility.Tests/DataOperations/ColumnContractTests.cs": "4dd9b6de00ee3a16e1645df753ba23864986f443d2c0038aafbdf417b4259b52",
    "ToolUtility.Tests/Golden/ToolUtility/columns-all.xml": "512885322d6d7c587a62d7bbb0b8cfc00aca47f67e39a19fbb7fa3e459476f02",
    "ToolUtility.Core/EntityOperations/EntityCrudService.cs": "bb9a8e30c7d2bcddf6248d14ad85d2091897b1b4fedf0f3772e74fe10dea256a",
    "ToolUtility.Core/OwnerOperations/OwnerManagementService.cs": "b910e9e7c9294305eb4996b90e6edf6ba49d7b17e5858043cbc8e29c9232f60b",
    "ToolUtility.Tests/DataOperations/MutationOperationsSafetyTests.cs": "52441dc49424da1cf9ca0c8e433ee38e5e59e3332d1c3b403f1bd5188c194cad",
    "ToolUtility.Core/ToolUtility.Core.csproj": "bd2fb61dc10f78442890bd986404368c44928da65319fb6628d6f51678eff07c",
    "ToolUtility.Tests/ToolUtility.Tests.csproj": "2b7c08f13f77d1fd073bce02664767faf53cd041e568ed10e3658a3a585a87a8",
    "wp-reports/P1-04-attribute-extension.md": "8fd5ba7b39efe4f9c198832fbb2a05afcb6f14966c6dcab9d8a48bad4a7f1baa"
  }
}
```

## 搬前 ToolUtility/AttributeOperations/EntityAttributeUtilityService.cs @ e7f02170
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/AttributeOperations/EntityAttributeUtilityService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class EntityAttributeUtilityService
// 主要成員：GetAttributeValue、RemoveAttribute、SetEntityAttributeToNull
// 引用命名空間：Microsoft.Xrm.Sdk、System、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 實體屬性工具服務實作
    /// 處理實體屬性的通用操作
    /// </summary>
    public class EntityAttributeUtilityService : IEntityAttributeUtilityService
    {
        private readonly object _logger;

        public EntityAttributeUtilityService(object logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 取得屬性值（支援 AliasedValue）
        /// </summary>
        public string GetAttributeValue(Entity targetEntity, string attributeName)
        {
            try
            {
                if (targetEntity == null)
                    throw new ArgumentNullException(nameof(targetEntity));

                if (string.IsNullOrEmpty(attributeName))
                    return string.Empty;

                if (!targetEntity.Contains(attributeName))
                    return string.Empty;

                var value = targetEntity[attributeName];

                if (value is AliasedValue aliasedValue)
                {
                    return aliasedValue.Value?.ToString() ?? string.Empty;
                }
                else
                {
                    return value?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error getting attribute value '{attributeName}' from entity '{targetEntity?.LogicalName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 移除實體的指定屬性
        /// </summary>
        public void RemoveAttribute(ref Entity entity, string propertyName)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                if (string.IsNullOrWhiteSpace(propertyName))
                    throw new ArgumentNullException(nameof(propertyName));

                if (entity.Attributes.Contains(propertyName))
                {
                    entity.Attributes.Remove(propertyName);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error removing attribute '{propertyName}' from entity '{entity?.LogicalName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 將實體的指定屬性設為 null
        /// </summary>
        public void SetEntityAttributeToNull(ref Entity entity, string propertyName)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                if (string.IsNullOrWhiteSpace(propertyName))
                    throw new ArgumentNullException(nameof(propertyName));

                if (entity.Attributes.Contains(propertyName))
                {
                    entity.Attributes[propertyName] = null;
                }
                else
                {
                    entity.Attributes.Add(propertyName, null);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error setting attribute '{propertyName}' to null for entity '{entity?.LogicalName}': {ex.Message}", ex);
            }
        }
    }
}

```

## 搬前 ToolUtility/Interfaces/IEntityAttributeUtilityService.cs @ e7f02170
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Interfaces/IEntityAttributeUtilityService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IEntityAttributeUtilityService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Microsoft.Xrm.Sdk、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace.Interfaces
{
    /// <summary>
    /// 實體屬性工具服務介面
    /// 處理實體屬性的通用操作
    /// </summary>
    public interface IEntityAttributeUtilityService
    {
        /// <summary>
        /// 取得屬性值（支援 AliasedValue）
        /// </summary>
        string GetAttributeValue(Entity targetEntity, string attributeName);

        /// <summary>
        /// 移除實體的指定屬性
        /// </summary>
        void RemoveAttribute(ref Entity entity, string propertyName);

        /// <summary>
        /// 將實體的指定屬性設為 null
        /// </summary>
        void SetEntityAttributeToNull(ref Entity entity, string propertyName);
    }
}

```

## 搬前 ToolUtility/Extensions/OrganizationServiceExtensions.cs @ e7f02170
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Extensions/OrganizationServiceExtensions.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class OrganizationServiceExtensions
// 主要成員：ExecuteRetrieveMultiple
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Messages
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace ToolUtilityNameSpace.Extensions
{
    /// <summary>
    /// Backward compatibility extensions for legacy code expecting ExecuteRetrieveMultiple.
    /// </summary>
    public static class OrganizationServiceExtensions
    {
        /// <summary>
        /// Legacy wrapper: executes a RetrieveMultipleRequest and returns the EntityCollection.
        /// </summary>
        public static EntityCollection ExecuteRetrieveMultiple(this IOrganizationService service, RetrieveMultipleRequest request)
        {
            if (service == null || request == null) return new EntityCollection();
            var resp = (RetrieveMultipleResponse)service.Execute(request);
            return resp?.EntityCollection ?? new EntityCollection();
        }
    }
}

```

## 現行 ToolUtility.Core/AttributeOperations/EntityAttributeUtilityService.cs
```
using System;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 只在呼叫期間操作 caller 獨占 Entity 的無狀態工具，不保存 Entity、logger、憑證或任何租戶資料。
    /// 不建立 I/O、cache、背景工作或 disposable，因此沒有可跨請求殘留的資源或資料。
    /// </summary>
    /// <remarks>
    /// Entity 不是執行緒安全集合；同一 Entity 的並行讀寫須由其 owner 協調。
    /// 保留舊 InvalidOperationException 包裝契約及真實 InnerException，固定外層訊息避免回顯輸入。
    /// 這與 SDK 轉接器的原樣上拋不同，是既有公開 API 的刻意相容，不在 Core 執行最終告警；
    /// host 必須把真實例外交共用入口，禁止輸出原 Message、ToString 或 Entity 內容。
    /// </remarks>
    public class EntityAttributeUtilityService : IEntityAttributeUtilityService
    {
        /// <summary>建立不需要主機診斷依賴的純工具；實例不擁有外部資源，可供不同操作共用。</summary>
        public EntityAttributeUtilityService()
        {
        }

        /// <summary>保留舊建構簽章與 null 拒絕；不保存或處置未使用的記錄物件，避免延長 request 生命。</summary>
        /// <param name="logger">只為來源相容保留的非 null 物件，不能作為租戶或授權依據。</param>
        public EntityAttributeUtilityService(object logger)
        {
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 讀取原值或一層 AliasedValue 的文字；缺欄位、null 值及空欄位名稱依舊回空字串。
        /// 沿用值物件原有 ToString 文化語義，不在資料抽取時改成其他格式或設定全域文化。
        /// </summary>
        /// <param name="targetEntity">本次操作已授權且由 caller 獨占的 Entity，不保存其參照。</param>
        /// <param name="attributeName">既有 logical name；null 或空字串視為未指定。</param>
        /// <returns>原值文字或合法缺值的空字串。</returns>
        /// <exception cref="InvalidOperationException">Entity 無效或值轉字串失敗，InnerException 保留真實故障。</exception>
        public string GetAttributeValue(Entity targetEntity, string? attributeName)
        {
            try
            {
#if NETFRAMEWORK
                if (targetEntity == null) throw new ArgumentNullException(nameof(targetEntity));
#else
                ArgumentNullException.ThrowIfNull(targetEntity);
#endif
                if (string.IsNullOrEmpty(attributeName) || !targetEntity.Contains(attributeName)) return string.Empty;
                var value = targetEntity[attributeName];
                return value is AliasedValue alias ? alias.Value?.ToString() ?? string.Empty : value?.ToString() ?? string.Empty;
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The entity attribute could not be read.", failure);
            }
        }

        /// <summary>
        /// 就地移除指定屬性，缺欄位是合法 no-op；不更換 Entity、不清除其他欄位，也不呼叫 SDK。
        /// 移除代表不提交該屬性，與明確設 null 的清值語義不同。
        /// </summary>
        /// <param name="entity">caller 獨占的 Entity；ref 只保留舊簽章，實例不會被替換。</param>
        /// <param name="propertyName">非空、非全空白欄位名稱。</param>
        /// <exception cref="InvalidOperationException">輸入無效或欄位操作失敗，保留原始 InnerException。</exception>
        public void RemoveAttribute(ref Entity entity, string propertyName)
        {
            try
            {
                RequireMutationInput(entity, propertyName);
                entity.Attributes.Remove(propertyName);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The entity attribute could not be removed.", failure);
            }
        }

        /// <summary>
        /// 明確保存值為 null 的欄位，即使原本沒有該欄也會新增，供 SDK 更新表達清除值。
        /// 僅修改指定欄位，caller 持有完整候選並負責後續授權、提交與發布。
        /// </summary>
        /// <param name="entity">本次操作獨占 Entity，不保留或替換其參照。</param>
        /// <param name="propertyName">非空、非全空白欄位名稱。</param>
        /// <exception cref="InvalidOperationException">輸入無效或欄位操作失敗，保留原始 InnerException。</exception>
        public void SetEntityAttributeToNull(ref Entity entity, string propertyName)
        {
            try
            {
                RequireMutationInput(entity, propertyName);
                entity.Attributes[propertyName] = null;
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The entity attribute could not be cleared.", failure);
            }
        }

        /// <summary>保留舊 mutation 引數例外及參數名，固定訊息不包含 entity／欄位值，不構成授權。</summary>
        private static void RequireMutationInput(Entity entity, string propertyName)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif
            if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentNullException(nameof(propertyName));
        }
    }
}

```

## 現行 ToolUtility.Core/Interfaces/IEntityAttributeUtilityService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Interfaces
{
    /// <summary>
    /// 操作 caller 獨占 SDK Entity 的純屬性契約；不存取伺服器、不保存 Entity 或主機狀態。
    /// 呼叫端須完成授權並避免同一 Entity 並行寫入，服務不接管候選的發布或生命週期。
    /// </summary>
    public interface IEntityAttributeUtilityService
    {
        /// <summary>一般值與單層別名值轉文字，缺值及空欄位名稱回空字串；不改變既有格式文化。</summary>
        /// <param name="targetEntity">已授權且只屬目前操作的 Entity，不保存其參照。</param>
        /// <param name="attributeName">欄位 logical name；null 或空字串依相容契約回空文字。</param>
        /// <returns>原值文字或真正缺值的空字串。</returns>
        /// <exception cref="InvalidOperationException">讀取失敗，以 InnerException 保留真實型別供 host 通知。</exception>
        string GetAttributeValue(Entity targetEntity, string? attributeName);

        /// <summary>就地移除指定欄位；缺欄位不變更其他資料，不替換 caller 的 Entity。</summary>
        /// <param name="entity">操作獨占的可變候選；owner 須協調同一物件的並行寫入。</param>
        /// <param name="propertyName">非空且非全空白欄位名。</param>
        /// <exception cref="InvalidOperationException">輸入或操作失敗；原始故障位於 InnerException。</exception>
        void RemoveAttribute(ref Entity entity, string propertyName);

        /// <summary>保留指定欄位並明確設 null，表達 SDK 清值；原本缺欄位時新增該欄。</summary>
        /// <param name="entity">操作獨占的 Entity，方法不保留或替換其參照。</param>
        /// <param name="propertyName">非空且非全空白欄位名。</param>
        /// <exception cref="InvalidOperationException">輸入或操作失敗；host 須以真實例外走共用通知。</exception>
        void SetEntityAttributeToNull(ref Entity entity, string propertyName);
    }
}

```

## 現行 ToolUtility.Core/Extensions/OrganizationServiceExtensions.cs
```
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.Extensions
{
    /// <summary>
    /// 舊 SDK 相容入口，委派唯一 EntityQueryService 查詢驗證，不建立第二套 XML／資料列驗證器。
    /// 靜態類別沒有可變狀態、快取或 SDK 參照；授權、傳輸逾時與連線 owner 仍由 host 負責。
    /// </summary>
    public static class OrganizationServiceExtensions
    {
        /// <summary>
        /// 執行一次完整原請求，保留 Query、選項與分頁；完整穩定 ID 及根實體驗證後才發布。
        /// 無效輸入或回應明確失敗，只有 SDK 真正回傳的合法空頁才代表查無資料。
        /// 操作局部查詢器確定性 Dispose 只清借入參照，不關閉 SDK、不保存結果、不隱藏重試；
        /// 真實 SDK Exception 原樣傳回 host 的共用通知邊界。
        /// </summary>
        /// <param name="service">host 擁有、已完成使用者／組織／範圍授權的借入 SDK。</param>
        /// <param name="request">caller 獨占且含已授權 Query 的原請求，不在呼叫中並行修改。</param>
        /// <returns>單次操作擁有的完整頁，保持所有不同 ID 的同名記錄。</returns>
        /// <exception cref="ArgumentNullException">SDK 或 request 未提供。</exception>
        /// <exception cref="ArgumentException">Query 無效，在 SDK I/O 前拒絕。</exception>
        /// <exception cref="InvalidOperationException">SDK 回應缺頁、ID 無效／重複或根實體不符。</exception>
        public static EntityCollection ExecuteRetrieveMultiple(this IOrganizationService service, RetrieveMultipleRequest request)
        {
#if NETFRAMEWORK
            if (service == null) throw new ArgumentNullException(nameof(service));
#else
            ArgumentNullException.ThrowIfNull(service);
#endif
            using var queries = new EntityQueryService(organizationService: service);
            return queries.ExecuteRetrieveMultiple(request);
        }
    }
}

```

## 現行 ToolUtility.Core/EntityOperations/EntityQueryService.cs
```
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 不保存查詢結果的純 SDK 實體讀取器；授權、組織與服務身分必須由 host 先驗證。
    /// 每次建立操作局部候選，整頁穩定 ID 驗證後才回傳，不以名稱去重或發布部分結果。
    /// </summary>
    /// <remarks>
    /// SDK 只借入，不接管 Dispose／連線或憑證。服務應與原授權 scope 同生命週期，禁止跨租戶 singleton。
    /// 不建立 cache、timer、queue、Task.Run 或隱藏重試；SDK 的逾時由 host gateway／連線 owner 控制。
    /// Dispose 停止新操作並清除借用參照；已取得局部參照的同步操作可完成，host 須先 drain 才關閉 SDK。
    /// 真實 SDK 例外原樣上傳最終 host 共用通知邊界，不在純核心記錄原訊息或另建 LINE client。
    /// </remarks>
    public class EntityQueryService : IEntityQueryService, IDisposable
    {
        // 僅共享有限的唯讀結構名稱，不含使用者、組織、查詢條件或 SDK 結果。
        private static readonly ReadOnlyCollection<string> TaskFields = Array.AsReadOnly(new[]
        {
            "subject", "statecode", "prioritycode", "scheduledend", "createdby",
            "regardingobjectid", "activityid", "description"
        });
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>
        /// 保留舊 optional 建構式但不保存未使用的任意 logger，避免延長 request／憑證參照。
        /// 為相容允許未初始化 SDK；首次操作會明確拒絕，不將設定失敗當成查無資料。
        /// </summary>
        /// <param name="logger">只為來源相容保留，不使用、不保存，也不 Dispose。</param>
        /// <param name="organizationService">呼叫端已授權且擁有的 SDK；null 僅供舊建構相容。</param>
        public EntityQueryService(object? logger = null, IOrganizationService? organizationService = null)
        {
            _organizationService = organizationService;
        }

        /// <summary>
        /// 依舊單筆契約讀完整欄位，不走快取。SDK 回應 ID／實體必須與請求完全相符，否則拒絕發布。
        /// </summary>
        /// <param name="entityName">伺服器決定的合法資料庫 logical name，不構成授權。</param>
        /// <param name="entityId">已授權的非空資料庫 GUID。</param>
        /// <returns>本次操作擁有且已驗證的單筆實體。</returns>
        public Entity RetrieveEntity(string entityName, Guid entityId)
        {
            var service = GetService();
            RequireLogicalName(entityName, nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(entityId));
            var candidate = service.Retrieve(entityName, entityId, new ColumnSet(true));
            if (candidate == null || candidate.Id != entityId || !string.Equals(candidate.LogicalName, entityName, StringComparison.Ordinal))
                throw new InvalidOperationException("The SDK returned a mismatched database identity.");
            return candidate;
        }

        /// <summary>
        /// 還原舊來源直接 SDK 讀取的明確入口；每次重新查詢，不回傳前次 caller 修改的物件。
        /// 與 RetrieveEntity 共用相同識別驗證及故障傳遞，不複製第二套資料／快取邏輯。
        /// </summary>
        /// <param name="entityName">受信任的合法 logical name，含 XML 特殊字元的結構名稱會被拒絕。</param>
        /// <param name="entityId">伺服器已授權的非空資料庫 GUID。</param>
        /// <returns>本次 SDK 的完整欄位實體，生命週期只屬於呼叫端操作。</returns>
        public Entity RetrieveEntityNoCache(string entityName, Guid entityId) => RetrieveEntity(entityName, entityId);

        /// <summary>
        /// 保留 TopCount=1、AllColumns 與啟用條件的單筆欄位查詢；型別化值不拼接成 XML。
        /// 此 API 的一筆語義不代表按顯示名稱合併資料；空頁回 null，SDK 故障不回 null。
        /// </summary>
        /// <param name="entityName">已授權的資料庫實體名稱。</param>
        /// <param name="fieldName">伺服器決定的資料庫欄位名稱。</param>
        /// <param name="fieldValue">依原 SDK 型別化傳入的查詢值，不保存於服務。</param>
        /// <returns>完整驗頁後的首筆資料，或真正空頁的 null。</returns>
        public Entity? RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            var service = GetService();
            RequireLogicalName(entityName, nameof(entityName));
            RequireLogicalName(fieldName, nameof(fieldName));
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            var candidate = ValidatePage(service.RetrieveMultiple(query), entityName);
            return candidate.Entities.Count == 0 ? null : candidate.Entities[0];
        }

        /// <summary>
        /// 執行呼叫端已授權的查詢，保留既有「PageInfo 為 null 才補 5000／第一頁」相容行為。
        /// 不自動遍歷無界頁面；單頁 MoreRecords／PagingCookie 原樣交由原分頁 owner 繼續處理。
        /// </summary>
        /// <param name="query">操作擁有的查詢，不可在執行期間由其他執行緒改動。</param>
        /// <returns>整頁穩定 ID 驗證後的 SDK 候選，不保存或共享。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var service = GetService();
#if NETFRAMEWORK
            if (query == null) throw new ArgumentNullException(nameof(query));
#else
            ArgumentNullException.ThrowIfNull(query);
#endif
            var entityName = ExpectedEntity(query);
            if (query is QueryExpression expression && expression.PageInfo == null)
                expression.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            return ValidatePage(service.RetrieveMultiple(query), entityName);
        }

        /// <summary>
        /// 保留依名稱查第一個啟用帳戶 ID 的相容 API，只投影 accountid，不作所有記錄的名稱去重。
        /// host 必須先完成業務授權；未查到回 Guid.Empty，SDK 故障與識別衝突仍直接拋出。
        /// </summary>
        /// <param name="accountName">依原資料庫語義查詢的名稱，不是授權或唯一識別。</param>
        /// <returns>已驗證的首筆資料庫 ID，或真正空頁時的 Guid.Empty。</returns>
        public Guid RetrieveAccountByName(string accountName)
        {
            var service = GetService();
            var query = new QueryByAttribute("account") { ColumnSet = new ColumnSet("accountid"), TopCount = 1 };
            query.Attributes.AddRange("name", "statecode");
            query.Values.AddRange(accountName, 0);
            var candidate = ValidatePage(service.RetrieveMultiple(query), "account");
            return candidate.Entities.Count == 0 ? Guid.Empty : candidate.Entities[0].Id;
        }

        /// <summary>
        /// 保留工作欄位、100 筆上限、排序與啟用條件；主旨只能作 XML 屬性值，不能注入結構。
        /// 完整驗證同一消費頁的穩定 ID，合法同名但不同 ID 的工作不被刪減。
        /// </summary>
        /// <param name="subject">原查詢文字；null 依舊 API 視為空文字，不構成授權。</param>
        /// <returns>完整驗證的單頁工作結果；SDK 例外不被空集合遮蔽。</returns>
        public EntityCollection RetrieveTaskBySubject(string subject)
        {
            var service = GetService();
            var entity = new XElement("entity", new XAttribute("name", "task"));
            foreach (var field in TaskFields) entity.Add(new XElement("attribute", new XAttribute("name", field)));
            entity.Add(new XElement("order", new XAttribute("attribute", "createdon"), new XAttribute("descending", "true")),
                new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", "subject"), new XAttribute("operator", "eq"),
                        new XAttribute("value", subject ?? string.Empty)),
                    new XElement("condition", new XAttribute("attribute", "statecode"), new XAttribute("operator", "eq"), new XAttribute("value", "0"))));
            var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", "false"), new XAttribute("top", "100"), entity);
            var response = (RetrieveMultipleResponse)service.Execute(new RetrieveMultipleRequest
                { Query = new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting)) });
            return ValidatePage(response.EntityCollection, "task");
        }

        /// <summary>
        /// 保留完整 RetrieveMultipleRequest（含 SDK 選項）並驗證全部回應 ID，完成前不發布部分頁。
        /// 不攔截或重新包裝 SDK 真實故障，避免丟失 host 共用通知入口所需的原 Exception。
        /// </summary>
        /// <param name="request">呼叫者操作擁有的請求及已授權 Query，不保存於服務。</param>
        /// <returns>原分頁資訊與欄位均保留的完整頁。</returns>
        public EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request)
        {
            var service = GetService();
#if NETFRAMEWORK
            if (request == null) throw new ArgumentNullException(nameof(request));
#else
            ArgumentNullException.ThrowIfNull(request);
#endif
            if (request.Query == null) throw new ArgumentException("A query is required.", nameof(request));
            var entityName = ExpectedEntity(request.Query);
            if (service.Execute(request) is not RetrieveMultipleResponse response)
                throw new InvalidOperationException("The SDK returned an invalid query response.");
            return ValidatePage(response.EntityCollection, entityName);
        }

        /// <summary>
        /// 冪等停止新呼叫並清除保存的借入服務參照；不 Dispose 呼叫端擁有的 SDK，也不阻塞等待外部 I/O。
        /// 已借到局部參照的同步呼叫可完成，host 必須 drain 後才處置其原連線 owner。
        /// </summary>
        /// <param name="disposing">保留既有可覆寫清理契約；本類無自有 native／managed disposable。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Exchange(ref _organizationService, null);
        }

        /// <summary>由本服務 owner 確定性停止新操作；重複呼叫安全，不轉移借入 SDK 的所有權。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>取得單次借用參照；Dispose 前已開始的同步 I/O 不被暗中取消，新操作則立即拒絕。</summary>
        private IOrganizationService GetService()
        {
            var service = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(EntityQueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return service ?? throw new InvalidOperationException("OrganizationService is not initialized.");
        }

        /// <summary>只接受 SDK logical name 字元，不把 XML 編碼當作任意結構名稱的合法性或授權。</summary>
        private static void RequireLogicalName(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A logical name is required.", parameter);
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9') && character != '_')
                    throw new ArgumentException("An invalid logical name was supplied.", parameter);
        }

        /// <summary>
        /// 在任何 I/O 前辨識根實體，包含 FetchExpression，避免通用入口略過跨實體回應檢查。
        /// 此驗證不取代 host 授權，不改寫原 Query 或追加條件；未知 SDK 查詢型別明確拒絕而非降級。
        /// </summary>
        private static string ExpectedEntity(QueryBase query)
        {
            var entityName = query is QueryExpression expression ? expression.EntityName
                : query is QueryByAttribute attributes ? attributes.EntityName
                : query is FetchExpression fetch ? ExpectedFetchEntity(fetch)
                : throw new ArgumentException("Unsupported query type.", nameof(query));
            RequireLogicalName(entityName, nameof(query));
            return entityName;
        }

        /// <summary>
        /// 僅串流辨識一個無命名空間的 fetch／entity 根表，不建立或保存完整 XML 物件樹。
        /// DTD 禁止、resolver 為 null，不能讀檔或網路；字元上限等於既有輸入長度，禁止展開額外內容。
        /// Reader 與 StringReader 由單次操作擁有並在成功／拒絕／解析失敗時確定性 Dispose；
        /// 不保存查詢文字、SDK 或使用者資料。壞 XML 的固定外層訊息不包含原輸入。
        /// </summary>
        private static string ExpectedFetchEntity(FetchExpression query)
        {
            if (string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentException("A FetchXML query is required.", nameof(query));
            try
            {
                using var text = new StringReader(query.Query);
                using var reader = XmlReader.Create(text, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = query.Query.Length
                });
                if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "fetch"
                    || reader.NamespaceURI.Length != 0)
                    throw new ArgumentException("Invalid FetchXML root.", nameof(query));
                string? entityName = null;
                var entityCount = 0;
                while (reader.Read())
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1
                        && reader.LocalName == "entity" && reader.NamespaceURI.Length == 0)
                    {
                        entityCount++;
                        entityName = reader.GetAttribute("name");
                    }
                if (entityCount != 1 || entityName == null || entityName.Length == 0)
                    throw new ArgumentException("A single FetchXML entity is required.", nameof(query));
                return entityName;
            }
            catch (XmlException failure)
            {
                throw new ArgumentException("Invalid FetchXML query.", nameof(query), failure);
            }
        }

        /// <summary>
        /// 候選頁只活至本次操作：驗全部 ID 非空／不重複及查詢根實體，才原子式回傳。
        /// HashSet 與候選沒有 static／cache 引用；衝突不回顯 ID 或本文，也不改動原候選。
        /// </summary>
        private static EntityCollection ValidatePage(EntityCollection? candidate, string entityName)
        {
            if (candidate == null) throw new InvalidOperationException("The SDK returned no query page.");
            var identities = new HashSet<Guid>();
            foreach (var row in candidate.Entities)
                if (row == null || row.Id == Guid.Empty || string.IsNullOrEmpty(row.LogicalName)
                    || !string.Equals(row.LogicalName, entityName, StringComparison.Ordinal)
                    || !identities.Add(row.Id))
                    throw new InvalidOperationException("Query page contains invalid or duplicate database identities.");
            return candidate;
        }
    }
}

```

## 現行 ToolUtility.Tests/CoreTargetFrameworkTests.cs
```
using System.Reflection;
using System.Runtime.Versioning;
using ToolUtilityNameSpace.FeeOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using ToolUtilityNameSpace.OwnerOperations;
using ToolUtilityNameSpace.Interfaces;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.Extensions;
using ToolUtilityNameSpace.Constants;
using Xunit;

namespace ToolUtility.Tests
{
    /// <summary>
    /// 反射實際載入產品組件，而非只讀csproj文字；Framework測試宿主可以net48，
    /// 但產品輸出必須仍是核定net462，不能誤載net48／host組件而假稱雙目標通過。
    /// </summary>
    public sealed class CoreTargetFrameworkTests
    {
        /// <summary>
        /// 比對FeeService所屬assembly的目標中繼資料；錯用任一產品目標即決定性失敗，
        /// 不接觸D365、不配置連線或其他需處置資源。
        /// </summary>
        [Fact]
        public void LoadedCore_UsesApprovedProductTarget()
        {
            var assembly = typeof(FeeService).Assembly;
            Assert.Same(assembly, typeof(EntityQueryService).Assembly);
            Assert.Same(assembly, typeof(AppointmentService).Assembly);
            Assert.Same(assembly, typeof(LessonsService).Assembly);
            Assert.Same(assembly, typeof(MeetingStatisticsService).Assembly);
            Assert.Same(assembly, typeof(EntityCrudService).Assembly);
            Assert.Same(assembly, typeof(OwnerManagementService).Assembly);
            Assert.Same(assembly, typeof(EntityAttributeUtilityService).Assembly);
            Assert.Same(assembly, typeof(IEntityAttributeUtilityService).Assembly);
            Assert.Same(assembly, typeof(OrganizationServiceExtensions).Assembly);
            Assert.Same(assembly, typeof(CrmEntityColumns).Assembly);
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }

#if !NETFRAMEWORK
        /// <summary>
        /// 實際反射抽取介面的公開 nullable 中繼資料，而非以檔頭是否有 pragma 判斷。
        /// Core strict 專案統一啟用 Nullable；若後續設定漂移成 oblivious，任一字串參數即決定性失敗。
        /// 屬性讀取的 attributeName 依舊允許 null，其餘字串必須維持非 null 契約。
        /// 不需修改產品公開 API 或新增重複 pragma 來配合靜態審查推測。
        /// </summary>
        [Theory]
        [InlineData(typeof(IAppointmentService))]
        [InlineData(typeof(ILessonsService))]
        [InlineData(typeof(IMeetingStatisticsService))]
        [InlineData(typeof(IEntityCrudService))]
        [InlineData(typeof(IOwnerManagementService))]
        [InlineData(typeof(IEntityAttributeUtilityService))]
        public void CoreInterfaces_EmitNonObliviousStringContracts(System.Type contract)
        {
            var context = new NullabilityInfoContext();
            var checkedParameters = 0;
            foreach (var method in contract.GetMethods())
                foreach (var parameter in method.GetParameters())
                    if (parameter.ParameterType == typeof(string))
                    {
                        var expected = contract == typeof(IEntityAttributeUtilityService) && parameter.Name == "attributeName"
                            ? NullabilityState.Nullable : NullabilityState.NotNull;
                        Assert.Equal(expected, context.Create(parameter).ReadState);
                        checkedParameters++;
                    }
            Assert.True(checkedParameters > 0);
        }
#endif
    }
}

```

## 現行 ToolUtility.Tests/DataOperations/AttributeExtensionCharacterizationTests.cs
```
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.Extensions;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在抽取前保護屬性工具的缺值、別名與就地更新，以及 SDK 擴充的完整請求／分頁契約。
    /// 僅用合成實體與受控 SDK 邊界；不連線、不配置 logger 或外部資源。
    /// </summary>
    public sealed class AttributeExtensionCharacterizationTests
    {
        /// <summary>一般值、別名值與真正缺值維持舊文字語義，不把合法的空資料當成故障。</summary>
        /// <param name="shape">測試限定的欄位形狀。</param>
        /// <param name="expected">獨立指定的預期文字，不由產品方法推導。</param>
        [Theory]
        [InlineData("plain", "合成欄位")]
        [InlineData("alias", "合成別名")]
        [InlineData("alias-null", "")]
        [InlineData("null", "")]
        [InlineData("missing", "")]
        [InlineData("empty-name", "")]
        [InlineData("null-name", "")]
        public void AttributeRead_PreservesValueAndMissingSemantics(string shape, string expected)
        {
            var entity = new Entity("contact");
            if (shape == "plain") entity["synthetic_value"] = "合成欄位";
            if (shape == "alias") entity["synthetic_value"] = new AliasedValue("contact", "fullname", "合成別名");
            if (shape == "alias-null") entity["synthetic_value"] = new AliasedValue("contact", "fullname", null);
            if (shape == "null") entity["synthetic_value"] = null;
            var service = new EntityAttributeUtilityService(new object());
            Assert.Equal(expected, service.GetAttributeValue(entity,
                shape == "empty-name" ? "" : shape == "null-name" ? null : "synthetic_value"));
            Assert.Equal("contact", entity.LogicalName);
        }

        /// <summary>移除欄位只改原 Entity 的指定屬性；缺欄位合法、其他值與實體參照不能被替換。</summary>
        /// <param name="present">候選是否原本含該欄位。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AttributeRemove_PreservesEntityAndUnrelatedValues(bool present)
        {
            var entity = new Entity("contact") { ["synthetic_keep"] = "保留" };
            if (present) entity["synthetic_remove"] = "刪除此欄";
            var original = entity;
            new EntityAttributeUtilityService(new object()).RemoveAttribute(ref entity, "synthetic_remove");
            Assert.Same(original, entity);
            Assert.False(entity.Contains("synthetic_remove"));
            Assert.Equal("保留", entity.GetAttributeValue<string>("synthetic_keep"));
        }

        /// <summary>設 null 保留屬性存在，才能表達 SDK 清欄位語義；不得誤做成移除屬性而漏送更新。</summary>
        /// <param name="present">欄位是否已存在。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AttributeSetNull_PreservesExplicitNullAndOtherValues(bool present)
        {
            var entity = new Entity("contact") { ["synthetic_keep"] = "保留" };
            if (present) entity["synthetic_clear"] = "清除此值";
            var original = entity;
            new EntityAttributeUtilityService(new object()).SetEntityAttributeToNull(ref entity, "synthetic_clear");
            Assert.Same(original, entity);
            Assert.True(entity.Contains("synthetic_clear"));
            Assert.Null(entity["synthetic_clear"]);
            Assert.Equal("保留", entity.GetAttributeValue<string>("synthetic_keep"));
        }

        /// <summary>
        /// 真實擴充入口只 Execute 一次原請求，保留選項、Query 與分頁欄位；合法同名不同 ID 不可被合併。
        /// SDK 只借用，測試結束不會配置或留下外部連線。
        /// </summary>
        /// <param name="empty">是否為合法空頁。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExtensionExecute_PreservesRequestOptionsAndCompletePage(bool empty)
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("fullname"),
                PageInfo = new PagingInfo { PageNumber = 7, Count = 17, PagingCookie = "synthetic-query-cookie" }
            };
            var request = new RetrieveMultipleRequest { Query = query };
            request["ReturnNotifications"] = true;
            var page = new EntityCollection
            {
                EntityName = "contact", MoreRecords = true, PagingCookie = "synthetic-cookie", TotalRecordCount = 2
            };
            if (!empty)
            {
                page.Entities.Add(new Entity("contact", new Guid("11111111-1111-1111-1111-111111111111")) { ["fullname"] = "合成同名" });
                page.Entities.Add(new Entity("contact", new Guid("22222222-2222-2222-2222-222222222222")) { ["fullname"] = "合成同名" });
            }
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = page;
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(value => value.Execute(request)).Returns(response);

            var actual = sdk.Object.ExecuteRetrieveMultiple(request);

            Assert.Same(page, actual);
            Assert.Same(query, request.Query);
            Assert.Equal("contact", query.EntityName);
            Assert.False(query.ColumnSet.AllColumns);
            Assert.Equal("fullname", Assert.Single(query.ColumnSet.Columns));
            Assert.Equal(7, query.PageInfo.PageNumber);
            Assert.Equal(17, query.PageInfo.Count);
            Assert.Equal("synthetic-query-cookie", query.PageInfo.PagingCookie);
            Assert.True((bool)request["ReturnNotifications"]);
            Assert.Equal(empty ? 0 : 2, actual.Entities.Count);
            Assert.True(actual.MoreRecords);
            Assert.Equal("synthetic-cookie", actual.PagingCookie);
            Assert.Equal(2, actual.TotalRecordCount);
            sdk.Verify(value => value.Execute(request), Times.Once);
            sdk.VerifyNoOtherCalls();
        }
    }
}

```

## 現行 ToolUtility.Tests/DataOperations/AttributeExtensionSafetyTests.cs
```
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.Extensions;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 注入屬性轉字串故障、SDK 異常頁與並行 A/B 操作，保護固定安全診斷、原始例外及單次發布。
    /// 全部資料為合成值；替身僅替代外部 SDK I/O，不實作產品驗證、不連線或發送通知。
    /// </summary>
    public sealed class AttributeExtensionSafetyTests
    {
        /// <summary>沙箱可不建立 logger；舊簽章仍拒絕 null，不能以隱藏記錄物件維持相容。</summary>
        [Fact]
        public void AttributeConstructor_OffersStatelessPathAndPreservesLegacyNullRejection()
        {
            var service = Activator.CreateInstance<EntityAttributeUtilityService>();
            Assert.Equal("", service.GetAttributeValue(new Entity("contact"), "missing"));
            Assert.Throws<ArgumentNullException>(() => new EntityAttributeUtilityService(null!));
        }

        /// <summary>三入口的 null Entity 仍包裝真實引數例外，但外層訊息不能包含呼叫者的欄位文字。</summary>
        /// <param name="operation">固定方法路徑。</param>
        [Theory]
        [InlineData("read")]
        [InlineData("remove")]
        [InlineData("clear")]
        public void AttributeNullEntity_PreservesWrapperWithoutEchoingInput(string operation)
        {
            var service = new EntityAttributeUtilityService(new object());
            Entity entity = null!;
            var error = Assert.Throws<InvalidOperationException>(() =>
                Invoke(service, operation, ref entity, "synthetic_secret_property"));
            Assert.IsType<ArgumentNullException>(error.InnerException);
            Assert.DoesNotContain("synthetic_secret", error.Message);
        }

        /// <summary>寫入欄位空值保持既有包裝及內層型別；拒絕不改動原實體或其他欄位。</summary>
        /// <param name="operation">固定的移除或清空路徑。</param>
        /// <param name="property">故障注入的無效欄位名稱。</param>
        [Theory]
        [InlineData("remove", null)]
        [InlineData("remove", "")]
        [InlineData("remove", " ")]
        [InlineData("clear", null)]
        [InlineData("clear", "")]
        [InlineData("clear", " ")]
        public void AttributeInvalidProperty_RejectsWithoutMutation(string operation, string? property)
        {
            var service = new EntityAttributeUtilityService(new object());
            var entity = new Entity("synthetic_secret_entity") { ["synthetic_keep"] = "保留" };
            var error = Assert.Throws<InvalidOperationException>(() => Invoke(service, operation, ref entity, property!));
            Assert.IsType<ArgumentNullException>(error.InnerException);
            Assert.DoesNotContain("synthetic_secret", error.Message);
            Assert.Single(entity.Attributes);
            Assert.Equal("保留", entity["synthetic_keep"]);
        }

        /// <summary>
        /// 欄位物件的 ToString 真正失敗時，保留同一例外供 host 統一通知，外層不回顯資料／原 Message；
        /// caller 修復自己的欄位後可重試，服務不保存失敗值。
        /// </summary>
        /// <param name="alias">是否由 SDK 別名欄位包裝故障值。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AttributeFormattingFault_PreservesOriginalExceptionAndAllowsRetry(bool alias)
        {
            var failure = new FormatException("synthetic_secret_failure");
            var fault = new FaultingValue(failure);
            var entity = new Entity("synthetic_secret_entity")
            {
                ["synthetic_secret_property"] = alias ? new AliasedValue("contact", "fullname", fault) : fault
            };
            var service = new EntityAttributeUtilityService(new object());
            var error = Assert.Throws<InvalidOperationException>(() => service.GetAttributeValue(entity, "synthetic_secret_property"));
            Assert.Same(failure, error.InnerException);
            Assert.DoesNotContain("synthetic_secret", error.Message);
            entity["synthetic_secret_property"] = "已恢復";
            Assert.Equal("已恢復", service.GetAttributeValue(entity, "synthetic_secret_property"));
        }

        /// <summary>服務存活時，未使用的 logger 與已完成操作的 Entity 均必須可回收，不保留跨使用者狀態。</summary>
        [Fact]
        public void AttributeLifetime_DoesNotRetainLoggerOrEntity()
        {
            var service = CreateWithWeakInputs(out var logger, out var entity);
            for (var attempt = 0; attempt < 3 && (logger.IsAlive || entity.IsAlive); attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(logger.IsAlive);
            Assert.False(entity.IsAlive);
            GC.KeepAlive(service);
        }

        /// <summary>擴充入口的缺失參數與缺 Query 必須在 SDK I/O 前拒絕，不能偽裝成查無資料。</summary>
        [Fact]
        public void ExtensionInvalidArguments_RejectBeforeSdkIo()
        {
            var request = new RetrieveMultipleRequest { Query = new QueryExpression("contact") };
            Assert.Throws<ArgumentNullException>(() => OrganizationServiceExtensions.ExecuteRetrieveMultiple(null!, request));
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentNullException>(() => sdk.Object.ExecuteRetrieveMultiple(null!));
            Assert.Throws<ArgumentException>(() => sdk.Object.ExecuteRetrieveMultiple(new RetrieveMultipleRequest()));
            Assert.Throws<ArgumentException>(() => sdk.Object.ExecuteRetrieveMultiple(
                new RetrieveMultipleRequest { Query = new QueryExpression("") }));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 三種 query 共用既有根實體辨識：不改 query，DTD 與缺表在 I/O 前失敗。
        /// 防止 extension 自行複製 Fetch parser，形成可繞過的另一條入口。
        /// </summary>
        /// <param name="xml">合成不合法查詢，不含檔案、網址或真實資料。</param>
        [Theory]
        [InlineData("<fetch />")]
        [InlineData("<fetch><entity name='contact'/><entity name='account'/></fetch>")]
        [InlineData("<!DOCTYPE fetch [<!ENTITY sample 'synthetic'>]><fetch><entity name='contact'/></fetch>")]
        public void ExtensionInvalidFetch_RejectsBeforeSdkIo(string xml)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentException>(() => sdk.Object.ExecuteRetrieveMultiple(
                new RetrieveMultipleRequest { Query = new FetchExpression(xml) }));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// SDK 缺少／錯型回應不是合法空頁；兩個公開入口必須明確回固定完整性故障，
        /// 而不是 NullReference、InvalidCast 或安靜回傳空集合。
        /// </summary>
        /// <param name="shape">合成的異常回應形狀。</param>
        /// <param name="extension">是否走相容擴充入口。</param>
        [Theory]
        [InlineData("null", true)]
        [InlineData("wrong", true)]
        [InlineData("missing-page", true)]
        [InlineData("null", false)]
        [InlineData("wrong", false)]
        [InlineData("missing-page", false)]
        public void SdkResponseIntegrity_RejectsIncompleteResponse(string shape, bool extension)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            OrganizationResponse response = shape == "wrong" ? new OrganizationResponse()
                : shape == "null" ? null! : new RetrieveMultipleResponse();
            sdk.Setup(value => value.Execute(It.IsAny<RetrieveMultipleRequest>())).Returns(response);
            var request = new RetrieveMultipleRequest { Query = new QueryByAttribute("contact") };
            using var service = new EntityQueryService(organizationService: sdk.Object);
            var error = Assert.Throws<InvalidOperationException>(() => extension
                ? sdk.Object.ExecuteRetrieveMultiple(request) : service.ExecuteRetrieveMultiple(request));
            Assert.DoesNotContain("synthetic_secret", error.Message);
        }

        /// <summary>整頁穩定 ID 衝突、空 ID 與跨表記錄必須拒絕，不得刪除衝突列後部分發布。</summary>
        /// <param name="shape">故障列的形狀。</param>
        /// <param name="fetch">是否經 FetchXML 根實體辨識。</param>
        [Theory]
        [InlineData("duplicate", false)]
        [InlineData("empty", false)]
        [InlineData("wrong-entity", false)]
        [InlineData("duplicate", true)]
        [InlineData("empty", true)]
        [InlineData("wrong-entity", true)]
        public void ExtensionPublication_RejectsWholeInvalidPage(string shape, bool fetch)
        {
            var id = new Guid("11111111-1111-1111-1111-111111111111");
            var page = new EntityCollection();
            page.Entities.Add(new Entity("contact", id));
            page.Entities.Add(new Entity(shape == "wrong-entity" ? "account" : "contact",
                shape == "empty" ? Guid.Empty : shape == "duplicate" ? id : Guid.NewGuid()));
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(value => value.Execute(It.IsAny<RetrieveMultipleRequest>())).Returns(Response(page));
            var request = new RetrieveMultipleRequest
            {
                Query = fetch ? new FetchExpression("<fetch><entity name='contact'/></fetch>") : new QueryExpression("contact")
            };
            Assert.Throws<InvalidOperationException>(() => sdk.Object.ExecuteRetrieveMultiple(request));
            Assert.Equal(2, page.Entities.Count);
            sdk.Verify(value => value.Execute(request), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 原故障不重包、不吞掉；重試由 caller 明確發起，且 extension 絕不處置借入服務。</summary>
        [Fact]
        public void ExtensionSdkFault_PreservesExceptionRetryAndBorrowedOwnership()
        {
            var failure = new TimeoutException("synthetic_secret_transport");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            sdk.SetupSequence(value => value.Execute(It.IsAny<RetrieveMultipleRequest>()))
                .Throws(failure).Returns(Response(new EntityCollection()));
            var request = new RetrieveMultipleRequest { Query = new QueryExpression("contact") };
            Assert.Same(failure, Assert.Throws<TimeoutException>(() => sdk.Object.ExecuteRetrieveMultiple(request)));
            Assert.Empty(sdk.Object.ExecuteRetrieveMultiple(request).Entities);
            sdk.Verify(value => value.Execute(request), Times.Exactly(2));
            disposable.Verify(value => value.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 一千次有界 A/B 操作共用無狀態屬性服務，各自的 Entity、Query、回應及 SDK 始終獨立。
        /// Task 全數 await 後活動 SDK 計數回零、1000 次完整成功且借入 Dispose 為零。
        /// </summary>
        /// <returns>僅由測試擁有且排空的有限工作集合。</returns>
        [Fact]
        public async Task ConcurrentScopes_AttributeAndQueryDoNotCrossTalkAndDrain()
        {
            var active = 0;
            var completed = 0;
            var shared = new EntityAttributeUtilityService(new object());
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            for (var scope = 0; scope < clients.Length; scope++)
            {
                var expected = scope == 0 ? "scope-a" : "scope-b";
                clients[scope].As<IDisposable>();
                clients[scope].Setup(value => value.Execute(It.IsAny<RetrieveMultipleRequest>())).Returns((OrganizationRequest request) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var query = Assert.IsType<QueryByAttribute>(Assert.IsType<RetrieveMultipleRequest>(request).Query);
                        Assert.Equal(expected, Assert.Single(query.Values));
                        var entity = new Entity("contact", Guid.NewGuid()) { ["synthetic_scope"] = expected };
                        return Response(new EntityCollection(new[] { entity }));
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            }
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var scope = index % 2;
                var expected = scope == 0 ? "scope-a" : "scope-b";
                var query = new QueryByAttribute("contact");
                query.AddAttributeValue("synthetic_scope", expected);
                var page = clients[scope].Object.ExecuteRetrieveMultiple(new RetrieveMultipleRequest { Query = query });
                var entity = Assert.Single(page.Entities);
                Assert.Equal(expected, shared.GetAttributeValue(entity, "synthetic_scope"));
                shared.SetEntityAttributeToNull(ref entity, "synthetic_temporary");
                shared.RemoveAttribute(ref entity, "synthetic_temporary");
                Assert.False(entity.Contains("synthetic_temporary"));
                Assert.Equal(expected, entity["synthetic_scope"]);
                Interlocked.Increment(ref completed);
            })).ToArray();
            await Task.WhenAll(tasks);
            Assert.All(tasks, task => Assert.True(task.IsCompleted));
            Assert.Equal(1000, Volatile.Read(ref completed));
            Assert.Equal(0, Volatile.Read(ref active));
            foreach (var sdk in clients) sdk.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        }

        /// <summary>實際呼叫指定產品入口；不在測試 helper 重做欄位檢查、診斷或結果轉換。</summary>
        private static void Invoke(EntityAttributeUtilityService service, string operation, ref Entity entity, string property)
        {
            if (operation == "read") service.GetAttributeValue(entity, property);
            else if (operation == "remove") service.RemoveAttribute(ref entity, property);
            else service.SetEntityAttributeToNull(ref entity, property);
        }

        /// <summary>只建構 SDK 回應封包，驗證仍由真產品入口執行。</summary>
        private static RetrieveMultipleResponse Response(EntityCollection page)
        {
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = page;
            return response;
        }

        /// <summary>獨立堆疊避免 JIT 暫存強引用干擾回收斷言；服務只能持有本身無狀態實例。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static EntityAttributeUtilityService CreateWithWeakInputs(out WeakReference logger, out WeakReference entity)
        {
            var log = new object();
            var candidate = new Entity("contact") { ["synthetic_value"] = "完成後可回收" };
            var service = new EntityAttributeUtilityService(log);
            service.GetAttributeValue(candidate, "synthetic_value");
            logger = new WeakReference(log);
            entity = new WeakReference(candidate);
            return service;
        }

        /// <summary>只擁有本測試建立的例外，不配置資源；ToString 故障用於證明真例外不被字串取代。</summary>
        private sealed class FaultingValue
        {
            private readonly Exception _failure;

            /// <summary>保存本次 fault-injection 的原例外，生命週期僅限單一測試。</summary>
            internal FaultingValue(Exception failure) { _failure = failure; }

            /// <summary>依測試要求拋出同一例外；產品必須保留其身分且不回顯原訊息。</summary>
            public override string ToString() => throw _failure;
        }
    }
}

```

## 現行 ToolUtility.Core/Constants/CrmEntityColumns.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Core/Constants/CrmEntityColumns.cs
// 所屬區塊：供 net462 與 net10 共用的純 Dataverse 欄位結構目錄，不依賴產品 host。
// 檔案責任：集中保存各 CRM 實體查詢會使用的固定欄位名稱與順序，避免各呼叫端複製不同版本。
// 主要型別：CrmEntityColumns，以及 Contact、List、PresentRecord、WeeklyReport、DedicationBooking、Fee、StorLessons、Account、Task 九個分類。
// 主要成員：21 組公開欄位複本存取器；每次讀取都回傳新的 caller-owned 陣列，不共享可變陣列。
// 引用命名空間：System
// 閱讀路徑：依實體分類確認欄位 logical name；實際查詢、授權、連線與例外處理由各 host 負責。
// 維護重點：私有模板只保存有限的結構字串；不可直接公開模板，也不可依姓名、租戶或請求建立共享可變狀態。
// 行為保護：golden 測試鎖定 21 組欄位及其順序；修改欄位需先確認消費者契約並同步更新特性證據。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace ToolUtilityNameSpace.Constants
{
    /// <summary>
    /// 提供各 Dataverse 實體既有查詢使用的固定欄位集合；每組欄位名稱與順序都由 golden 契約保護。
    /// </summary>
    /// <remarks>
    /// 類別只保存有限的結構欄位名稱，不持有 CRM Entity、服務連線、使用者、教會或租戶資料。
    /// 私有靜態模板只供 getter 複製；每次 getter 都建立新的陣列交給單一呼叫端，
    /// 因此呼叫端可為 SDK 查詢調整自己的陣列，而不會污染同時或後續操作。
    /// 例如：<c>new ColumnSet(CrmEntityColumns.Contact.Basic)</c>。
    /// </remarks>
    public static class CrmEntityColumns
    {
        /// <summary>
        /// Contact (連絡人) 標準欄位
        /// </summary>
        public static class Contact
        {
            private static readonly string[] BasicColumns = {
                "contactid",
                "fullname",
                "mobilephone"
            };

            /// <summary>
            /// 基本欄位 (最常用)
            /// 用於: 列表顯示、基本資訊
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "contactid",
                "fullname",
                "mobilephone",
                "telephone2",          // 住家電話
                "emailaddress1",       // 電子郵件
                "address2_line1",      // 地址
                "customertypecode",    // 委身類型
                "gendercode",          // 性別
                "birthdate"            // 生日
            };

            /// <summary>
            /// 擴展欄位 (一般使用)
            /// 用於: 詳細資訊、編輯頁面
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();

            private static readonly string[] FullColumns = {
                "contactid",
                "fullname",
                "lastname",
                "mobilephone",
                "telephone2",
                "company",             // 公司電話
                "emailaddress1",
                "address2_line1",
                "customertypecode",
                "gendercode",
                "birthdate",
                "familystatuscode",    // 婚姻狀態
                "new_spiriitual_identity", // 信仰狀態
                "new_enter_church_date",   // 進教會日期
                "new_industry",        // 職業
                "new_fb_account",      // Facebook
                "new_ig_account",      // Instagram
                "new_personal_id",     // 身分證字號
                "new_last_six_digit",  // 銀行帳戶後六碼
                "new_ntbt_ornot",      // 是否上傳國稅局
                "description",         // 描述
                "new_invitor",         // 邀請人
                "new_carers",          // 邀請人關係
                "new_cell_list_contact", // 主要小組
                "new_race_leader_contact", // 族系組長
                "parentcustomerid"     // 所屬教會
            };

            /// <summary>
            /// 完整欄位 (詳細資訊)
            /// 用於: 完整編輯、匯出
            /// </summary>
            public static string[] Full => (string[])FullColumns.Clone();

            private static readonly string[] FollowUpColumns = {
                "contactid",
                "fullname",
                "mobilephone",
                "customertypecode",
                "new_enter_church_date",
                "new_start_tracking_date", // 開始關懷日期
                "description",
                "gendercode"
            };

            /// <summary>
            /// 新人跟進欄位
            /// 用於: 新人管理、跟進記錄
            /// </summary>
            public static string[] FollowUp => (string[])FollowUpColumns.Clone();
        }

        /// <summary>
        /// List (名單) 標準欄位
        /// </summary>
        public static class List
        {
            private static readonly string[] BasicColumns = {
                "listid",
                "listname",
                "purpose"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "listid",
                "listname",
                "purpose",
                "createdfromcode",
                "type",                    // 靜態/動態
                "new_app_named",           // APP點名
                "new_contact_family_leader_list",  // 小組長
                "new_contact_race_leager_list",    // 族系組長
                "new_familyhead_list",     // 小家長
                "statuscode",
                "statecode"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// new_present_record (出席記錄) 標準欄位
        /// </summary>
        public static class PresentRecord
        {
            private static readonly string[] BasicColumns = {
                "new_present_recordid",
                "new_contact_new_present_record",  // 聯絡人
                "new_sunday_date",                 // 主日日期
                "new_sunday_present_this_week",    // 主日出席
                "new_group_present_this_week"      // 小組出席
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "new_present_recordid",
                "new_contact_new_present_record",
                "new_sunday_date",
                "new_group_date",
                "new_sunday_present_this_week",
                "new_group_present_this_week",
                "new_sunday_rate",            // 主日出席率
                "new_small_group_rate",       // 小組出席率
                "new_list_new_present_record", // 名單
                "new_groupleader_present_record", // 小組長
                "new_explanation",            // 附註
                "new_cell_hpone"              // 手機
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();

            private static readonly string[] FollowUpColumns = {
                "new_present_recordid",
                "new_contact_new_present_record",
                "new_sunday_date",
                "new_weeks",              // 週次
                "new_follow_up",          // 跟進方式
                "new_conclusion_choise",  // 跟進結果
                "new_next_step",          // 下一步驟
                "new_explanation",        // 跟進描述
                "new_groupleader_present_record"
            };

            /// <summary>
            /// 新人跟進欄位
            /// </summary>
            public static string[] FollowUp => (string[])FollowUpColumns.Clone();
        }

        /// <summary>
        /// new_group_present_weekly_report (週報) 標準欄位
        /// </summary>
        public static class WeeklyReport
        {
            private static readonly string[] BasicColumns = {
                "new_group_present_weekly_reportid",
                "new_name",
                "new_sunday_date",
                "new_list_group_present_weekly_report"  // 名單
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "new_group_present_weekly_reportid",
                "new_name",
                "new_sunday_date",
                "new_list_group_present_weekly_report",
                "new_small_group_place",     // 小組地點
                "new_small_group_time",      // 小組時間
                "new_sunday_present_rate",   // 主日出席率
                "new_group_present_rate",    // 小組出席率
                "new_sunday_present_number", // 主日出席人數
                "new_group_present_number",  // 小組出席人數
                "createdon",
                "statecode"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// new_dedication_booking (奉獻預約) 標準欄位
        /// </summary>
        public static class DedicationBooking
        {
            private static readonly string[] BasicColumns = {
                "new_dedication_bookingid",
                "new_name",
                "new_contact_new_dedication_booking",  // 聯絡人
                "new_dedication_booking_status",       // 狀態
                "createdon"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "new_dedication_bookingid",
                "new_name",
                "new_contact_new_dedication_booking",
                "new_dedication_booking_status",
                "new_dedication_type",          // 奉獻類型
                "new_amount",                   // 金額
                "new_start_date",               // 開始日期
                "new_end_date",                 // 結束日期
                "createdon",
                "statecode"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// new_fee (費用) 標準欄位
        /// </summary>
        public static class Fee
        {
            private static readonly string[] BasicColumns = {
                "new_feeid",
                "new_name",
                "new_dedication_booking_new_fee",  // 奉獻預約
                "new_paid_period",                 // 繳費期間
                "createdon"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "new_feeid",
                "new_name",
                "new_dedication_booking_new_fee",
                "new_paid_period",
                "new_amount",           // 金額
                "new_payment_status",   // 付款狀態
                "new_payment_date",     // 付款日期
                "new_payment_method",   // 付款方式
                "createdon",
                "statecode"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// new_stor_lessons (課程記錄) 標準欄位
        /// </summary>
        public static class StorLessons
        {
            private static readonly string[] BasicColumns = {
                "new_stor_lessonsid",
                "new_contact_new_stor_lessons",       // 聯絡人
                "new_new_disciple_lessons_new_stor_les", // 課程
                "createdon"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "new_stor_lessonsid",
                "new_contact_new_stor_lessons",
                "new_new_disciple_lessons_new_stor_les",
                "new_fee",              // 費用
                "new_pay_date",         // 付款日期
                "new_current_complete", // 完成狀態
                "new_enroll_status",    // 報名狀態
                "createdon",
                "statuscode",
                "statecode"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// Account (教會) 標準欄位
        /// </summary>
        public static class Account
        {
            private static readonly string[] BasicColumns = {
                "accountid",
                "name"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "accountid",
                "name",
                "telephone1",
                "address1_line1",
                "emailaddress1"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }

        /// <summary>
        /// Task (工作) 標準欄位
        /// </summary>
        public static class Task
        {
            private static readonly string[] BasicColumns = {
                "activityid",
                "subject",
                "statecode"
            };

            /// <summary>
            /// 基本欄位
            /// </summary>
            public static string[] Basic => (string[])BasicColumns.Clone();

            private static readonly string[] ExtendedColumns = {
                "activityid",
                "subject",
                "description",
                "scheduledend",
                "prioritycode",
                "regardingobjectid",
                "createdby",
                "statecode",
                "createdon"
            };

            /// <summary>
            /// 擴展欄位
            /// </summary>
            public static string[] Extended => (string[])ExtendedColumns.Clone();
        }
    }
}

```

## 現行 ToolUtility.Tests/DataOperations/ColumnContractTests.cs
```
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Constants;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 鎖住21組既有欄位名稱及順序，再注入 caller 修改以證明不會污染下一個使用者的查詢。
    /// 只檢查結構名稱，不含業務資料；反射同時支援舊 field 與新 property 的來源相容搬移。
    /// </summary>
    public sealed class ColumnContractTests
    {
        /// <summary>把實際所有欄位定義完整投影後與搬前黃金檔比對，缺欄、改序、新增或刪除組都不能靜默通過。</summary>
        [Fact]
        public void ColumnsCharacterization_AllDefinitions_PreserveExactOrder()
        {
            var root = new XElement("columnSets");
            foreach (var type in typeof(CrmEntityColumns).GetNestedTypes().OrderBy(t => t.Name, StringComparer.Ordinal))
                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m is FieldInfo || m is PropertyInfo).OrderBy(m => m.Name, StringComparer.Ordinal))
                    root.Add(new XElement("set", new XAttribute("type", type.Name), new XAttribute("name", member.Name),
                        Read(type.Name, member.Name).Select(column => new XElement("column", column))));
            Assert.Equal(21, root.Elements().Count());
            GoldenQueryAssertions.Equal("columns-all.xml", root.ToString());
        }

        /// <summary>
        /// 修改操作A的陣列後，操作B必須仍取得原內容與不同陣列。舊版共享欄位的紅燈在 finally 還原，
        /// 不讓重現測試本身污染同程序其他案例；新版只還原caller獨占複本。
        /// </summary>
        /// <param name="type">固定的結構分類，不是動態租戶值。</param>
        /// <param name="member">固定的欄位集合名稱。</param>
        [Theory]
        [InlineData("Contact", "Basic")]
        [InlineData("Contact", "Extended")]
        [InlineData("Contact", "Full")]
        [InlineData("Contact", "FollowUp")]
        [InlineData("List", "Basic")]
        [InlineData("List", "Extended")]
        [InlineData("PresentRecord", "Basic")]
        [InlineData("PresentRecord", "Extended")]
        [InlineData("PresentRecord", "FollowUp")]
        [InlineData("WeeklyReport", "Basic")]
        [InlineData("WeeklyReport", "Extended")]
        [InlineData("DedicationBooking", "Basic")]
        [InlineData("DedicationBooking", "Extended")]
        [InlineData("Fee", "Basic")]
        [InlineData("Fee", "Extended")]
        [InlineData("StorLessons", "Basic")]
        [InlineData("StorLessons", "Extended")]
        [InlineData("Account", "Basic")]
        [InlineData("Account", "Extended")]
        [InlineData("Task", "Basic")]
        [InlineData("Task", "Extended")]
        public void ColumnsSafety_CallerMutation_DoesNotCrossBoundary(string type, string member)
        {
            var first = Read(type, member);
            var original = first[0];
            try
            {
                first[0] = "synthetic_caller_mutation";
                var second = Read(type, member);
                Assert.NotSame(first, second);
                Assert.Equal(original, second[0]);
            }
            finally { first[0] = original; }
        }

        /// <summary>一千個獨立操作同時讀改基本欄位；陣列只在外層測試Task內存活，全部await後不得留下污染。</summary>
        /// <returns>全部操作排空的Task；產品本身不建立任何非同步工作。</returns>
        [Fact]
        public async Task ColumnsSafety_ConcurrentCopies_RemainIndependent()
        {
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var columns = CrmEntityColumns.Contact.Basic;
                Assert.Equal("contactid", columns[0]);
                columns[0] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            })).ToArray();
            try
            {
                await Task.WhenAll(tasks);
                Assert.All(tasks, task => Assert.True(task.IsCompleted));
                Assert.Equal("contactid", CrmEntityColumns.Contact.Basic[0]);
            }
            finally { CrmEntityColumns.Contact.Basic[0] = "contactid"; }
        }

        /// <summary>只讀指定公開成員的實際陣列；不複製或過濾，否則會把產品的共享陣列缺陷藏掉。</summary>
        private static string[] Read(string typeName, string member)
        {
            var type = typeof(CrmEntityColumns).GetNestedType(typeName);
            Assert.NotNull(type);
            var value = type!.GetField(member)?.GetValue(null) ?? type.GetProperty(member)?.GetValue(null);
            return Assert.IsType<string[]>(value);
        }
    }
}

```

## 現行 ToolUtility.Tests/Golden/ToolUtility/columns-all.xml
```
<columnSets><set type="Account" name="Basic"><column>accountid</column><column>name</column></set><set type="Account" name="Extended"><column>accountid</column><column>name</column><column>telephone1</column><column>address1_line1</column><column>emailaddress1</column></set><set type="Contact" name="Basic"><column>contactid</column><column>fullname</column><column>mobilephone</column></set><set type="Contact" name="Extended"><column>contactid</column><column>fullname</column><column>mobilephone</column><column>telephone2</column><column>emailaddress1</column><column>address2_line1</column><column>customertypecode</column><column>gendercode</column><column>birthdate</column></set><set type="Contact" name="FollowUp"><column>contactid</column><column>fullname</column><column>mobilephone</column><column>customertypecode</column><column>new_enter_church_date</column><column>new_start_tracking_date</column><column>description</column><column>gendercode</column></set><set type="Contact" name="Full"><column>contactid</column><column>fullname</column><column>lastname</column><column>mobilephone</column><column>telephone2</column><column>company</column><column>emailaddress1</column><column>address2_line1</column><column>customertypecode</column><column>gendercode</column><column>birthdate</column><column>familystatuscode</column><column>new_spiriitual_identity</column><column>new_enter_church_date</column><column>new_industry</column><column>new_fb_account</column><column>new_ig_account</column><column>new_personal_id</column><column>new_last_six_digit</column><column>new_ntbt_ornot</column><column>description</column><column>new_invitor</column><column>new_carers</column><column>new_cell_list_contact</column><column>new_race_leader_contact</column><column>parentcustomerid</column></set><set type="DedicationBooking" name="Basic"><column>new_dedication_bookingid</column><column>new_name</column><column>new_contact_new_dedication_booking</column><column>new_dedication_booking_status</column><column>createdon</column></set><set type="DedicationBooking" name="Extended"><column>new_dedication_bookingid</column><column>new_name</column><column>new_contact_new_dedication_booking</column><column>new_dedication_booking_status</column><column>new_dedication_type</column><column>new_amount</column><column>new_start_date</column><column>new_end_date</column><column>createdon</column><column>statecode</column></set><set type="Fee" name="Basic"><column>new_feeid</column><column>new_name</column><column>new_dedication_booking_new_fee</column><column>new_paid_period</column><column>createdon</column></set><set type="Fee" name="Extended"><column>new_feeid</column><column>new_name</column><column>new_dedication_booking_new_fee</column><column>new_paid_period</column><column>new_amount</column><column>new_payment_status</column><column>new_payment_date</column><column>new_payment_method</column><column>createdon</column><column>statecode</column></set><set type="List" name="Basic"><column>listid</column><column>listname</column><column>purpose</column></set><set type="List" name="Extended"><column>listid</column><column>listname</column><column>purpose</column><column>createdfromcode</column><column>type</column><column>new_app_named</column><column>new_contact_family_leader_list</column><column>new_contact_race_leager_list</column><column>new_familyhead_list</column><column>statuscode</column><column>statecode</column></set><set type="PresentRecord" name="Basic"><column>new_present_recordid</column><column>new_contact_new_present_record</column><column>new_sunday_date</column><column>new_sunday_present_this_week</column><column>new_group_present_this_week</column></set><set type="PresentRecord" name="Extended"><column>new_present_recordid</column><column>new_contact_new_present_record</column><column>new_sunday_date</column><column>new_group_date</column><column>new_sunday_present_this_week</column><column>new_group_present_this_week</column><column>new_sunday_rate</column><column>new_small_group_rate</column><column>new_list_new_present_record</column><column>new_groupleader_present_record</column><column>new_explanation</column><column>new_cell_hpone</column></set><set type="PresentRecord" name="FollowUp"><column>new_present_recordid</column><column>new_contact_new_present_record</column><column>new_sunday_date</column><column>new_weeks</column><column>new_follow_up</column><column>new_conclusion_choise</column><column>new_next_step</column><column>new_explanation</column><column>new_groupleader_present_record</column></set><set type="StorLessons" name="Basic"><column>new_stor_lessonsid</column><column>new_contact_new_stor_lessons</column><column>new_new_disciple_lessons_new_stor_les</column><column>createdon</column></set><set type="StorLessons" name="Extended"><column>new_stor_lessonsid</column><column>new_contact_new_stor_lessons</column><column>new_new_disciple_lessons_new_stor_les</column><column>new_fee</column><column>new_pay_date</column><column>new_current_complete</column><column>new_enroll_status</column><column>createdon</column><column>statuscode</column><column>statecode</column></set><set type="Task" name="Basic"><column>activityid</column><column>subject</column><column>statecode</column></set><set type="Task" name="Extended"><column>activityid</column><column>subject</column><column>description</column><column>scheduledend</column><column>prioritycode</column><column>regardingobjectid</column><column>createdby</column><column>statecode</column><column>createdon</column></set><set type="WeeklyReport" name="Basic"><column>new_group_present_weekly_reportid</column><column>new_name</column><column>new_sunday_date</column><column>new_list_group_present_weekly_report</column></set><set type="WeeklyReport" name="Extended"><column>new_group_present_weekly_reportid</column><column>new_name</column><column>new_sunday_date</column><column>new_list_group_present_weekly_report</column><column>new_small_group_place</column><column>new_small_group_time</column><column>new_sunday_present_rate</column><column>new_group_present_rate</column><column>new_sunday_present_number</column><column>new_group_present_number</column><column>createdon</column><column>statecode</column></set></columnSets>

```

## 現行 ToolUtility.Core/EntityOperations/EntityCrudService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 純 SDK 單次寫入轉接器；只借用 host 已驗證組織、使用者及授權範圍的服務，不建立連線或共用資料。
    /// 輸入與輸出只在同步呼叫存活，不保存 Entity、logger、Session、token 或 callback。
    /// </summary>
    /// <remarks>
    /// 實例最長與 SDK 原授權 scope 同生命週期，禁止跨租戶 singleton；SDK 的逾時與 Dispose 仍由 host 擁有。
    /// 不隱藏重試或包裝 SDK 例外，真實故障交回 host 的共用通知邊界。
    /// 此底層 API 不推測業務事件鍵；caller 必須先以資料庫唯一鍵／替代鍵確保跨程序冪等，不以顯示名稱去重。
    /// </remarks>
    public class EntityCrudService : IEntityCrudService
    {
        private readonly IOrganizationService _organizationService;

        /// <summary>保留舊建構簽章及 null 拒絕，不保存未使用 logger，避免延長 request／憑證的生命。</summary>
        /// <param name="logger">來源相容非 null 參數，不使用、保存或 Dispose。</param>
        /// <param name="organizationService">host 擁有且已授權的 SDK；不可在同步呼叫中途關閉。</param>
        public EntityCrudService(object logger, IOrganizationService organizationService)
        {
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>只執行一次 Create，驗證回傳非空資料庫 ID 後才回報成功；不修改 caller 的 Entity 或自行重試。</summary>
        /// <param name="entityToCreate">操作獨占的已授權候選，可未預先指定 ID；呼叫中不能由其他執行緒修改。</param>
        /// <returns>SDK 確認的非空資料庫 ID。</returns>
        /// <exception cref="InvalidOperationException">SDK 未提供有效 ID；寫入狀態不確定，須以原冪等鍵查證，不可盲目新增。</exception>
        public Guid CreateEntity(Entity entityToCreate)
        {
            var candidate = entityToCreate ?? throw new ArgumentNullException(nameof(entityToCreate));
            RequireName(candidate.LogicalName, nameof(entityToCreate));
            var identity = _organizationService.Create(candidate);
            if (identity == Guid.Empty) throw new InvalidOperationException("The SDK returned no created database identity.");
            return identity;
        }

        /// <summary>以既有資料庫 ID 或伺服器替代索引鍵更新一次；缺少兩者即在 I/O 前拒絕。</summary>
        /// <param name="entityToUpdate">已授權、操作獨占的候選；屬性、ID、KeyAttributes 完整交給 SDK。</param>
        public void UpdateEntity(Entity entityToUpdate)
        {
            var candidate = entityToUpdate ?? throw new ArgumentNullException(nameof(entityToUpdate));
            RequireName(candidate.LogicalName, nameof(entityToUpdate));
            if (candidate.Id == Guid.Empty && candidate.KeyAttributes.Count == 0)
                throw new ArgumentException("A database identity or server alternate key is required.", nameof(entityToUpdate));
            _organizationService.Update(candidate);
        }

        /// <summary>刪除 caller 已授權的精確非空 ID；不查詢、合併或刪除同名資料。</summary>
        /// <param name="entityName">伺服器決定的實體 logical name；不是授權依據。</param>
        /// <param name="entityId">已驗證屬於當前操作範圍的非空資料庫 ID。</param>
        public void DeleteEntity(string entityName, Guid entityId)
        {
            if (string.IsNullOrEmpty(entityName))
                throw new ArgumentNullException(nameof(entityName));
            RequireName(entityName, nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("A database identity is required.", nameof(entityId));
            _organizationService.Delete(entityName, entityId);
        }

        /// <summary>型別化寫入不拼 XML；只拒絕缺少名稱，合法名稱與權限仍須由 SDK metadata／host 驗證。</summary>
        private static void RequireName(string name, string parameter)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An entity name is required.", parameter);
        }
    }
}

```

## 現行 ToolUtility.Core/OwnerOperations/OwnerManagementService.cs
```
using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.OwnerOperations
{
    /// <summary>
    /// 以原 SDK ownerid／AssignRequest 處理已授權的單筆資料；不保存實體、logger 或使用者可變狀態。
    /// 實例限於 SDK 原授權 scope；借入連線、憑證與 Dispose 由 host 擁有，不建立背景工作。
    /// </summary>
    /// <remarks>
    /// 缺省值不是故障；型別錯誤與真正 SDK 失敗直接上傳 host 共用通知入口。
    /// 不把實體名稱、ID 或原始 Exception.Message 拼成新訊息，也不 catch 吞掉失敗。
    /// 指派不是授權機制，caller 須先驗證當前組織、目標記錄與受派者權限。
    /// </remarks>
    public class OwnerManagementService : IOwnerManagementService
    {
        private readonly IOrganizationService _organizationService;

        /// <summary>保留舊建構及 null 拒絕；未用 logger 不保存，SDK 僅借用且不在服務內 Dispose。</summary>
        /// <param name="logger">來源相容非 null 參數，不使用或持有。</param>
        /// <param name="organizationService">已授權的 caller-owned SDK，須存活至所有同步操作返回。</param>
        public OwnerManagementService(object logger, IOrganizationService organizationService)
        {
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>只讀操作獨占實體的 ownerid；缺值沿用 Guid.Empty，不呼叫 SDK 或保存 EntityReference。</summary>
        /// <param name="entity">已授權非 null 實體，呼叫中不可並行修改。</param>
        /// <returns>原 owner 資料庫 ID，或缺省的 Guid.Empty。</returns>
        public Guid GetOwnerId(Entity entity)
        {
            var candidate = entity ?? throw new ArgumentNullException(nameof(entity));
            return candidate.GetAttributeValue<EntityReference>("ownerid")?.Id ?? Guid.Empty;
        }

        /// <summary>回傳既有 owner 顯示名稱或空字串；名稱不是 ID、授權或跨記錄去重鍵。</summary>
        /// <param name="entity">已授權的操作獨占實體，不保存其屬性。</param>
        /// <returns>僅供本次 caller 顯示的名稱，不寫入診斷或共用快取。</returns>
        public string GetOwnerName(Entity entity)
        {
            var candidate = entity ?? throw new ArgumentNullException(nameof(entity));
            return candidate.GetAttributeValue<EntityReference>("ownerid")?.Name ?? string.Empty;
        }

        /// <summary>
        /// 保留 systemuser 指派語義；目標實體一致、兩個 ID 非空才 Execute 一次。
        /// 原例外直接交 caller，不包裝、回顯個資或重試寫入，也不修改 caller 的 Entity。
        /// </summary>
        /// <param name="entityName">須與 entity.LogicalName 完全一致的伺服器實體名稱。</param>
        /// <param name="entity">已授權的持久化記錄，不使用暫存 draft 身分。</param>
        /// <param name="ownerId">同組織內已授權的 systemuser 資料庫 ID。</param>
        public void AssignOwner(string entityName, Entity entity, Guid ownerId)
        {
            var candidate = entity ?? throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentNullException(nameof(entityName));
            if (!string.Equals(entityName, candidate.LogicalName, StringComparison.Ordinal))
                throw new ArgumentException("The target entity name is missing or inconsistent.", nameof(entityName));
            if (candidate.Id == Guid.Empty) throw new ArgumentException("A database identity is required.", nameof(entity));
            if (ownerId == Guid.Empty) throw new ArgumentException("An owner database identity is required.", nameof(ownerId));
            var assign = new AssignRequest
            {
                Assignee = new EntityReference("systemuser", ownerId),
                Target = new EntityReference(entityName, candidate.Id)
            };
            _organizationService.Execute(assign);
        }
    }
}

```

## 現行 ToolUtility.Tests/DataOperations/MutationOperationsSafetyTests.cs
```
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.OwnerOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 保護純 SDK 寫入的識別、故障與引用生命週期；故障在記憶體注入，不實際修改 D365 或帳號權限。
    /// 各案例使用獨立服務；正常業務授權與跨程序唯一鍵仍是 host／資料庫責任，不能以測試假裝已整合。
    /// </summary>
    public sealed class MutationOperationsSafetyTests
    {
        /// <summary>保留兩服務的舊 null 建構拒絕；不因移除 logger 欄位就放寬錯誤組態。</summary>
        /// <param name="owner">是否測負責人服務。</param>
        /// <param name="missingService">是否缺 SDK，否則缺 logger。</param>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Constructor_MissingDependency_Rejects(bool owner, bool missingService)
        {
            var sdk = missingService ? null : new Mock<IOrganizationService>().Object;
            var logger = missingService ? new object() : null;
            Assert.Throws<ArgumentNullException>(() =>
                owner ? (object)new OwnerManagementService(logger!, sdk!) : new EntityCrudService(logger!, sdk!));
        }

        /// <summary>無合法目標的操作必須在 I/O 前拒絕；Strict SDK 證明沒有送出任何部分寫入。</summary>
        /// <param name="operation">要拒絕的合成錯誤。</param>
        [Theory]
        [InlineData("create-null")]
        [InlineData("create-name")]
        [InlineData("update-null")]
        [InlineData("update-id")]
        [InlineData("update-name")]
        [InlineData("delete-name")]
        [InlineData("delete-empty")]
        [InlineData("delete-id")]
        [InlineData("assign-null")]
        [InlineData("assign-id")]
        [InlineData("assign-owner")]
        [InlineData("assign-name")]
        [InlineData("assign-empty")]
        [InlineData("assign-mismatch")]
        public void InvalidIdentity_RejectsBeforeSdk(string operation)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var crud = new EntityCrudService(new object(), sdk.Object);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            var entity = new Entity("contact", Guid.NewGuid());
            Action action = operation switch
            {
                "create-null" => () => crud.CreateEntity(null!),
                "create-name" => () => crud.CreateEntity(new Entity(" ")),
                "update-null" => () => crud.UpdateEntity(null!),
                "update-id" => () => crud.UpdateEntity(new Entity("contact")),
                "update-name" => () => crud.UpdateEntity(new Entity(" ", entity.Id)),
                "delete-name" => () => crud.DeleteEntity(" ", entity.Id),
                "delete-empty" => () => crud.DeleteEntity("", entity.Id),
                "delete-id" => () => crud.DeleteEntity("contact", Guid.Empty),
                "assign-null" => () => owner.AssignOwner("contact", null!, Guid.NewGuid()),
                "assign-id" => () => owner.AssignOwner("contact", new Entity("contact"), Guid.NewGuid()),
                "assign-owner" => () => owner.AssignOwner("contact", entity, Guid.Empty),
                "assign-name" => () => owner.AssignOwner(" ", entity, Guid.NewGuid()),
                "assign-empty" => () => owner.AssignOwner("", entity, Guid.NewGuid()),
                "assign-mismatch" => () => owner.AssignOwner("account", entity, Guid.NewGuid()),
                _ => throw new ArgumentException("Unknown test scenario.", nameof(operation))
            };
            if (operation == "delete-empty" || operation == "assign-empty")
                Assert.Throws<ArgumentNullException>(action);
            else
                Assert.ThrowsAny<ArgumentException>(action);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 空字串保留舊版 ArgumentNullException；純空白名稱則新增 fail-closed 拒絕。
        /// 嚴格 SDK 替身在本機注入兩種輸入，並以精確例外型別及零呼叫保護相容差異。
        /// </summary>
        [Fact]
        public void Delete_NameValidation_PreservesEmptyExceptionAndRejectsWhitespace()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var crud = new EntityCrudService(new object(), sdk.Object);

            var emptyName = Assert.Throws<ArgumentNullException>(() =>
                crud.DeleteEntity(string.Empty, Guid.NewGuid()));
            Assert.Equal("entityName", emptyName.ParamName);
            Assert.Throws<ArgumentException>(() => crud.DeleteEntity(" ", Guid.NewGuid()));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 真的故障必須把同一 Exception 傳回共用 host 邊界，不能吞掉、重試重複寫入或包成含個資訊息。</summary>
        /// <param name="operation">四個會寫入 SDK 的入口。</param>
        [Theory]
        [InlineData("create")]
        [InlineData("update")]
        [InlineData("delete")]
        [InlineData("assign")]
        public void SdkFault_PreservesOriginalExceptionAndDoesNotRetry(string operation)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var borrowed = sdk.As<IDisposable>();
            var fault = new TimeoutException("synthetic-private-fault");
            var entity = new Entity("contact", Guid.NewGuid());
            sdk.Setup(s => s.Create(entity)).Throws(fault);
            sdk.Setup(s => s.Update(entity)).Throws(fault);
            sdk.Setup(s => s.Delete("contact", entity.Id)).Throws(fault);
            sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>())).Throws(fault);
            var crud = new EntityCrudService(new object(), sdk.Object);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            Action action = operation switch
            {
                "create" => () => crud.CreateEntity(entity),
                "update" => () => crud.UpdateEntity(entity),
                "delete" => () => crud.DeleteEntity("contact", entity.Id),
                "assign" => () => owner.AssignOwner("contact", entity, Guid.NewGuid()),
                _ => throw new ArgumentException("Unknown test scenario.", nameof(operation))
            };
            Assert.Same(fault, Record.Exception(action));
            Assert.Single(sdk.Invocations);
            borrowed.Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>空 Create 結果不是成功資料庫身分；不得把它交給 caller 當已完成發布或自行重試。</summary>
        [Fact]
        public void Create_EmptySdkIdentity_RejectsOnce()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns(Guid.Empty);
            Assert.Throws<InvalidOperationException>(() =>
                new EntityCrudService(new object(), sdk.Object).CreateEntity(new Entity("contact")));
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 支援的伺服器替代索引鍵更新仍合法，不能為 GUID 守門破壞既有 alternate-key API。</summary>
        [Fact]
        public void Update_ServerAlternateKey_PreservesSdkContract()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact", "new_serverkey", "synthetic-event");
            sdk.Setup(s => s.Update(entity));
            new EntityCrudService(new object(), sdk.Object).UpdateEntity(entity);
            sdk.Verify(s => s.Update(entity), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>唯讀 getter 的 null 是輸入拒絕，不應包成含實體文字的功能故障或呼叫 SDK。</summary>
        [Fact]
        public void Owner_NullEntity_RejectsWithoutSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            Assert.Throws<ArgumentNullException>(() => owner.GetOwnerId(null!));
            Assert.Throws<ArgumentNullException>(() => owner.GetOwnerName(null!));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>未用的 legacy logger 不可被任一服務延長保存；無內嵌／no-inline 工廠排除測試區域變數根參照。</summary>
        /// <param name="owner">是否測試負責人服務。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LegacyLogger_IsNotRetained(bool owner)
        {
            var sdk = new Mock<IOrganizationService>().Object;
            var holder = CreateService(owner, sdk, out var logger);
            for (var count = 0; count < 3 && logger.IsAlive; count++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(logger.IsAlive);
            GC.KeepAlive(holder);
        }

        /// <summary>
        /// 兩個 scope 一千次並行寫入同名但不同 ID；每次都驗正確 SDK、完整參數與結果。
        /// Task 全部 await 後計數固定1000，借入 SDK Dispose 為0；服務不保存 entity 或產生內部工作。
        /// </summary>
        /// <returns>全部外層有界測試工作完成後的 Task。</returns>
        [Fact]
        public async Task ConcurrentScopes_SameDisplayNames_NoCrossTalkAndDrain()
        {
            var count = 0;
            var assignments = 0;
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            var services = new EntityCrudService[2];
            var owners = new OwnerManagementService[2];
            for (var index = 0; index < clients.Length; index++)
            {
                var id = ids[index];
                clients[index].As<IDisposable>();
                clients[index].Setup(s => s.Create(It.IsAny<Entity>())).Returns((Entity entity) =>
                {
                    Assert.Equal(id, entity.Id);
                    Assert.Equal("合成同名", entity.GetAttributeValue<string>("fullname"));
                    Interlocked.Increment(ref count);
                    return id;
                });
                clients[index].Setup(s => s.Execute(It.IsAny<AssignRequest>())).Returns((OrganizationRequest request) =>
                {
                    var assign = Assert.IsType<AssignRequest>(request);
                    Assert.Equal(id, assign.Target.Id);
                    Assert.Equal(id, assign.Assignee.Id);
                    Assert.Equal("contact", assign.Target.LogicalName);
                    Assert.Equal("systemuser", assign.Assignee.LogicalName);
                    Interlocked.Increment(ref assignments);
                    return new AssignResponse();
                });
                services[index] = new EntityCrudService(new object(), clients[index].Object);
                owners[index] = new OwnerManagementService(new object(), clients[index].Object);
            }
            var tasks = new Task[1000];
            for (var index = 0; index < tasks.Length; index++)
            {
                var scope = index % 2;
                tasks[index] = Task.Run(() =>
                {
                    var entity = new Entity("contact", ids[scope]) { ["fullname"] = "合成同名" };
                    Assert.Equal(ids[scope], services[scope].CreateEntity(entity));
                    owners[scope].AssignOwner("contact", entity, ids[scope]);
                });
            }
            await Task.WhenAll(tasks);
            Assert.All(tasks, task => Assert.True(task.IsCompleted));
            Assert.Equal(1000, Volatile.Read(ref count));
            Assert.Equal(1000, Volatile.Read(ref assignments));
            foreach (var client in clients) client.As<IDisposable>().Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>只把弱引用交回測試，服務可存活但無理由保留的 logger 必須可回收；不建立外部資源。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object CreateService(bool owner, IOrganizationService sdk, out WeakReference logger)
        {
            var candidate = new object();
            logger = new WeakReference(candidate);
            return owner ? (object)new OwnerManagementService(candidate, sdk) : new EntityCrudService(candidate, sdk);
        }
    }
}

```

## 現行 ToolUtility.Core/ToolUtility.Core.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net462;net10.0</TargetFrameworks>
    <RootNamespace>ToolUtilityNameSpace</RootNamespace>
    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <!-- Framework SDK 傳遞引用 6.0.8，直接使用中央安全修補版；net10 則使用內建 JSON。 -->
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 僅取得 SDK 型別；純資料核心不建立 ServiceClient 或擁有連線。 -->
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 舊 SDK 帶入的傳遞版本有已知弱點；使用中央表的安全版本，維持完整 audit。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
  </ItemGroup>
</Project>

```

## 現行 ToolUtility.Tests/ToolUtility.Tests.csproj
```
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
  </PropertyGroup>

  <ItemGroup>
    <!-- 測試框架 -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>

    <!-- Mock 框架 -->
    <PackageReference Include="Moq" />

    <!-- 斷言庫 -->
    <PackageReference Include="FluentAssertions" />

    <!-- 程式碼覆蓋率 -->
    <PackageReference Include="coverlet.collector">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.msbuild">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 專案參考 -->
    <ProjectReference Include="..\ToolUtility\ToolUtility.csproj" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ToolUtility.Core\ToolUtility.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- CRM SDK 套件（用於測試） -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
    <!-- 既有 host 測試完整保留在 net10；Framework 宿主只編譯純 Core 契約，不引用 net10 host。 -->
    <Compile Remove="**\*.cs" />
    <Compile Include="FeeOperations\*.cs" />
    <Compile Include="EntityOperations\EntityQueryCharacterizationTests.cs" />
    <Compile Include="EntityOperations\EntityQueryContractTests.cs" />
    <Compile Include="DataOperations\*.cs" />
    <Compile Include="TestHelpers\GoldenQueryAssertions*.cs" />
    <Compile Include="CoreTargetFrameworkTests.cs" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 測試直接消費 SDK，需同樣以中央安全版本覆蓋其舊傳遞相依。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
    <PackageReference Include="System.Security.Permissions" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- 黃金查詢是合成資料，內嵌後兩個測試宿主均不依賴執行目錄或網路。 -->
    <EmbeddedResource Include="Golden\ToolUtility\*.xml"
                      LogicalName="ToolUtility.Tests.Golden.ToolUtility.%(Filename)%(Extension)" />
  </ItemGroup>

</Project>

```

## 現行 wp-reports/P1-04-attribute-extension.md
```
# P1-04 屬性工具與 SDK 相容入口抽取

日期：2026-11-05。本報告只涵蓋此切片，**不代表 P1-04 或 P1 完成**。

## 正式需求對照

| 需求 | 實作 | 驗證與證據 |
|---|---|---|
| 純 SDK 屬性操作雙目標、維持 namespace | `ToolUtility.Core/AttributeOperations/EntityAttributeUtilityService.cs`、`Interfaces/IEntityAttributeUtilityService.cs` | 先在原 host 的 12 個特性案例通過，再修安全故障、搬移；後補 null 名稱及完整 Query 分頁斷言 |
| 純 SDK extension 可由 Plugin／網站共用 | `ToolUtility.Core/Extensions/OrganizationServiceExtensions.cs` | 原簽章不變，委派現有 `EntityQueryService`，不複製 Fetch parser／穩定 ID 驗證 |
| SDK 回應完整性、整頁識別與原始故障 | `ToolUtility.Core/EntityOperations/EntityQueryService.cs` | null／錯型回應明確拒絕；既有 QueryByAttribute／QueryExpression／FetchXML 共用根表及整頁驗證 |
| 不保留無用 logger／Entity、不跨操作共享 | 屬性服務無欄位、extension 僅操作局部查詢器 | 弱引用回收、1,000 次 A/B 並行、SDK 活動數排空、借入 Dispose=0 |
| Framework 實際載入核定 net462 產品 | `ToolUtility.Tests/CoreTargetFrameworkTests.cs` | 反射三個新抽取型別及 Constants 均屬 Core；net48 只引用 Core，新增測試在既有 `DataOperations/*.cs` 白名單內 |

## 行為與相容性裁決

- 三個屬性方法簽章、原 Entity 就地更新、普通／一層 `AliasedValue` 轉文字、真正缺值回空字串保持不變。`GetAttributeValue` 的 nullable 欄位名稱註記描述既有 null 行為，未改 CLR 簽章。
- `RemoveAttribute` 是移除屬性；`SetEntityAttributeToNull` 即使原本缺欄也保存明確 null。這兩種 SDK 更新語義不能互換。
- 保留舊 `InvalidOperationException` 外層型別與真實 `InnerException`。只把原本回顯欄位、實體及 `Exception.Message` 的外層訊息改為固定文字；host 不得序列化／記錄原 Message 或 ToString。
- 保留舊建構式對 null logger 的拒絕，但不保存 logger；新增無參數建構式讓純資料工具無須配置主機診斷依賴。
- SDK extension 的 null SDK／request／Query 以及異常 response 以前可被當成空資料，現在明確失敗。限定目前 repo 的 C# 搜尋沒有找到依賴該 extension 的產品呼叫點；同名測試原來呼叫的是 `EntityQueryService` instance method，不能混為同一 API。repo 外部消費者未知，故仍明載此安全差異。
- extension 每次建立一個小型局部 `EntityQueryService` 並確定性 Dispose，以重用完整公開查詢入口；Dispose 只清借入 SDK 參照，不 Dispose 原連線。沒有新增 parser、cache、連線、重試或通知器，也沒有為內部 helper 開放 `InternalsVisibleTo`。
- 實體與 Query 只屬當次已授權操作；同一 Entity 的並行寫入仍須 caller owner 協調。通用工具不把 Entity／Query 名稱當授權，也不推測租戶條件。

## 分析發現與核實

CCG 自癒入口 run `20261105-181935-integration-p1-04-attribute-extension-analysis-analyzer`：
`dualModelComplete=true`、`quotaBlocked=false`、`degradedFallback=false`。

- 採納兩模型的未使用 logger、固定安全外層訊息、缺參數不可偽裝空結果及借入所有權建議。
- Claude 的 Critical「保留舊 wrapper 必定違反 Core」不成立：沒有跨產品契約禁止這種相容包裝，既有 `EntityCrudService`／Owner 也保留 logger null 拒絕，不能從另一類型的 optional 建構式推導所有 API 必須改行為。已用精確型別／InnerException 測試及產品 remarks 明載理由。
- 「ToString 不會失敗」屬錯誤前提；測試以真實欄位物件注入 `FormatException`，證明原例外必須保存。
- 分析提示的 `MutationContractTests.cs` 路徑不存在，正確範本為 `MutationOperationsCharacterizationTests.cs`／`MutationOperationsSafetyTests.cs`，主會話已讀實檔。
- net48 測試遺漏提醒已核實：新測試刻意放進既有 `DataOperations/*.cs`，無須擴大或修改白名單；完整 net48 執行數與 target 反射提供實際證據。
- 不從 host 呼叫 internal `SdkDataQuery`，而委派 Core 公開查詢器後搬移 extension；因此沒有跨 assembly 可見性缺口。

## 紅綠與輸出新鮮度

- 原 host 成功特性：`artifacts/p1-04-attribute-extension-before/before.trx`，12/12。
- 安全故障重現：`artifacts/p1-04-attribute-extension-red/red.trx`，31 案中 28 失敗／3 通過，沒有編譯錯誤或略過。失敗包含 logger 保留、外層訊息回顯、缺失輸入／回應及資料列拒絕缺口。
- 修正後搬前，連同既有 EntityQuery 契約：`artifacts/p1-04-attribute-extension-safe-before-move/`，81/81；通過後才執行三檔 `git mv`。
- 起始 `--no-build` 偶然載到舊 244／349 案輸出，**不採為現況驗收**。重新建置後 270／350、Constants 後 coverage 99.33%／100%；記錄保留於 `artifacts/p1-04-resume-columns-*`。
- 抽取後補測的最終雙宿主／覆蓋率在 `artifacts/p1-04-attribute-extension-final-{dual,coverage}/`；正式數字由 TRX counters 及恰好一份每目標 coverage 原檔核對，不挑選較佳結果。

## 審查及剩餘界線

完整增量 reviewer 尚待完成，須包含此切片、Constants 及前輪 mutation 相容性補證。
SDK／主機回歸、00 §8 及 changed-scope 編碼／duplicates 必須在本地提交前執行。
本輪不修改正式計畫，不需 Word 修正報告；不操作 Hyper-V／D365、不推播、不 push。
P1 仍為 2/12；其他候選與 P1-05～12 保持未完成。

```
