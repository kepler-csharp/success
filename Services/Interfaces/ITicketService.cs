
using Success.Models.Responses;

namespace success.Services.Interfaces;

public interface ITicketService
{
    Task<TicketValidationResponse> ValidateTicketAsync(string scanCode);
}
