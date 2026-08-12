// ============================================================================
// 檔案：ChurchReport/Services/FeeEditorReadService.cs
// 目的：在 server-authorized lesson locator 已驗證後，呼叫唯一 Package01 fee-editor operation 並建立 immutable DTO response。
//
// 安全與生命週期契約：
// - profile 與 workload 固定來自 server composition；browser 不能提供 connector、endpoint、credential、owner 或 operation。
// - 每個 upstream row 必須精確 match target；任何 null/mismatch/fault/cancellation 不發佈 partial response，也不走 legacy fallback/retry。
// - service 不保存 HttpContext、Session、FeeList、CRM Entity、cache、client lease、timer 或背景 task；executor/client 的資源仍由既有 DI owner 清理。
// - 取消 token 原樣傳遞；OperationCanceledException 不在此類別轉譯，避免已終止 request 留下非確定性資源或 response。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Models;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Models;

namespace ChurchReport.Services;

/// <summary>
/// `fees.editor.load.by.disciplelesson` 的 request-local typed coordinator。
///
/// 此 service 不執行授權決策：controller 必須先用 server-derived lesson snapshot 驗證 target。service
/// 接著以 deployment-owned profile 與固定 workload 送出唯一 operation，完整 materialize 後驗證每列 target，
/// 最後才建立新的 <see cref="FeeEditorReadResult"/>。因此 caller 不能用 GUID 選 profile 或繞過權限，且
/// timeout、fault 或 cancellation 不能將 DTO、partial list 或 legacy Entity 保留給下一個 request。
/// </summary>
public sealed class FeeEditorReadService
{
    /// <summary>產品端固定使用的 server-owned workload subject，不由 route、query 或 body 接收。</summary>
    private const string WorkloadSubjectId = "church-report-service";

    /// <summary>由 composition root 提供的 stateless typed client；其 transport/lease lifecycle 不屬於本 service。</summary>
    private readonly IPackage01FeeReadClient _package01Client;

    /// <summary>server-bound Dynamics options；只讀 ProfileAlias，從不保存或輸出 secret/endpoint。</summary>
    private readonly ProductDynamicsOptions _dynamicsOptions;

    /// <summary>
    /// 建立只依賴 server composition 的 typed coordinator。建構式不發送 I/O、不建立 provider、client、
    /// timer 或 cache；client 與 options 必須已由 DI/controlled composition 建立，避免 per-request runtime graph。
    /// </summary>
    /// <param name="package01Client">既有 Package01 fee-read client；service 不 Dispose 此 shared facade。</param>
    /// <param name="dynamicsOptions">已由 deployment configuration bind 的 options；只有非空 profile 可 dispatch。</param>
    public FeeEditorReadService(
        IPackage01FeeReadClient package01Client,
        IOptions<ProductDynamicsOptions> dynamicsOptions)
    {
        _package01Client = package01Client ?? throw new ArgumentNullException(nameof(package01Client));
        _dynamicsOptions = dynamicsOptions?.Value ?? throw new ArgumentNullException(nameof(dynamicsOptions));
    }

    /// <summary>
    /// 使用精確 Package01 operation 讀取已授權 lesson 的唯讀 rows。
    ///
    /// target 必須由 controller 在本方法前完成 session-derived authorization。方法不接受 lesson name、
    /// caller profile 或 owner，避免查詢意義在 request-time 擴張。上游資料全部成功且 target 一致時才回傳 result；
    /// 任一 mismatch/null 會拋出固定例外，而 cancellation/fault 依原樣傳回，沒有 fallback、retry 或 partial publish。
    /// </summary>
    /// <param name="discipleLessonId">已由目前 request 的 server lesson allowlist 驗證的精確 target。</param>
    /// <param name="cancellationToken">HTTP request 的取消 token；原樣傳遞至唯一 typed operation。</param>
    /// <returns>新的 immutable scalar response，不含 CRM SDK graph 或可變 session model。</returns>
    public async Task<FeeEditorReadResult> RetrieveAsync(
        Guid discipleLessonId,
        CancellationToken cancellationToken = default)
    {
        if (discipleLessonId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty authorized disciple lesson ID is required.", nameof(discipleLessonId));
        }

        var profileAlias = RequireProfileAlias();
        IReadOnlyList<StorLessonRecordDto> sourceRows = await _package01Client
            .RetrieveFeeEditorRowsByDiscipleLessonAsync(
                profileAlias,
                WorkloadSubjectId,
                discipleLessonId,
                cancellationToken)
            .ConfigureAwait(false);

        if (sourceRows is null)
        {
            throw new InvalidOperationException("The fee editor response was incomplete.");
        }

        var rows = new List<FeeEditorReadRow>(sourceRows.Count);
        foreach (var sourceRow in sourceRows)
        {
            if (sourceRow is null || sourceRow.DiscipleLessonId != discipleLessonId)
            {
                throw new InvalidOperationException("The fee editor response did not match the authorized lesson.");
            }

            rows.Add(new FeeEditorReadRow
            {
                StorLessonId = sourceRow.StorLessonId,
                ContactId = sourceRow.ContactId,
                DiscipleLessonId = discipleLessonId,
                CreatedOn = sourceRow.CreatedOn,
                PayDate = sourceRow.PayDate,
                CurrentComplete = sourceRow.CurrentComplete,
                ContactName = sourceRow.ContactName,
                ContactMobile = sourceRow.ContactMobile,
                DiscipleLessonName = sourceRow.DiscipleLessonName,
                ClassStartDate = sourceRow.ClassStartDate,
                StageName = sourceRow.StageName,
                FeeAmount = sourceRow.FeeAmount
            });
        }

        return new FeeEditorReadResult(rows);
    }

    /// <summary>
    /// 取得 deployment-owned profile alias。空白 profile 在任何 outbound dispatch 前 fail closed；不得以
    /// lesson ID、session account、legacy CrmConnection、endpoint 或測試預設值猜測替代，避免跨 profile
    /// 或跨 organization routing。回傳字串只被目前 operation stack 使用，不保存 credential 或 client state。
    /// </summary>
    /// <returns>可安全交給既有 typed client 的 server profile alias。</returns>
    private string RequireProfileAlias()
    {
        if (string.IsNullOrWhiteSpace(_dynamicsOptions.ProfileAlias))
        {
            throw new InvalidOperationException(
                "DynamicsAccess:ProfileAlias is required for the fee editor read boundary.");
        }

        return _dynamicsOptions.ProfileAlias;
    }
}
