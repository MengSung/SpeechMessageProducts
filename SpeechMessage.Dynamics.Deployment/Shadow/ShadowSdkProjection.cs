/*
 * 檔案整體說明
 *
 * 目的與定位：將 Deployment observe 已取得的 SDK Entity 轉為安全 ShadowRow，
 * 直接使用編入本專案的 Plugin recorder 唯一來源，避免維護第二份隱私欄位表。
 * 主要功能與流程：每次 Project 建立獨占 recorder，只呼叫 Update 記錄安全意圖，
 * 驗證恰有一筆且沒有 overflow，再解析 ProjectionJson 的 fields 並 Clone 成獨立值。
 * 使用邊界與維護：上游負責授權、組織隔離與 observe 查詢，本檔不取得連線或秘密。
 * 私有拒絕服務封鎖全部 SDK 讀寫；來源容量與欄位規則完全沿用 recorder，
 * 無正式 ID、未知型別或超量都失敗，不發布部分列。原始 SDK 資料僅於同步呼叫內
 * 存在，recorder 與 JSON 文件由方法 using 確定排空釋放，輸出不保留兩者參照。
 * 每次呼叫獨占可變狀態，可供不同使用者平行使用；沒有快取、背景工作或取消註冊。
 * 離線測試驗證敏感欄位、精度、容量及來源突變隔離，不宣稱實機驗收。
 */
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Infrastructure;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>
/// observe 的純記憶體安全投影入口；借用同源 recorder 演算法，但完全禁止其觸及外部服務。
/// 可變記錄器與解析文件只屬一次呼叫，回傳資料以 Clone 截斷資源及原始物件參照。
/// </summary>
internal static class ShadowSdkProjection
{
    /// <summary>
    /// 將具有正式資料庫身分的 Entity 投影為與 Plugin 寫入意圖相同的安全欄位值。
    /// Update 僅記錄意圖；欄位省略、雜湊、深複製及容量檢查由唯一 recorder 來源執行。
    /// 任何失敗都由 using 排空，不保留跨呼叫資料，也不回傳部分結果。
    /// </summary>
    /// <param name="row">上游完成授權及精確欄位查詢的資料列，不允許未存檔 draft 身分。</param>
    /// <returns>保留實體、正式 ID 與精確 JSON 型別的獨立安全快照。</returns>
    public static ShadowRow Project(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using var recorder = new RecordingOrganizationService(new RejectingOrganizationService());
        recorder.Update(row);
        var writes = recorder.Writes;
        if (recorder.Overflow || recorder.TotalCount != 1 || writes.Count != 1)
            throw new InvalidDataException("Invalid shadow SDK projection.");
        var write = writes[0];
        using var document = JsonDocument.Parse(write.ProjectionJson);
        var fields = document.RootElement.GetProperty("fields");
        if (fields.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid shadow SDK projection.");
        return new ShadowRow(write.LogicalName, write.Id.ToString("D", CultureInfo.InvariantCulture), fields.Clone());
    }

    /// <summary>
    /// 每次投影建立的無狀態防線；八個 SDK 入口均同步拒絕，不保存輸入或建立任何資源。
    /// 即使未來 recorder 增加讀取路徑，也只能失敗而不能穿透至真實 Dataverse。
    /// </summary>
    private sealed class RejectingOrganizationService : IOrganizationService
    {
        /// <summary>拒絕真實建立；所有建立意圖必須停留在 recorder 記憶體範圍。</summary>
        public Guid Create(Entity entity) => throw Rejected();

        /// <summary>拒絕真實更新；正常投影只會由 recorder 攔截並記錄。</summary>
        public void Update(Entity entity) => throw Rejected();

        /// <summary>拒絕真實刪除，防止純投影入口改變外部狀態。</summary>
        public void Delete(string entityName, Guid id) => throw Rejected();

        /// <summary>拒絕讀取單列；observe 查詢及授權必須在上游完成。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw Rejected();

        /// <summary>拒絕集合查詢，不允許投影隱含增加資料範圍。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw Rejected();

        /// <summary>拒絕所有訊息，包含看似唯讀訊息，維持零外部呼叫契約。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw Rejected();

        /// <summary>拒絕真實關聯，不取得或保留任何服務參照。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw Rejected();

        /// <summary>拒絕解除關聯，不允許安全投影產生真實寫入。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw Rejected();

        /// <summary>固定錯誤不回顯 SDK 輸入、身分或秘密。</summary>
        private static InvalidOperationException Rejected() => new("Shadow projection cannot call an organization service.");
    }
}
