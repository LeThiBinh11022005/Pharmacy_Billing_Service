using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyBillingService.Data;
using PharmacyBillingService.Models;

namespace PharmacyBillingService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BillsController : ControllerBase
    {
        private readonly PharmacyDbContext _context;

        public BillsController(PharmacyDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<ActionResult<IEnumerable<Bill>>> GetBills()
        {
            return await _context.Bills.ToListAsync();
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Receptionist")]
        public async Task<ActionResult<Bill>> GetBill(int id)
        {
            var bill = await _context.Bills.FindAsync(id);

            if (bill == null)
            {
                return NotFound();
            }

            return bill;
        }

        [HttpPost]
        [Authorize(Roles = "Receptionist")]
        public async Task<ActionResult<Bill>> PostBill(Bill bill)
        {
            bill.TotalAmount = bill.ExaminationFee + bill.MedicineFee;
            bill.CreatedAt = DateTime.UtcNow;
            bill.Status = "Pending";

            _context.Bills.Add(bill);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBill), new { id = bill.Id }, bill);
        }

        [HttpPost("{id}/pay")]
        [Authorize(Roles = "Admin,Receptionist,Patient")]
        public async Task<IActionResult> PayBill(int id)
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
    }
}
