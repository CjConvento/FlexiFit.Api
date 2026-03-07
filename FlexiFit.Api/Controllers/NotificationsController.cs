using FirebaseAdmin.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace FlexiFit.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    public record SendTestRequest(string FcmToken, string? Title, string? Body);

    [HttpPost("test")]
    public async Task<IActionResult> SendTest([FromBody] SendTestRequest req)
    {
        // ✅ sanitize token (removes whitespace/newlines/quotes)
        var token = (req.FcmToken ?? string.Empty).Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "FCM token is required." });

        var message = new Message
        {
            Token = token,
            Notification = new Notification
            {
                Title = string.IsNullOrWhiteSpace(req.Title) ? "FlexiFit Test" : req.Title,
                Body = string.IsNullOrWhiteSpace(req.Body) ? "Hello from ASP.NET API" : req.Body
            },
            Data = new Dictionary<string, string>
            {
                ["type"] = "test",
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
            }
        };

        try
        {
            var messageId = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            return Ok(new { messageId });
        }
        catch (FirebaseMessagingException ex)
        {
            // ✅ this shows real reason: InvalidArgument / Unregistered / SenderIdMismatch / etc.
            return BadRequest(new
            {
                ex.Message,
                ErrorCode = ex.ErrorCode.ToString(),
                MessagingErrorCode = ex.MessagingErrorCode?.ToString(),
                HttpResponse = ex.HttpResponse
            });
        }
    }
}