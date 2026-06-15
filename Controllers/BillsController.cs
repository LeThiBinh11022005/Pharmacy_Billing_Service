using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmacyBillingService.Data;
using PharmacyBillingService.Models;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PharmacyBillingService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BillsController : ControllerBase
    {
        private readonly PharmacyDbContext _context;
        private readonly ILogger<BillsController> _logger;

        public BillsController(PharmacyDbContext context, ILogger<BillsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Receptionist,Pharmacist,Cashier,Patient")]
        public async Task<ActionResult<IEnumerable<Bill>>> GetBills()
        {
            try
            {
                if (User.IsInRole("Patient"))
                {
                    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (int.TryParse(userIdClaim, out int patientId))
                    {
                        return await _context.Bills
                            .Where(b => b.PatientId == patientId)
                            .OrderByDescending(b => b.CreatedAt)
                            .ToListAsync();
                    }
                    return Ok(new List<Bill>());
                }
                return await _context.Bills.OrderByDescending(b => b.CreatedAt).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetBills() endpoint.");
                return StatusCode(500, new { message = "Lỗi hệ thống khi tải danh sách hóa đơn.", error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Receptionist,Pharmacist,Cashier,Patient")]
        public async Task<ActionResult<Bill>> GetBill(int id)
        {
            try
            {
                var bill = await _context.Bills.FindAsync(id);

                if (bill == null)
                {
                    return NotFound();
                }

                if (User.IsInRole("Patient"))
                {
                    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (int.TryParse(userIdClaim, out int patientId))
                    {
                        if (bill.PatientId != patientId)
                        {
                            return Forbid();
                        }
                    }
                }

                return bill;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetBill({BillId}) endpoint.", id);
                return StatusCode(500, new { message = "Lỗi hệ thống khi tải chi tiết hóa đơn.", error = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Receptionist,Pharmacist,Cashier")]
        public async Task<ActionResult<Bill>> PostBill(Bill bill)
        {
            try
            {
                bill.TotalAmount = bill.ExaminationFee + bill.MedicineFee;
                bill.CreatedAt = DateTime.UtcNow;
                bill.Status = "Pending";

                _context.Bills.Add(bill);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetBill), new { id = bill.Id }, bill);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in PostBill() endpoint.");
                return StatusCode(500, new { message = "Lỗi hệ thống khi tạo hóa đơn.", error = ex.Message });
            }
        }

        [HttpPost("{id}/pay")]
        [Authorize(Roles = "Admin,Receptionist,Pharmacist,Cashier,Patient")]
        public async Task<IActionResult> PayBill(int id)
        {
            try
            {
                var bill = await _context.Bills.FindAsync(id);
                if (bill == null)
                {
                    return NotFound();
                }

                if (bill.Status == "Paid")
                {
                    return BadRequest("Bill is already paid.");
                }

                bill.Status = "Paid";
                await _context.SaveChangesAsync();

                return Ok(new { Message = "Bill paid successfully", Bill = bill });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in PayBill({BillId}) endpoint.", id);
                return StatusCode(500, new { message = "Lỗi hệ thống khi thực hiện thanh toán hóa đơn.", error = ex.Message });
            }
        }
    }
}
