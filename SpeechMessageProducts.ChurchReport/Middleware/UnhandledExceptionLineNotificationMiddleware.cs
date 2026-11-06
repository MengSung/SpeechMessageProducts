/*
 * 檔案整體說明
 *
 * 目的與定位：HTTP 最終失敗轉接器；必須位於 UseExceptionHandler 的內側，先落檔與排入通知再 rethrow。 只持有組合根 owner 與管線委派；所有 request 狀態僅存在本次 
 * InvokeAsync，不流入背景工作。
 *
 * 主要功能與流程：
 * 本檔宣告 UnhandledExceptionLineNotificationMiddleware。
 * - DI 注入借用的診斷 owner，不由 middleware 釋放。
 * - 正常 client 取消不報，逾時與真正故障先落檔；永遠保留同一個原始例外。
 * 主要操作入口包含 InvokeAsync。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/ExceptionPipelineTests.cs、
 * SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ToolUtilityNameSpace.Diagnostics;

namespace ChurchReport.Middleware;

/// <summary>
/// HTTP 最終失敗轉接器；必須位於 UseExceptionHandler 的內側，先落檔與排入通知再 rethrow。
/// 只持有組合根 owner 與管線委派；所有 request 狀態僅存在本次 InvokeAsync，不流入背景工作。
/// </summary>
public sealed class UnhandledExceptionLineNotificationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ExceptionDiagnostics _diagnostics;

    /// <summary>DI 注入借用的診斷 owner，不由 middleware 釋放。</summary>
    public UnhandledExceptionLineNotificationMiddleware(RequestDelegate next, ExceptionDiagnostics diagnostics)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>正常 client 取消不報，逾時與真正故障先落檔；永遠保留同一個原始例外。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context).ConfigureAwait(false); }
        catch (Exception exception)
        {
            _diagnostics.Report(exception, "Http.UnhandledException", context.RequestAborted);
            throw;
        }
    }
}
