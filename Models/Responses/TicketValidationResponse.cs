namespace Success.Models.Responses;

public class TicketValidationResponse
{
    public bool Success { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Type { get; set; } = "";
    public TicketInfo? Ticket { get; set; }

    public class TicketInfo
    {
        public string Code { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string Email { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string EventName { get; set; } = "";
        public string TicketType { get; set; } = "";
        public string SeatNumber { get; set; } = "";
        public string Row { get; set; } = "";
        public string VenueName { get; set; } = "";
        public string Status { get; set; } = "";
        public string EntryMode { get; set; } = "";
        public DateTime ScanTime { get; set; }
        public DateTime? ShowtimeStart { get; set; }
        public string PhotoUrl { get; set; } = "";
    }
}
