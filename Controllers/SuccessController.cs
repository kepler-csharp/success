using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Success.Models.Responses;
using success.Services.Interfaces;

namespace success.Controllers;

[Authorize]
[Route("Success")]
public class SuccessController : Controller
{
    private readonly ITicketService _ticketService;

    public SuccessController(ITicketService ticketService)
    {
        _ticketService = ticketService;
    }

    [HttpGet("")]
    [HttpGet("/")]
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost("ValidateTicket")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateTicket([FromBody] ValidateTicketRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ScanCode))
        {
            return BadRequest(new TicketValidationResponse
            {
                Success = false,
                Title = "Codigo requerido",
                Message = "Escanea un QR o escribe el codigo del ticket.",
                Type = "error"
            });
        }

        var response = await _ticketService.ValidateTicketAsync(request.ScanCode);
        return Ok(response);
    }

}
