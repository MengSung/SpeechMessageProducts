using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class Fee
    {
        public Fee()
        { }

        public string DiscipleLessonsId { get; set; }          //教會課程 Id
        public string StorLessonsId { get; set; }              //學員上課記錄 Id
        public string DiscipleLessonsName { get; set; }     // 課程名稱
        public string FullName { get; set; }                // 姓名
        public string MobilePhone { get; set; }             // 行動電話
        public DateTime PayDate { get; set; }               // 繳費日期
        public int Amount { get; set; }                     // 繳費金額
        public String PayWay { get; set; }                     // 付款方式

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
