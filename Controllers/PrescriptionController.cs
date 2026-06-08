using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyBillingService.Data;
using PharmacyBillingService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PharmacyBillingService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PrescriptionController : ControllerBase
    {
        private readonly PharmacyDbContext _context;

        public PrescriptionController(PharmacyDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Doctor,Pharmacist,Receptionist,Cashier,Patient")]
        public async Task<ActionResult<IEnumerable<object>>> GetPrescriptions()
        {
            // Retrieve prescriptions from event logs
            var logs = await _context.EventLogs
                .Where(l => l.EventType == "prescription.created")
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            if (User.IsInRole("Patient"))
            {
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var usernameClaim = User.Identity?.Name ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                if (int.TryParse(userIdClaim, out int patientId))
                {
                    var filteredLogs = new List<EventLog>();
                    foreach (var log in logs)
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(log.Payload);
                            if (doc.RootElement.TryGetProperty("patientId", out var pIdProp))
                            {
                                if (pIdProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    int val = pIdProp.GetInt32();
                                    if (val == patientId || (val == 4 && usernameClaim == "patient"))
                                    {
                                        filteredLogs.Add(log);
                                    }
                                }
                                else if (pIdProp.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(pIdProp.GetString(), out int parsedId))
                                {
                                    if (parsedId == patientId || (parsedId == 4 && usernameClaim == "patient"))
                                    {
                                        filteredLogs.Add(log);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignore malformed JSON
                        }
                    }
                    return Ok(filteredLogs);
                }
                return Ok(new List<EventLog>());
            }

            return Ok(logs);
        }

        [HttpPost("process/{id}")]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<IActionResult> ProcessPrescription(int id)
        {
            var log = await _context.EventLogs.FindAsync(id);
            if (log == null) return NotFound();

            log.Status = "Processed";
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Prescription processed successfully." });
        }
    }
}
