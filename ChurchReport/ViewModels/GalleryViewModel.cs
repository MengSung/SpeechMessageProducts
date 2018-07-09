using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.ViewModel
{
    public class GalleryViewModel
    {
        public IEnumerable<string> Images { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
    }
    public class RegisterViewModel
    {
        public IEnumerable<string> Images { get; set; }
        public string FullName { get; set; }
        public string Mobile { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
    public class LineBindingViewModel
    {
        public IEnumerable<string> Images { get; set; }
        public string DisplayName { get; set; }
        public string DisplayId { get; set; }
        public string GroupId { get; set; }
        public string RoomId { get; set; }
        public string LineUserId { get; set; }
        public string ViewType { get; set; }
        public string FullName { get; set; }
        public string Mobile { get; set; }
        public string EncodeUrl { get; set; }
    }
}