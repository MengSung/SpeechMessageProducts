/*
 * 檔案整體說明
 * 目的與定位：P1-12 在唯一測試組織驗證真正 alternate key 的受限業務資料探測，仍由 Deployment 命令單一入口執行。
 * 主要功能與流程：借用本命令 SDK、固定合成文字及專屬證據目錄，建立只屬本次的測試教會與文字覆寫，再確認第二筆相同鍵被資料庫拒絕。
 * 信任邊界：CLI 先確認已發布結構及全部 key Active，探測本身不修改舊記錄、權限或結構，也不發 LINE。
 * D3659.1通用DuplicateRecord不能只按錯誤碼接受：須證明原三欄tuple僅第一筆、第二ID未寫入，且同鍵不同文化控制列可真實建立及讀回。
 * 擁有權：每筆候選先選唯一本地 GUID 並在 Create 前確認不存在；正式建立回應須與該資料庫 ID 一致。識別與標記先 flush 到操作紀錄。
 * 清理與故障：即使 Create 回應不確定、取消或故障，也只依本次 ID 查回且核對標記，再依文字→教會順序清理；不能刪除同名或其他資料。
 * 沒有共享快取、背景排程或跨使用者狀態；SDK 由外層租約擁有，最後同步呼叫返回才歸還。
 * 驗證方式：離線測試注入唯一鍵故障、非預期故障、取消、Create 後失去回應與清理失敗；實機結果另保存，不把替身當真索引證據。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>正式P1-12合成輸入，只有固定文字鍵、文化與無個資測試文字；由嚴格JSON入口驗證，不接受任意表名或欄位。</summary>
/// <param name="Key">固定Org.Level.Group。</param><param name="Culture">固定全文化星號。</param><param name="Text">驗收計畫指定的合成文字，最長2000。</param>
internal sealed record SchemaAcceptanceFixture(string Key, string Culture, string Text);

/// <summary>單次受限驗收探測，沒有跨命令狀態；借用SDK，唯一擁有本次合成資料及操作紀錄。</summary>
internal static class SchemaAcceptanceProbe
{
    /// <summary>建立本次合成資料、檢查正式重複鍵拒絕並確定清理，任何未預期故障保留給共用通知入口。</summary>
    /// <param name="service">外層唯一命令租約的SDK。</param><param name="directory">Host固定操作目錄。</param>
    /// <param name="text">正式驗收fixture的文字，不是使用者個資。</param><param name="cancellationToken">caller取消；清理不能被同一token跳過。</param>
    internal static void Run(IOrganizationService service, string directory, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new InvalidDataException("Acceptance text is outside its fixed fixture budget.");
        cancellationToken.ThrowIfCancellationRequested();
        var marker = "P1-Acceptance-" + Guid.NewGuid().ToString("N");
        var account = new Entity("account", Guid.NewGuid()) { ["name"] = marker };
        var first = TextRow(Guid.NewGuid());
        var duplicate = TextRow(Guid.NewGuid());
        var cultureControl = TextRow(Guid.NewGuid());
        cultureControl["new_culture"] = "zh-TW";
        var candidates = new[] { account, first, duplicate, cultureControl };
        var attempted = new bool[candidates.Length];
        foreach (var candidate in candidates)
            if (Read(candidate) is not null) throw new InvalidDataException("Acceptance candidate identity already exists.");
        _ = SchemaFiles.WriteNew(directory, "acceptance-ownership.json", new
        {
            Organization = "goodshepherdback", Marker = marker, AccountId = account.Id,
            FirstTextId = first.Id, DuplicateTextId = duplicate.Id, CultureControlTextId = cultureControl.Id
        });
        Exception? failure = null;
        var cleanupFailures = new List<Exception>();
        var duplicateCode = "";
        try
        {
            // 前三筆是原本驗收；第四筆只供9.1通用SQL錯誤的差分控制，仍須事先記錄身份並共用finally清理。
            for (var index = 0; index < 3; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = candidates[index];
                attempted[index] = true;
                try
                {
                    var id = service.Create(candidate);
                    if (id != candidate.Id || id == Guid.Empty) throw new InvalidDataException("Acceptance Create did not confirm the expected database identity.");
                    cancellationToken.ThrowIfCancellationRequested();
                    if (index == 2) throw new InvalidOperationException("Duplicate alternate key was accepted by the server.");
                }
                catch (FaultException<OrganizationServiceFault> exception) when (index == 2)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (exception.Detail.ErrorCode == unchecked((int)0x80060892)) duplicateCode = "0x80060892";
                    else if (exception.Detail.ErrorCode == unchecked((int)0x80040237))
                    {
                        // 地端9.1將真正唯一鍵違規包成DuplicateRecord，SQL本文可能隱藏。
                        // 不解析／記錄本文，也不直接接受通用碼；須完成同tuple唯一與不同文化可寫的服務端控制實驗。
                        try { ConfirmLegacyDuplicate(); duplicateCode = "0x80040237"; }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                        catch (Exception evidenceFailure) { throw new AggregateException("Legacy duplicate verification failed.", exception, evidenceFailure); }
                    }
                    else throw;
                }
            }
            var persisted = Read(first) ?? throw new InvalidDataException("Acceptance record was not persisted.");
            if (persisted.GetAttributeValue<EntityReference>("new_accountid")?.Id != account.Id
                || persisted.GetAttributeValue<string>("new_key") != "Org.Level.Group"
                || persisted.GetAttributeValue<string>("new_culture") != "*"
                || persisted.GetAttributeValue<string>("new_text") != text)
                throw new InvalidDataException("Acceptance record did not preserve the fixture values.");
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            // 所有候選的ID先落檔，即使Create已提交但回應逾時，也可精確查回；不使用名稱查詢或全表刪除。
            // 清理不用已取消caller token，且逐筆保留故障，避免第一筆失敗阻止其他自身資料的清理嘗試。
            for (var index = candidates.Length - 1; index >= 0; index--)
            {
                if (!attempted[index]) continue;
                try
                {
                    Cleanup(candidates[index]);
                }
                catch (Exception exception) { cleanupFailures.Add(exception); }
            }
        }
        if (cleanupFailures.Count != 0)
        {
            if (failure is not null) cleanupFailures.Insert(0, failure);
            throw new AggregateException("Acceptance cleanup failed; retain the ownership ledger for scoped recovery.", cleanupFailures);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        _ = SchemaFiles.WriteNew(directory, "acceptance-result.json", new
        {
            DuplicateStableKeyRejected = true, DuplicateErrorCode = duplicateCode, TestRowsRemaining = 0,
            LegacyIntegrityDifferentialPassed = duplicateCode == "0x80040237",
            FixturePreserved = true, AccountId = account.Id, FirstTextId = first.Id
        });

        // 區域候選只含合成文字、固定key及本次教會；借用的SDK沒有被候選或輸出長期保留。
        Entity TextRow(Guid id) => new("new_localizedtext", id)
        {
            ["new_name"] = marker, ["new_accountid"] = new EntityReference("account", account.Id),
            ["new_key"] = "Org.Level.Group", ["new_culture"] = "*", ["new_text"] = text
        };

        // 所有控制驗證均在同一已驗Active key的租約與寫鎖內；只用本次account及固定key，最多兩列，不碰其他教會。
        // 若同樣輸入僅換culture也不能建立、第二ID實際存在或原tuple不是唯一第一筆，通用SQL故障仍須整案拒絕。
        void ConfirmLegacyDuplicate()
        {
            if (Read(account) is null || Read(duplicate) is not null) throw new InvalidDataException("Legacy rejection did not preserve the expected identities.");
            ConfirmTuple();
            cancellationToken.ThrowIfCancellationRequested();
            attempted[3] = true;
            if (service.Create(cultureControl) != cultureControl.Id) throw new InvalidDataException("Legacy control Create identity is invalid.");
            cancellationToken.ThrowIfCancellationRequested();
            var persistedControl = Read(cultureControl) ?? throw new InvalidDataException("Legacy control was not persisted.");
            if (persistedControl.GetAttributeValue<EntityReference>("new_accountid")?.Id != account.Id
                || persistedControl.GetAttributeValue<string>("new_key") != "Org.Level.Group"
                || persistedControl.GetAttributeValue<string>("new_culture") != "zh-TW"
                || persistedControl.GetAttributeValue<string>("new_text") != text)
                throw new InvalidDataException("Legacy control values do not match the differential fixture.");
            ConfirmTuple();
        }

        // 必須恰為原第一個正式ID及完整三欄；不以顯示名Distinct或Top1掩蓋重複，也不將空結果當故障已被證明。
        void ConfirmTuple()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new QueryExpression("new_localizedtext") { TopCount = 2, ColumnSet = new ColumnSet("new_name", "new_accountid", "new_key", "new_culture") };
            query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, account.Id);
            query.Criteria.AddCondition("new_key", ConditionOperator.Equal, "Org.Level.Group");
            query.Criteria.AddCondition("new_culture", ConditionOperator.Equal, "*");
            var rows = service.RetrieveMultiple(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (rows is null || rows.MoreRecords || rows.Entities.Count != 1) throw new InvalidDataException("Legacy duplicate tuple is not a unique complete result.");
            var row = rows.Entities[0];
            if (row is null || row.LogicalName != "new_localizedtext" || row.Id != first.Id
                || row.GetAttributeValue<string>("new_name") != marker
                || row.GetAttributeValue<EntityReference>("new_accountid")?.Id != account.Id
                || row.GetAttributeValue<string>("new_key") != "Org.Level.Group" || row.GetAttributeValue<string>("new_culture") != "*")
                throw new InvalidDataException("Legacy duplicate tuple identity or scope is invalid.");
        }

        // 清理作業不在finally直接throw；外層逐筆收集原故障後才聚合，不能遮蔽最初故障。
        void Cleanup(Entity candidate)
        {
            if (Read(candidate) is not null) service.Delete(candidate.LogicalName, candidate.Id);
            if (Read(candidate) is not null) throw new InvalidDataException("Acceptance record remains after cleanup.");
        }

        // 精確ID、有界最多兩列且驗原始標記；任何身分不符皆拒絕，不能按相同顯示名刪其他記錄。
        Entity? Read(Entity candidate)
        {
            var name = candidate.LogicalName == "account" ? "name" : "new_name";
            var query = new QueryExpression(candidate.LogicalName)
            {
                TopCount = 2, ColumnSet = candidate.LogicalName == "account" ? new ColumnSet(name)
                    : new ColumnSet(name, "new_accountid", "new_key", "new_culture", "new_text")
            };
            query.Criteria.AddCondition(candidate.LogicalName + "id", ConditionOperator.Equal, candidate.Id);
            var result = service.RetrieveMultiple(query);
            if (result is null || result.MoreRecords || result.Entities.Count > 1)
                throw new InvalidDataException("Acceptance identity lookup is incomplete or duplicated.");
            if (result.Entities.Count == 0) return null;
            var row = result.Entities[0];
            if (row is null || row.Id != candidate.Id || row.LogicalName != candidate.LogicalName
                || row.GetAttributeValue<string>(name) != marker)
                throw new InvalidDataException("Acceptance record ownership does not match the ledger.");
            return row;
        }
    }
}
