using System;

namespace ChurchReport.Models
{
    public class SameNameElement
    {
        public String SameNameElementId { get; set; }

        public String DedicationNumber { get; set; }
        public String NationId { get; set; }
        public String FullName { get; set; }
        public string Mobile { get; set; }
        public string SmallGroupName { get; set; }
        public String ChurchName { get; set; }                          //所屬教會
    }
}