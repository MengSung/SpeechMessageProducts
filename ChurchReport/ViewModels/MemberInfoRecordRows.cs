using System;

namespace ChurchReport.ViewModels
{
    public class ContactPresentRecordRow
    {
        public string PresentRecordId { get; set; }
        public string FullName { get; set; }
        public DateTime? SundayDate { get; set; }
        public bool Sunday { get; set; }
        public bool SmallGroup { get; set; }
        public string PrayItem { get; set; }
    }

    public class MemberInfoStorLessonRow
    {
        public string StorLessonsEntityId { get; set; }
        public string DiscipleLessonsName { get; set; }
        public DateTime DiscipleLessonsDateTime { get; set; }
        public string StageName { get; set; }
        public bool CurrentComplete { get; set; }
    }
}
