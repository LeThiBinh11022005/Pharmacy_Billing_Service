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
    public class ImportBillsController : ControllerBase
    {
        private readonly PharmacyDbContext _context;

        public ImportBillsController(PharmacyDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ImportBill>>> GetImportBills()
        {
            return await _context.ImportBills
                .Include(ib => ib.Medications)
                .OrderByDescending(ib => ib.Date)
                .ToListAsync();
        }

        [HttpPost]
        public async Task<ActionResult<ImportBill>> PostImportBill(ImportBill importBill)
        {
            // Set date to UTC if not specified
            if (importBill.Date == default)
            {
                importBill.Date = DateTime.UtcNow;
            }

            _context.ImportBills.Add(importBill);
            
            // For each medication, update or create the medicine in the inventory
            foreach (var med in importBill.Medications)
            {
                var medicineNameClean = med.Name?.Trim();
                if (string.IsNullOrEmpty(medicineNameClean)) continue;

                var dbMed = await _context.Medicines
                    .FirstOrDefaultAsync(m => m.Name.ToLower() == medicineNameClean.ToLower());

                if (dbMed != null)
                {
                    // Update existing medicine stock and price
                    dbMed.StockQuantity += med.Qty;
                    dbMed.Price = med.Price;
                    dbMed.ExpiryDate = med.ExpiryDate;
                }
                else
                {
                    // Create new medicine
                    var newMed = new Medicine
                    {
                        Name = medicineNameClean,
                        ActiveIngredient = med.Code ?? "Generic",
                        Unit = med.Unit ?? "Viên",
                        Price = med.Price,
                        StockQuantity = med.Qty,
                        ExpiryDate = med.ExpiryDate
                    };
                    _context.Medicines.Add(newMed);
                }
            }

            await _context.SaveChangesAsync();

            return Ok(importBill);
        }
    }
}
