using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class Lesson
    {
        public Lesson()
        { }

        public string DiscipleLessonsName { get; set; }       // 教會課程名稱
        public string DiscipleLessonsId { get; set; }       // 教會課程 Id
        public DateTime EnrollStartDate { get; set; }               // 報名開始日期
        public DateTime EnrollEndDate { get; set; }               // 報名結束日期
        public DateTime LessonStartDate { get; set; }               // 課程開始日期
        public DateTime LessonEndDate { get; set; }               // 課程結束日期
        public int EnrolledNumber { get; set; }                     // 報名人數

    }
}
