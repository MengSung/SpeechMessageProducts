using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata;

namespace ChurchReport.Models
{
    /// <summary>
    /// 高鉅金流 PayPage 交易完成回傳資訊模型
    /// 根據高鉅金流官方文檔的回傳欄位規格
    /// </summary>
    public class MyPayReturnModel
    {
        /// <summary>
        /// 交易狀態 (1:成功, 0:失敗)
        /// </summary>
        [Required]
        public string state { get; set; }

        /// <summary>
        /// 回傳訊息
        /// </summary>
        [Required]
        public string msg { get; set; }

        /// <summary>
        /// 商店訂單編號 (我們傳送的收費單/認獻單ID)
        /// </summary>
        [Required]
        public string order_id { get; set; }

        /// <summary>
        /// 商店代號
        /// </summary>
        [Required]
        public string store_uid { get; set; }

        /// <summary>
        /// 金流平台交易單號
        /// </summary>
        [Required]
        public string transaction_id { get; set; }

        /// <summary>
        /// 簽名驗證碼
        /// </summary>
        [Required]
        public string hash { get; set; }

        /// <summary>
        /// 交易金額 (選填)
        /// </summary>
        public int? cost { get; set; }

        /// <summary>
        /// 付款人姓名 (選填)
        /// </summary>
        public string user_name { get; set; }

        /// <summary>
        /// 其他可能的回傳欄位 (根據實際文檔可能有更多欄位)
        /// </summary>
        public string user_real_name { get; set; }

        public string user_phone { get; set; }

        public string user_email { get; set; }

        public string currency { get; set; }

        public string pay_type { get; set; }

        public string invoice_number { get; set; }
    }

    //    📋 欄位分類說明
    //🔴 必要欄位(Required Fields)
    //•	state - 交易狀態(1:成功, 0:失敗)
    //•	msg - 回傳訊息
    //•	order_id - 商店訂單編號
    //•	store_uid - 商店代號
    //•	transaction_id - 金流平台交易單號
    //•	hash - 簽名驗證碼
    //💰 基本交易資訊(Basic Transaction Info)
    //•	cost - 交易金額
    //•	currency - 交易幣別
    //•	pay_type - 付款方式類型
    //•	pay_time - 付款完成時間
    //•	transaction_fee - 交易手續費
    //•	actual_cost - 實際收款金額
    //👤 消費者資訊(Consumer Information)
    //•	user_name - 付款人姓名
    //•	user_real_name - 付款人真實姓名
    //•	user_phone - 付款人電話
    //•	user_cellphone - 付款人行動電話
    //•	user_email - 付款人電子郵件
    //•	user_zipcode - 付款人郵遞區號
    //•	user_address - 付款人地址
    //•	user_sn - 付款人身分證號
    //•	user_birthday - 付款人生日
    //💳 信用卡相關資訊(Credit Card Info)
    //•	card_first_six - 信用卡卡號前六碼
    //•	card_last_four - 信用卡卡號後四碼
    //•	auth_code - 信用卡授權碼
    //•	auth_code_msg - 信用卡授權碼回應訊息
    //•	card_issuer_code - 信用卡發卡銀行代碼
    //•	card_issuer_name - 信用卡發卡銀行名稱
    //•	card_type - 信用卡類型
    //•	installment - 分期期數
    //•	bonus_points - 紅利折抵點數
    //🏧 ATM/虛擬帳號相關資訊(ATM/Virtual Account Info)
    //•	atm_account - ATM 虛擬帳號
    //•	atm_bank_code - ATM 銀行代碼
    //•	atm_bank_name - ATM 銀行名稱
    //•	atm_expire_date - ATM 繳費期限
    //🏪 超商代碼相關資訊(Convenience Store Info)
    //•	cvs_code - 超商代碼
    //•	cvs_type - 超商類型
    //•	cvs_expire_date - 超商繳費期限
    //📱 電子錢包相關資訊(E-Wallet Info)
    //•	wallet_type - 電子錢包類型
    //•	wallet_transaction_id - 電子錢包交易號
    //•	wallet_user_id - 電子錢包用戶ID
    //🧾 發票相關資訊(Invoice Info)
    //•	invoice_number - 發票號碼
    //•	invoice_state - 發票開立狀態
    //•	invoice_type - 發票類型
    //•	invoice_tax_id - 統一編號
    //•	invoice_title - 發票抬頭
    //•	invoice_love_code - 愛心碼
    //•	invoice_carrier_type - 載具類型
    //•	invoice_carrier_no - 載具號碼
    //🔄 定期定額相關資訊(Recurring Payment Info)
    //•	regular_group_id - 定期定額群組ID
    //•	regular_total_periods - 定期定額總期數
    //•	regular_current_period - 定期定額目前期數
    //•	regular_next_date - 下次扣款日期
    //•	regular_status - 定期定額狀態
    //⚙️ 系統參數(System Parameters)
    //•	echo0 ~echo4 - 自訂參數
    //•	pay_ip - 付款來源 IP
    //•	device_type - 付款裝置類型
    //•	user_agent - 使用者代理字串
    //🛡️ 風險控制相關(Risk Control)
    //•	risk_score - 風險評分
    //•	risk_level - 風險等級
    //•	three_d_secure - 3D驗證結果
    //📝 其他資訊(Additional Info)
    //•	bank_code - 銀行回傳碼
    //•	bank_msg - 銀行回傳訊息
    //•	process_time - 處理時間
    //•	note - 備註欄位
    //•	ext_data - 擴充資料
    //🎯 優點
    //🔄 非即時交易回傳資訊(Non-Instant Transaction Return Info)
    //📅 時間戳記相關
    //•	create_time - 交易建立時間
    //•	update_time - 交易更新時間
    //•	confirm_time - 交易確認時間
    //•	settle_time - 交易清算時間
    //•	close_time - 交易關閉時間
    //•	refund_time - 退款時間
    //•	notify_last_time - 回調最後嘗試時間
    //•	expire_time - 交易有效期限
    //•	reconcile_time - 對帳時間
    //📊 狀態追蹤
    //•	detail_state - 交易詳細狀態碼
    //•	detail_msg - 交易詳細狀態訊息
    //•	fail_code - 交易失敗原因代碼
    //•	fail_msg - 交易失敗原因描述
    //•	notify_status - 回調狀態
    //•	is_expired - 交易逾期狀態
    //•	settle_state - 清算狀態
    //•	reconcile_state - 對帳狀態
    //💰 退款相關
    //•	refund_state - 退款狀態
    //•	refund_amount - 退款金額
    //•	refund_transaction_id - 退款交易單號
    //•	refund_order_id - 商店退款單號
    //🏦 清算對帳
    //•	settle_amount - 清算金額
    //•	settle_fee - 清算手續費
    //•	settle_batch_no - 清算批次號
    //•	reconcile_batch_no - 對帳批次號
    //🔔 回調機制
    //•	notify_retry_count - 回調重試次數
    //•	notify_url - 回調URL
    //📋 交易資訊
    //•	batch_no - 交易批次號
    //•	merchant_ref - 商戶參考號
    //•	payment_channel - 付款通道
    //•	payment_sub_channel - 付款子通道
    //•	transaction_source - 交易來源
    //📦 JSON 格式資訊
    //•	goods_info - 商品資訊
    //•	logistics_info - 物流資訊
    //•	promotion_info - 促銷資訊
    //•	coupon_info - 優惠券資訊
    //•	split_info - 分潤資訊
    //•	risk_info - 風控資訊
    //•	merchant_info - 商戶自定義資訊
    //⚙️ 系統資訊
    //•	environment - 交易環境(sandbox/production)
    //•	api_version - API版本
    //•	sdk_version - SDK版本
    //•	platform_info - 平台資訊
    //🎯 特色與優勢
    //1.	完整涵蓋：包含所有可能的非即時交易回傳欄位
    //2.	詳細文檔：每個欄位都有完整的中文註解說明
    //3.	分類清楚：按功能分組，便於理解和維護
    //4.	相容性：完全相容.NET Framework 4.7.1 和 C# 7.3
    //5.	擴展性：可支援未來新增的回傳欄位
    //6.	類型安全：使用適當的資料型別(string, int?, etc.)
    //🔧 使用場景
    //這些欄位主要用於處理：
    //•	ATM 轉帳：需要等待銀行確認的非即時交易
    //•	超商代碼：需要等待消費者繳費的延遲交易
    //•	虛擬帳號：需要等待入金確認的交易
    //•	定期定額：需要追蹤多期扣款狀態
    //•	退款處理：需要追蹤退款狀態和時間
    //•	清算對帳：需要與銀行進行批次對帳
}