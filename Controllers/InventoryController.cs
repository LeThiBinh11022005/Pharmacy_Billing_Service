using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyBillingService.Data;
using PharmacyBillingService.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PharmacyBillingService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin,Pharmacist")]
    public class InventoryController : ControllerBase
    {
        private readonly PharmacyDbContext _context;

        public InventoryController(PharmacyDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Medicine>>> GetInventory()
        {
            return await _context.Medicines.ToListAsync();
        }

        [HttpPost("adjust/{id}")]
        public async Task<IActionResult> AdjustStock(int id, [FromBody] int quantity)
        {
            var medicine = await _context.Medicines.FindAsync(id);
            if (medicine == null) return NotFound();

            medicine.StockQuantity = quantity;
            await _context.SaveChangesAsync();

            return Ok(medicine);
        }
    }
}
