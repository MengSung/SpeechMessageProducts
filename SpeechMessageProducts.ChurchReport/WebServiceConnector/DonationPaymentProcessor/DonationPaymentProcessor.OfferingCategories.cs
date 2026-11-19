/*
 * 檔案整體說明
 *
 * 目的與定位：提供 ChurchReport 奉獻付款處理器的奉獻類別伺服器端解析流程，
 * 將已驗證登入 contact 的 parentcustomerid 作為唯一教會範圍，並呼叫 GoodShepherd.Core
 * 取得一次性清單快照。表單提供的 Category 僅是正式 Guid 輸入，不包含授權或舊選項權威。
 * 主要流程：多筆付款先將全部 Category 嚴格解析成 Guid，再以一份教會清單整批驗證 ID、
 * 啟用狀態與必填 LegacyOptionValue；全部成功才把不可變類別值附在此次操作的明細上。
 * 單筆 KeyIn 與認獻路徑採相同解析規則。任何失敗都在 CRM Create 前拋出，不輸出部分候選。
 * 使用邊界與維護：contact 與清單只在該次同步呼叫／付款操作中存活，不寫入 Session、靜態欄位
 * 或共享快取；IOrganizationService 由既有 ToolUtility 呼叫端持有，本檔不 Dispose。
 * 併行請求各自讀取和保存自己的區域快照；沒有背景工作、timer 或額外取消註冊。
 * 失敗與驗證：空／格式錯誤／重複／未知／停用／跨教會／缺舊值都 fail closed；純 resolver
 * 合成測試驗證整批拒絕與欄位映射，產品建置及 ChurchReport 測試另行驗證接線。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
using ChurchReport.Services;
using Microsoft.Xrm.Sdk;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;

namespace ChurchReport.WebServiceConnector;

/// <summary>ChurchReport 奉獻付款處理器之類別讀取與操作區域驗證協助方法。</summary>
internal partial class DonationPaymentProcessor
{
    /// <summary>
    /// 從伺服器提供的登入 contact 讀取一次清單，並原子驗證多筆表單類別 ID。
    /// </summary>
    /// <param name="loginContact">由 ChurchReport 登入流程解析的 contact，不是 request 自報身分。</param>
    /// <param name="lines">本次付款的正規化明細。</param>
    /// <exception cref="InvalidOperationException">任何類別 ID 不合法或不屬於登入教會時。</exception>
    private void ResolveOfferingCategories(Entity loginContact, IReadOnlyList<DonationLineItemInput> lines)
    {
        ArgumentNullException.ThrowIfNull(loginContact);
        ArgumentNullException.ThrowIfNull(lines);
        var categoryList = DonationOfferingCategoryResolver.ReadForContact(
            ToolUtility.m_Crm2011OrganizationService,
            loginContact);
        var ids = new Guid[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            if (!Guid.TryParseExact(lines[index].Category, "D", out ids[index]) || ids[index] == Guid.Empty)
            {
                throw new InvalidOperationException("奉獻類別 ID 格式無效。");
            }
        }

        var resolved = DonationOfferingCategoryResolver.ResolveAllForWrite(
            DonationOfferingCategoryResolver.ResolveChurchId(loginContact),
            categoryList,
            ids);
        for (var index = 0; index < lines.Count; index++)
        {
            lines[index].ResolvedCategory = resolved[index];
        }
    }

    /// <summary>解析單筆 KeyIn 或認獻類別；每次只保留本次呼叫的教會清單。</summary>
    /// <param name="loginContact">伺服器登入 contact。</param>
    /// <param name="categoryId">表單提交的正式類別 Guid 字串。</param>
    /// <returns>屬於登入教會且含必填舊選項值的啟用類別。</returns>
    private OfferingCategory ResolveOfferingCategory(Entity loginContact, string categoryId)
    {
        if (!Guid.TryParseExact(categoryId, "D", out var id) || id == Guid.Empty)
        {
            throw new InvalidOperationException("奉獻類別 ID 格式無效。");
        }

        var categoryList = DonationOfferingCategoryResolver.ReadForContact(
            ToolUtility.m_Crm2011OrganizationService,
            loginContact);
        return DonationOfferingCategoryResolver.ResolveAllForWrite(
            DonationOfferingCategoryResolver.ResolveChurchId(loginContact),
            categoryList,
            new[] { id })[0];
    }
}
