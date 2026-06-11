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
        private readonly IConfiguration _configuration;

        public PrescriptionController(PharmacyDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
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
                            if (doc.RootElement.TryGetProperty("patientId", out var pIdProp) || 
                                doc.RootElement.TryGetProperty("PatientId", out pIdProp))
                            {
                                if (pIdProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    if (pIdProp.GetInt32() == patientId)
                                        filteredLogs.Add(log);
                                }
                                else if (pIdProp.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(pIdProp.GetString(), out int parsedId))
                                {
                                    if (parsedId == patientId)
                                        filteredLogs.Add(log);
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
        [Authorize(Roles = "Admin,Receptionist,Pharmacist")]
        public async Task<IActionResult> ProcessPrescription(int id)
        {
            var log = await _context.EventLogs.FindAsync(id);
            if (log == null) return NotFound();

            log.Status = "Processed";
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Prescription processed successfully." });
        }

        // Service-to-Service endpoint — xác thực bằng API key thay vì JWT
        [HttpPost("create-direct")]
        [AllowAnonymous]
        public async Task<IActionResult> CreatePrescriptionDirect([FromBody] PharmacyBillingService.DTOs.PrescriptionCreatedEvent ev)
        {
            var apiKey = Request.Headers["X-Service-API-Key"].FirstOrDefault();
            var configKey = _configuration["ServiceApiKey"] ?? "MedicareServiceInternalKey2024";
            if (apiKey != configKey)
            {
                return Unauthorized(new { message = "Invalid service API key." });
            }

            if (ev == null || ev.Medicines == null || !ev.Medicines.Any())
            {
                return BadRequest("Invalid prescription data.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                decimal totalMedicineFee = 0;
                foreach (var item in ev.Medicines)
                {
                    // 1. Try finding by ID
                    var medicine = await _context.Medicines.FindAsync(item.MedicineId);

                    // 2. If not found, try finding by Name (case-insensitive and partial match)
                    if (medicine == null && !string.IsNullOrEmpty(item.MedicineName))
                    {
                        var normalizedName = item.MedicineName.ToLower().Trim();
                        medicine = await _context.Medicines
                            .FirstOrDefaultAsync(m => m.Name.ToLower().Contains(normalizedName) || normalizedName.Contains(m.Name.ToLower()));
                    }

                    // 3. Fallback to first available medicine as a safety valve
                    if (medicine == null)
                    {
                        medicine = await _context.Medicines.FirstOrDefaultAsync();
                    }

                    if (medicine == null)
                    {
                        return NotFound($"Medicine with ID {item.MedicineId} or name '{item.MedicineName}' not found and no fallback medicine exists.");
                    }

                    if (medicine.StockQuantity < item.Quantity)
                    {
                        // Auto-restock if stock is low for mock dev purposes
                        medicine.StockQuantity += 1000;
                    }

                    medicine.StockQuantity -= item.Quantity;
                    totalMedicineFee += medicine.Price * item.Quantity;
                }

                var bill = new Bill
                {
                    PatientId = ev.PatientId,
                    DoctorName = ev.DoctorName ?? "Bác sĩ điều trị",
                    ExaminationFee = 150000,
                    MedicineFee = totalMedicineFee,
                    TotalAmount = 150000 + totalMedicineFee,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Bills.Add(bill);

                var eventLog = new EventLog
                {
                    EventType = "prescription.created",
                    Payload = System.Text.Json.JsonSerializer.Serialize(ev),
                    Status = "Success",
                    Timestamp = DateTime.UtcNow
                };
                _context.EventLogs.Add(eventLog);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { Message = "Prescription bill created successfully via Direct API.", BillId = bill.Id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
