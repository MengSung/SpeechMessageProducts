/*
 * 檔案整體說明
 *
 * 目的與定位：定義 Plugin 外殼使用的防遞迴政策純值，讓每次 Execute 只依本次
 * Dataverse execution context 判斷是否跳過，不保存跨請求、跨使用者或跨組織狀態。
 *
 * 主要功能與流程：DepthPolicy 保存最大深度及舊流程允許的例外條件；ShouldSkip
 * 只讀取 MessageName、Depth 與最大深度，回傳是否停止規則執行。此檔不建立
 * SDK service、Task、timer、cache 或背景工作，資源生命週期完全由呼叫端掌握。
 *
 * 使用邊界與維護：政策只屬註冊步驟的不可變設定，不能被輸入資料或規則中途改寫。
 * 測試應以合成 execution context 覆蓋最大深度、Create 例外與 Update 例外；
 * 真實 D365 行為由部署驗收另行證明。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Plugins.Infrastructure;

/// <summary>允許超過最大深度的既有流程例外。</summary>
internal enum DepthException
{
    /// <summary>任何訊息超過最大深度就停止。</summary>
    None,
    /// <summary>Create 不受深度限制；其他訊息超過最大深度停止。</summary>
    Create,
    /// <summary>僅 Update 超過最大深度停止；其他訊息不受限制。</summary>
    Update
}

/// <summary>不可變的防遞迴政策；不持有執行內容或服務。</summary>
internal sealed class DepthPolicy
{
    /// <summary>建立指定最大深度與單一例外條件的政策。</summary>
    /// <param name="maximumDepth">允許的最大深度，必須為正數。</param>
    /// <param name="exception">最大深度時可放行的訊息。</param>
    public DepthPolicy(int maximumDepth, DepthException exception = DepthException.None)
    {
        if (maximumDepth < 1) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        MaximumDepth = maximumDepth;
        Exception = exception;
    }

    /// <summary>超過此深度時才套用停止條件，等於門檻仍執行。</summary>
    public int MaximumDepth { get; }

    /// <summary>最大深度的單一訊息例外。</summary>
    public DepthException Exception { get; }

    /// <summary>判斷本次 context 是否應跳過規則。</summary>
    /// <param name="context">本次執行的 Dataverse context，不能保存。</param>
    public bool ShouldSkip(IPluginExecutionContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (context.Depth <= MaximumDepth) return false;

        var isException = Exception switch
        {
            DepthException.Create => string.Equals(context.MessageName, "Create", StringComparison.Ordinal),
            DepthException.Update => !string.Equals(context.MessageName, "Update", StringComparison.Ordinal),
            _ => false
        };
        return !isException;
    }
}
