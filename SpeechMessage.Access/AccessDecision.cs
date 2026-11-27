/*
 * 檔案整體說明
 * 目的與定位：F8權限引擎回傳給產品呼叫端的單次授權結果，保存允許旗標、成功範圍及固定原因碼，不攜帶Contact或SDK資料列。
 * 主要功能與流程：DataverseAccessPolicy完成身分、租戶、角色與資料範圍檢查，且歸還租約後才發布此不可變值；拒絕時不提供可用範圍，原因碼不包含輸入內容。
 * 使用邊界與維護：本檔只描述輸出，不查詢、不擁有連線或背景工作；呼叫端不得把結果當成另一請求的授權快取。結果由當次請求持有並於請求結束後釋放參照，變更須驗證允許、拒絕及角色撤回的矩陣案例。
 */
namespace SpeechMessage.Access;

/// <summary>只保存授權結果及固定原因碼，沒有Contact、SDK Entity或權限集合參照；不應跨請求快取。</summary>
/// <param name="Allowed">只有所有範圍與權限檢查成功才為true。</param>
/// <param name="Scope">成功的資料範圍；拒絕時為null。</param>
/// <param name="ReasonCode">開發者固定的原因碼，不包含輸入、秘密或個資。</param>
public sealed record AccessDecision(bool Allowed, ScopeLevel? Scope, string ReasonCode);
