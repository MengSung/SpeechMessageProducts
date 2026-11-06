/*
 * 檔案整體說明
 *
 * 目的與定位：保存舊奉獻流程使用的信用卡token、卡號顯示片段及到期資訊；此可變模型屬付款操作邊界，不能放入跨使用者快取或整體診斷輸出，token亦不是使用者授權。
 *
 * 主要功能與流程：
 * 本檔宣告 CreditCard。
 * 資料／選項欄位包含 CreditCard、CCToken、LeftCardNumber、RightCardNumber、CreditCardNumber、ExpireDate，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments.Tests/Workflows/PaymentOrderDraftMapperTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class CreditCard
    {
        public CreditCard()
        { }

        public string CCToken { get; set; }             // CCToken
        public string LeftCardNumber { get; set; }    // 信用卡卡號
        public string RightCardNumber { get; set; }    // 信用卡卡號
        public string CreditCardNumber { get; set; }    // 信用卡卡號
        public string ExpireDate { get; set; }          // 過期日
    }
}
