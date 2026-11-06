/*
 * 檔案整體說明
 *
 * 目的與定位：保存課程點名畫面各堂課的顯示名稱，Lesson1至Lesson30等欄位與既有表格欄位繫結；欄位序號代表課次，不能用顯示名稱推導課程或學員的資料庫ID。
 *
 * 主要功能與流程：
 * 本檔宣告 ClassName。
 * 資料／選項欄位包含 ClassName、Lesson1、Lesson2、Lesson3、Lesson4、Lesson5、Lesson6、Lesson7、Lesson8、Lesson9、Lesson10、Lesson11、
 * Lesson12、Lesson13、Lesson14、Lesson15，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/PhoneBindingController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs、
 * SpeechMessageProducts.ChurchReport/Models/FeeList.cs、SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class ClassName
    {
        public ClassName()
        { }

        public string Lesson1 { get; set; }   = "第一課";
        public string Lesson2 { get; set; }   = "第二課";
        public string Lesson3 { get; set; }   = "第三課";
        public string Lesson4 { get; set; }   = "第四課";
        public string Lesson5 { get; set; }   = "第五課";
        public string Lesson6 { get; set; }   = "第六課";
        public string Lesson7 { get; set; }   = "第七課";
        public string Lesson8 { get; set; }   = "第八課";
        public string Lesson9 { get; set; }   = "第九課";
        public string Lesson10 { get; set; }  = "第十課";
        public string Lesson11 { get; set; }  = "第十一課";
        public string Lesson12 { get; set; }  = "第十二課";
        public string Lesson13 { get; set; }  = "第十三課";
        public string Lesson14 { get; set; }  = "第十四課";
        public string Lesson15 { get; set; }  = "第十五課";
        public string Lesson16 { get; set; }  = "第十六課";
        public string Lesson17 { get; set; }  = "第十七課";
        public string Lesson18 { get; set; }  = "第十八課";
        public string Lesson19 { get; set; }  = "第十九課";
        public string Lesson20 { get; set; }  = "第二十課";
        public string Lesson21 { get; set; }  = "第二十一課";
        public string Lesson22 { get; set; }  = "第二十二課";
        public string Lesson23 { get; set; }  = "第二十三課";
        public string Lesson24 { get; set; }  = "第二十四課";
        public string Lesson25 { get; set; }  = "第二十五課";
        public string Lesson26 { get; set; }  = "第二十六課";
        public string Lesson27 { get; set; }  = "第二十七課";
        public string Lesson28 { get; set; }  = "第二十八課";
        public string Lesson29 { get; set; }  = "第二十九課";
        public string Lesson30 { get; set; }  = "第三十課";
        public string HomeWorkA { get; set; } = "作業A";
        public string HomeWorkB { get; set; } = "作業B";
        public string HomeWorkC { get; set; } = "作業C";
        public string HomeWorkD { get; set; } = "作業D";
        public string HomeWorkE { get; set; } = "作業E";

    }
}
