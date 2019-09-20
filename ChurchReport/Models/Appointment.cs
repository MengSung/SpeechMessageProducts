using System;

namespace ChurchReport.Models
{
    public class Appointment
    {
        public String AppointmentId { get; set; }
        public string Text { get; set; }
        public string AppointmentType { get; set; }
        public int[] CategoryId { get; set; }// 約會類別
        public int LeaveId { get; set; }// 請假人資類別
        public int LocationId { get; set; }// 場地類別
        public int[] OwnerId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string RecurrenceRule { get; set; }
        public bool AllDay { get; set; }
        public string Description { get; set; }
    }
}