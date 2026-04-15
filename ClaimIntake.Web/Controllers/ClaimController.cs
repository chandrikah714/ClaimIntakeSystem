// ============================================================
// FILE: ClaimIntake.Web/Controllers/ClaimController.cs
// PURPOSE: Handles the claim submission form.
//          [Authorize] means you MUST be logged in to use this!
//
// FLOW:
// 1. User visits /Claim/Submit
// 2. Controller shows the blank form (GET)
// 3. User fills it in and clicks Submit
// 4. Controller receives the data (POST)
// 5. Validates it
// 6. Sends it to the Azure Function
// 7. Redirects to confirmation page
// ============================================================

using ClaimIntake.Domain.Models;
using ClaimIntake.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace ClaimIntake.Web.Controllers;

// [Authorize] on the class means EVERY method in this controller
// requires the user to be logged in. If not logged in,
// they get redirected to /Account/Login automatically.
[Authorize]
public class ClaimController : BaseController
{
    private readonly IConfiguration _config;
    private readonly ILogger<ClaimController> _logger;
    private readonly HttpClient _httpClient;

    public ClaimController(IConfiguration config,
        ILogger<ClaimController> logger,
        IHttpClientFactory httpClientFactory) : base(config)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("ClaimApi");
    }

    // ── GET /Claim/Submit ────────────────────────────────────────────────────
    [HttpGet]
    public IActionResult Submit()
    {
        // Show an empty form
        return View(new ClaimFormViewModel());
    }

    // ── POST /Claim/Submit ───────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]  // ← Prevents CSRF attacks
    public async Task<IActionResult> Submit(ClaimFormViewModel model)
    {
        // Check if all the [Required] and [RegularExpression] rules passed
        if (!ModelState.IsValid)
        {
            // Return the SAME form with error messages shown in red
            return View(model);
        }

        // Build the claim DTO to send to our API
        var claimDto = new ClaimDto
        {
            MemberId = model.MemberId.Trim().ToUpper(),
            ProviderId = model.ProviderId.Trim().ToUpper(),
            DiagnosisCode = model.DiagnosisCode.Trim().ToUpper(),
            ClaimAmount = model.ClaimAmount,
            SubmittedBy = User.Identity!.Name!  // Gets the logged-in username
        };

        try
        {
            // Call the Azure Function API
            var result = await SubmitToApiAsync(claimDto);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Claim {ClaimId} submitted by {User}",
                    result.ClaimId, claimDto.SubmittedBy);

                // Redirect to success page (passing claimId in URL)
                return RedirectToAction("Confirmation", new
                {
                    claimId = result.ClaimId,
                    memberId = claimDto.MemberId,
                    claimAmount = claimDto.ClaimAmount,
                    submittedBy = claimDto.SubmittedBy
                });
            }

            // API returned an error
            foreach (var error in result.ValidationErrors)
                ModelState.AddModelError(string.Empty, error);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
                ModelState.AddModelError(string.Empty, result.ErrorMessage);

            return View(model);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Claim API.");
            ModelState.AddModelError(string.Empty,
                "Unable to reach the claims service. Please try again in a moment.");
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error submitting claim.");
            ModelState.AddModelError(string.Empty,
                "An unexpected error occurred. Please contact support.");
            return View(model);
        }
    }

    // ── GET /Claim/Confirmation ──────────────────────────────────────────────
    [HttpGet]
    public IActionResult Confirmation(string claimId, string memberId,
        decimal claimAmount, string submittedBy)
    {
        var model = new ClaimConfirmationViewModel
        {
            ClaimId = claimId,
            MemberId = memberId,
            ClaimAmount = claimAmount,
            SubmittedBy = submittedBy,
            SubmittedAt = DateTime.UtcNow
        };
        return View(model);
    }

    // ── PRIVATE: Call Azure Function API ────────────────────────────────────
    private async Task<ClaimSubmissionResult> SubmitToApiAsync(ClaimDto claim)
    {
        var apiSettings = _config.GetSection("ClaimApiSettings");
        var url = $"{apiSettings["BaseUrl"]}{apiSettings["SubmitEndpoint"]}";
        var functionKey = apiSettings["FunctionKey"];

        var json = JsonSerializer.Serialize(claim);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Add the function key header (protects our API)
        _httpClient.DefaultRequestHeaders.Remove("x-functions-key");
        _httpClient.DefaultRequestHeaders.Add("x-functions-key", functionKey);

        var response = await _httpClient.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();

        // 202 Accepted = success!
        if (response.IsSuccessStatusCode)
        {
            // Parse the claimId from response body
            using var doc = JsonDocument.Parse(body);
            var claimId = claim.ClaimId;  // We set this ourselves

            return ClaimSubmissionResult.Ok(claimId);
        }

        // Parse error message from API response
        try
        {
            using var doc = JsonDocument.Parse(body);
            var msg = doc.RootElement.GetProperty("message").GetString()
                      ?? "Submission failed.";
            return ClaimSubmissionResult.Fail(msg);
        }
        catch
        {
            return ClaimSubmissionResult.Fail(
                $"API error: HTTP {(int)response.StatusCode}");
        }
    }
}
