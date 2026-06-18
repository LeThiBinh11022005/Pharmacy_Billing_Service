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
            if (importBill.Date == default)
            {
                importBill.Date = DateTime.UtcNow;
            }

            // Auto-generate code PN0001, PN0002...
            var maxId = await _context.ImportBills.MaxAsync(ib => (int?)ib.Id) ?? 0;
            importBill.Code = "PN" + (maxId + 1).ToString("D4");

            _context.ImportBills.Add(importBill);

            var medicineChanges = new List<object>();

            foreach (var med in importBill.Medications)
            {
                var medicineNameClean = med.Name?.Trim();
                if (string.IsNullOrEmpty(medicineNameClean)) continue;

                var dbMed = await _context.Medicines
                    .FirstOrDefaultAsync(m => m.Name.ToLower() == medicineNameClean.ToLower());

                int beforeStock = 0;
                if (dbMed != null)
                {
                    beforeStock = dbMed.StockQuantity;
                    dbMed.StockQuantity += med.Qty;
                    dbMed.Price = med.Price;
                    dbMed.ExpiryDate = med.ExpiryDate;
                }
                else
                {
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
                    beforeStock = 0;
                }

                medicineChanges.Add(new
                {
                    medicineName = medicineNameClean,
                    medicineCode = med.Code,
                    qty = med.Qty,
                    beforeStock,
                    afterStock = dbMed?.StockQuantity ?? med.Qty,
                    batch = med.Batch,
                    unit = med.Unit
                });
            }

            await _context.SaveChangesAsync();

            // Log import event for inventory history
            _context.EventLogs.Add(new EventLog
            {
                EventType = "import.created",
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    importBillId = importBill.Id,
                    code = importBill.Code,
                    supplierCode = importBill.SupplierCode,
                    supplierName = importBill.SupplierName,
                    medicines = medicineChanges
                }),
                Status = "Success",
                Timestamp = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(importBill);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ImportBill>> PutImportBill(int id, ImportBill importBill)
        {
            var existing = await _context.ImportBills
                .Include(ib => ib.Medications)
                .FirstOrDefaultAsync(ib => ib.Id == id);

            if (existing == null) return NotFound();

            existing.SupplierCode = importBill.SupplierCode;
            existing.SupplierName = importBill.SupplierName;
            existing.Date = importBill.Date;
            existing.Creator = importBill.Creator;
            existing.Note = importBill.Note;
            existing.GoodsTotal = importBill.GoodsTotal;
            existing.DiscountTotal = importBill.DiscountTotal;
            existing.VatTotal = importBill.VatTotal;
            existing.FinalTotal = importBill.FinalTotal;

            _context.ImportBillMedications.RemoveRange(existing.Medications);
            existing.Medications = importBill.Medications.Select(m => new ImportBillMedication
            {
                Code = m.Code,
                Name = m.Name,
                Batch = m.Batch,
                ExpiryDate = m.ExpiryDate,
                Qty = m.Qty,
                Unit = m.Unit,
                Price = m.Price,
                Total = m.Total
            }).ToList();

            await _context.SaveChangesAsync();

            return Ok(existing);
        }
    }
}
