using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ChurchReport.Models
{
    /// <summary>
    /// PayPage 金流交易類別
    /// </summary>
    public class PayPage
    {
        /// <summary>
        /// 特約商店代碼
        /// </summary>
        public string store_uid { get; set; }
        
        /// <summary>
        /// 消費者帳號 (記憶卡號和記憶發票依此欄位為基準)
        /// </summary>
        public string user_id { get; set; }
        
        /// <summary>
        /// 消費者姓名，電子錢包交易必要欄位
        /// </summary>
        public string user_name { get; set; }
        
        /// <summary>
        /// 消費者真實姓名，電子錢包交易必要欄位
        /// </summary>
        public string user_real_name { get; set; }
        
        /// <summary>
        /// 消費者郵遞區號
        /// </summary>
        public string user_zipcode { get; set; }
        
        /// <summary>
        /// 消費者帳單地址
        /// </summary>
        public string user_address { get; set; }
        
        /// <summary>
        /// 證號類型
        /// </summary>
        public string user_sn_type { get; set; }
        
        /// <summary>
        /// 付款人身分證/統一證號/護照號碼
        /// </summary>
        public string user_sn { get; set; }
        
        /// <summary>
        /// 消費者家用電話
        /// </summary>
        public string user_phone { get; set; }
        
        /// <summary>
        /// 消費者行動電話國碼，電子錢包交易必要欄位
        /// </summary>
        public string user_cellphone_code { get; set; }
        
        /// <summary>
        /// 消費者行動電話，電子錢包交易必要欄位
        /// </summary>
        public string user_cellphone { get; set; }
        
        /// <summary>
        /// 消費者 E-Mail，電子錢包交易必要欄位
        /// </summary>
        public string user_email { get; set; }
        
        /// <summary>
        /// 消費者生日
        /// </summary>
        public string user_birthday { get; set; }
        
        /// <summary>
        /// 訂單總金額 = 物品之總價加總 + 折價 + 運費(如為定期定額交易，此為每一期的應扣金額)
        /// </summary>
        public string cost { get; set; }
        
        /// <summary>
        /// 預設交易幣別(預設為TWD新台幣)
        /// </summary>
        public string currency { get; set; }
        
        /// <summary>
        /// 啟用dcc(自動換匯)
        /// </summary>
        public int? enable_dcc { get; set; }
        
        /// <summary>
        /// 訂單編號（訂單編號最長為50bytes）
        /// </summary>
        public string order_id { get; set; }
        
        /// <summary>
        /// 消費者來源 IP(格式務必正確，部分金流服務商後續處理會驗證)
        /// </summary>
        public string ip { get; set; }
        
        /// <summary>
        /// 訂單內物品數
        /// </summary>
        public string item { get; set; }
        
        /// <summary>
        /// 訂單物品項目
        /// </summary>
        public List<ProductItem> items { get; set; }
        
        /// <summary>
        /// 定期定額付費，期數單位
        /// </summary>
        public string regular { get; set; }
        
        /// <summary>
        /// 總期數(如為 12 期即代入 12，如果不設定終止期，請代入 0)
        /// </summary>
        public string regular_total { get; set; }
        
        /// <summary>
        /// 定期扣款起扣日 (不得小於當日，若未指定日期，將判定為當日扣款，格式為 YYYYMMDD，如 20090916)
        /// </summary>
        public string regular_first_charge_date { get; set; }
        
        /// <summary>
        /// 定期定額式扣款編號
        /// </summary>
        public string group_id { get; set; }
        
        /// <summary>
        /// 自訂回傳參數 1
        /// </summary>
        public string echo_0 { get; set; }
        
        /// <summary>
        /// 自訂回傳參數 2
        /// </summary>
        public string echo_1 { get; set; }
        
        /// <summary>
        /// 自訂回傳參數 3
        /// </summary>
        public string echo_2 { get; set; }
        
        /// <summary>
        /// 自訂回傳參數 4
        /// </summary>
        public string echo_3 { get; set; }
        
        /// <summary>
        /// 自訂回傳參數 5
        /// </summary>
        public string echo_4 { get; set; }
        
        /// <summary>
        /// 預選付費方法
        /// </summary>
        public string pfn { get; set; }
        
        /// <summary>
        /// 消費者操作介面類型 pc/app
        /// </summary>
        public string interface_type { get; set; }
        
        /// <summary>
        /// 折價金額 (預設0)，如果有使用發票功能，建議將折扣資訊放入item。
        /// </summary>
        public string discount { get; set; }
        
        /// <summary>
        /// 交易成功導頁網址
        /// </summary>
        public string success_returl { get; set; }
        
        /// <summary>
        /// 交易失敗導頁網址
        /// </summary>
        public string failure_returl { get; set; }
        
        /// <summary>
        /// 虛擬帳號與超商代碼使用之有效天數
        /// </summary>
        public int? limit_pay_days { get; set; }
        
        /// <summary>
        /// 運費
        /// </summary>
        public string shipping_fee { get; set; }
        
        /// <summary>
        /// 啟用快速結帳
        /// </summary>
        public int? enable_quickpay { get; set; }
        
        /// <summary>
        /// 啟用電子錢包
        /// </summary>
        public int? enable_ewallet { get; set; }
        
        /// <summary>
        /// 電子錢包虛擬卡號
        /// </summary>
        public string virtual_pan { get; set; }
        
        /// <summary>
        /// 電子錢包執行類型
        /// </summary>
        public int? ewallet_type { get; set; }
        
        /// <summary>
        /// 交易類型
        /// </summary>
        public int? transaction_type { get; set; }
        
        /// <summary>
        /// 國內信用卡分期顯示限定，JSON格式如下: {"3": ["013", "822", "808"], "6": ["822", "812"]}
        /// </summary>
        public string creditcard_installment { get; set; }
        
        /// <summary>
        /// 無卡分期商品代碼
        /// </summary>
        public string cardless_code { get; set; }
        
        /// <summary>
        /// 無卡分期顯示消費者可選擇之期數，格式為陣列，例：[3, 6, 9, 12]
        /// </summary>
        public string cardless_installment { get; set; }
        
        /// <summary>
        /// eACH交易代碼(如果使用eACH必須指定交易代碼)
        /// </summary>
        public string each_code { get; set; }
        
        /// <summary>
        /// 開立發票
        /// </summary>
        public int? issue_invoice_state { get; set; }
        
        /// <summary>
        /// 電子發票稅率別
        /// </summary>
        public int? invoice_ratetype { get; set; }
        
        /// <summary>
        /// 電子發票開立類型
        /// </summary>
        public int? invoice_input_type { get; set; }
        
        /// <summary>
        /// 「雲端發票」類型，當invoice_input_type為1，此狀態才有效
        /// </summary>
        public string invoice_cloud_type { get; set; }
        
        /// <summary>
        /// 統一編號，當invoice_input_type為1，此欄位才有效，非必要
        /// </summary>
        public string invoice_tax_id { get; set; }
        
        /// <summary>
        /// 手機條碼，當invoice_cloud_type為2，此欄位才有效
        /// </summary>
        public string invoice_mobile_code { get; set; }
        
        /// <summary>
        /// 自然人憑證條碼，當invoice_cloud_type為3，此欄位才有效
        /// </summary>
        public string invoice_natural_person { get; set; }
        
        /// <summary>
        /// 愛心碼，當invoice_input_type為2，此欄位才有效
        /// </summary>
        public string invoice_love_code { get; set; }
        
        /// <summary>
        /// 發票抬頭，當invoice_input_type為3時，此欄位才有效
        /// </summary>
        public string invoice_b2b_title { get; set; }
        
        /// <summary>
        /// 統一編號，當invoice_input_type為3時，此欄位才有效
        /// </summary>
        public string invoice_b2b_id { get; set; }
        
        /// <summary>
        /// 發票地址，當invoice_input_type為3時，此欄位才有效
        /// </summary>
        public string invoice_b2b_address { get; set; }
        
        /// <summary>
        /// 若選擇實體發票時，發票抬頭無法異動。
        /// </summary>
        public string invoice_b2b_title_force { get; set; }
        
        /// <summary>
        /// 若選擇實體發票時，統一編號無法異動。
        /// </summary>
        public string invoice_b2b_id_force { get; set; }
        
        /// <summary>
        /// 若選擇實體發票時，預設地址無法異動。
        /// </summary>
        public int? invoice_b2b_address_force { get; set; }
        
        /// <summary>
        /// 無卡分期消費者資訊
        /// </summary>
        public object user_data { get; set; }
        
        /// <summary>
        /// 無卡分期上傳檔案路徑 (JSON多筆格式)
        /// </summary>
        public string files_path { get; set; }
        
        /// <summary>
        /// 儲值交易之交易金額/點數 (儲值交易以此欄位辨識交易金額/點數)
        /// </summary>
        public string price { get; set; }
        
        /// <summary>
        /// 儲值交易時，使用當時儲值之儲值產品代碼
        /// </summary>
        public string recharge_code { get; set; }
        
        /// <summary>
        /// 儲值交易用物品資訊
        /// </summary>
        public object[] recharge_items { get; set; }
        
        /// <summary>
        /// 經銷商代收費是否含簡訊費
        /// </summary>
        public int? agent_sms_fee_type { get; set; }
        
        /// <summary>
        /// 經銷商代收費是否含手續費
        /// </summary>
        public int? agent_charge_fee_type { get; set; }
        
        /// <summary>
        /// 經銷商代收費
        /// </summary>
        public int? agent_charge_fee { get; set; }
        
        /// <summary>
        /// 是否為經銷商代收費模式
        /// </summary>
        public int? is_agent_charge { get; set; }
        
        /// <summary>
        /// 送貨資訊 (後付款為必填)
        /// </summary>
        public object shipping_info { get; set; }
        
        /// <summary>
        /// 信用卡類交易時，是否使用授權請款模式(預設自動請款)
        /// </summary>
        public string creditcard_is_automatic_payment { get; set; }
    }
}