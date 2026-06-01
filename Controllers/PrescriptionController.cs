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
        [Authorize(Roles = "Admin,Doctor")]
        public async Task<ActionResult<IEnumerable<object>>> GetPrescriptions()
        {
            // Retrieve prescriptions from event logs
            var logs = await _context.EventLogs
                .Where(l => l.EventType == "prescription.created")
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

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
