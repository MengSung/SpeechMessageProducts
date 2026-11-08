/*
 * 檔案整體說明
 *
 * 目的與定位：保留P2-06已編譯呼叫端對LineLegacySendWorkflow的舊Workflows組件身分，
 * 將CLR型別查找轉送至較低層LineMessagingProcessor中的唯一實作，不建立相容副本。
 *
 * 主要功能與流程：舊組件限定名稱及新來源都解析到同一完整型別名稱；Workflows本來就
 * 向下參考processor，因此不新增反向邊。ToolUtility可直接消費processor，避免為legacy
 * 送訊參考同層Workflows；原HTTP序列、Retry Key、公開多載與資源所有權均不由本檔改寫。
 *
 * 使用邊界與維護：本檔只有組件屬性，不接收個資、不建立client、cache、背景工作或
 * Disposable資源。不得移除轉送或複製原類別來掩蓋載入失敗；以真實舊組件名稱解析、
 * 型別Same斷言與完整Debug／Release相依圖驗證，資源清理由原頻道owner負責。
 */
using System.Runtime.CompilerServices;
using LineMessagingProcessor.Workflows;

[assembly: TypeForwardedTo(typeof(LineLegacySendWorkflow))]
