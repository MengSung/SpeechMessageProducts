using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class Fee
    {
        public Fee()
        { }

        public string DiscipleLessonsId { get; set; }          // 教會課程 Id
        public string StorLessonsId { get; set; }              // 學員上課記錄 Id
        public string DiscipleLessonsName { get; set; }        // 課程名稱
        public string FullName { get; set; }                   // 姓名
        public DateTime Birthday { get; set; }                 // 生日
        public String Gender { get; set; }                     // 性別
        public String SmallGroupName { get; set; }             // 小組名稱

        public string MobilePhone { get; set; }                // 行動電話
        public DateTime PayDate { get; set; }                  // 繳費日期
        public int Amount { get; set; }                        // 繳費金額
        public DateTime RefundDate { get; set; }               // 退費日期
        public int RefundAmount { get; set; }                  // 退費金額
        public int ShouldPayAmount { get; set; }               // 應收金額
        public String PayWay { get; set; }                     // 付款方式

        public String SubClass { get; set; }                   // 班別

        #region 課程點名
        public bool Lesson1 { get; set; }       // 第一課點名
        public bool Lesson2 { get; set; }       // 第二課點名
        public bool Lesson3 { get; set; }       // 第三課點名
        public bool Lesson4 { get; set; }       // 第四課點名
        public bool Lesson5 { get; set; }       // 第五課點名
        public bool Lesson6 { get; set; }       // 第六課點名
        public bool Lesson7 { get; set; }       // 第七課點名
        public bool Lesson8 { get; set; }       // 第八課點名
        public bool Lesson9 { get; set; }       // 第九課點名
        public bool Lesson10 { get; set; }       // 第十課點名
        public bool Lesson11 { get; set; }       // 第十一課點名
        public bool Lesson12 { get; set; }       // 第十二課點名
        public bool Lesson13 { get; set; }       // 第十三課點名
        public bool Lesson14 { get; set; }       // 第十四課點名
        public bool Lesson15 { get; set; }       // 第十五課點名

        public bool Lesson16 { get; set; }       // 第十六課點名
        public bool Lesson17 { get; set; }       // 第十七課點名
        public bool Lesson18 { get; set; }       // 第十八課點名
        public bool Lesson19 { get; set; }       // 第十九課點名
        public bool Lesson20 { get; set; }       // 第二十課點名
        public bool Lesson21 { get; set; }       // 第二十一課點名
        public bool Lesson22 { get; set; }       // 第二十二課點名
        public bool Lesson23 { get; set; }       // 第二十三課點名
        public bool Lesson24 { get; set; }       // 第二十四課點名
        public bool Lesson25 { get; set; }       // 第二十五課點名
        public bool Lesson26 { get; set; }       // 第二十六課點名
        public bool Lesson27 { get; set; }       // 第二十七課點名
        public bool Lesson28 { get; set; }       // 第二十八課點名
        public bool Lesson29 { get; set; }       // 第二十九課點名
        public bool Lesson30 { get; set; }       // 第三十課點名

        #endregion
        #region 作業繳交
        [Required()]
        //[Required(AllowEmptyStrings = true)]
        public DateTime HomeWorkA { get; set; }       // A 作業繳交
        //[Required(AllowEmptyStrings = true)]
        public DateTime HomeWorkB { get; set; }       // B 作業繳交
        //[Required(AllowEmptyStrings = true)]
        public DateTime HomeWorkC { get; set; }       // C 作業繳交
        //[Required(AllowEmptyStrings = true)]
        public DateTime HomeWorkD { get; set; }       // D 作業繳交
        //[Required(AllowEmptyStrings = true)]
        public DateTime HomeWorkE { get; set; }       // E 作業繳交
        #endregion
    }
}
