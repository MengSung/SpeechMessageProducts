/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承載LIFF應用程式ID與視窗設定，供LIFF管理API建立／查閱應用程式。
 *
 * 主要功能與流程：
 * 本檔宣告 LiffApp。
 * 資料／選項欄位包含 LiffApp、LiffId、View，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Liff/LiffClient.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging.Liff
{
    public class LiffApp
    {
        public string LiffId { get; }
        public View View { get; }

        public LiffApp(string liffId, View view)
        {
            LiffId = liffId;
            View = view;
        }
    }
}
