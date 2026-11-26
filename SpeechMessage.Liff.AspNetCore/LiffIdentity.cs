/*
 * 檔案整體說明
 * 目的與定位：F6 共用 LIFF 模組驗證完成後的不可變身分值，由 LiffIdTokenVerifier
 * 建立，交給端點 filter 與各產品 actor resolver 作為後續授權查詢的輸入。
 * 主要功能與流程：保存 LINE sub、可省略的顯示姓名與已確認尚未到期的 UTC 瞬間。
 * 本型別不解析 token、不查租戶或角色；身分存在不代表任何教會或產品權限。
 * 使用邊界與維護：只有三個純值，不持有 request、response、client、Session 或 disposable；
 * 含個資的值由單次請求 owner 保留到請求完成，不得放入跨使用者快取或寫入診斷。
 * 不建立工作、訂閱或資源，沒有可由本型別清理的傳輸；驗證失敗時不得建立此成功結果。
 * 離線測試核對每筆獨立值及 A/B 並行隔離，不宣稱已完成 LINE 服務實機驗收。
 */
using System;

namespace SpeechMessage.Liff.AspNetCore;

/// <summary>
/// 已由官方驗證端點核對 audience 與有效期限的單次請求身分純值。
/// 呼叫端須另外從伺服器資料解析租戶及角色，不得把姓名當成識別或授權依據。
/// </summary>
/// <param name="UserId">官方回應的非空 sub，保留原值供伺服器精確比對。</param>
/// <param name="DisplayName">可省略的顯示姓名，只供本次請求顯示，不能當快取鍵。</param>
/// <param name="ExpiresAt">驗證完成時仍大於時鐘現在的 UTC 到期瞬間，不提供寬限時間。</param>
public sealed record LiffIdentity(string UserId, string? DisplayName, DateTimeOffset ExpiresAt);
