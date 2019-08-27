using System;

namespace ChurchReport.Models
{
    public class Appointment
    {
        public String AppointmentId { get; set; }
        public string Text { get; set; }
        public string AppointmentType { get; set; }
        public int[] CategoryId { get; set; }
        public int[] OwnerId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string RecurrenceRule { get; set; }
        public bool AllDay { get; set; }
        public string Description { get; set; }
    }
}