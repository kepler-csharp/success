using System.Text.Json;

namespace success.Models.CentralApi;

internal sealed class TicketDetailApiDto
{
    public JsonElement TicketId { get; set; }
    public string? HolderEmail { get; set; }
    public string? EventName { get; set; }
    public string? VenueName { get; set; }
    public DateTime? ShowtimeStart { get; set; }
    public string? SeatLabel { get; set; }
    public bool WasAlreadyUsed { get; set; }
    public DateTime? UsedAt { get; set; }
}
