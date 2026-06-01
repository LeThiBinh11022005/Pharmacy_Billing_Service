using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyBillingService.Data;

namespace PharmacyBillingService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly PharmacyDbContext _context;

        public DashboardController(PharmacyDbContext context)
        {
            _context = context;
        }

        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            var totalMedicines = await _context.Medicines.CountAsync();
            var today = DateTime.UtcNow.Date;
            
            var todayBills = await _context.Bills
                .Where(b => b.CreatedAt.Date == today)
                .CountAsync();

            var totalRevenue = await _context.Bills
                .Where(b => b.Status == "Paid")
                .SumAsync(b => b.TotalAmount);

            // Chart data for the last 7 days
            var last7Days = Enumerable.Range(0, 7).Select(i => today.AddDays(-i)).ToList();
            
            var revenueChart = new List<object>();
            foreach(var day in last7Days.OrderBy(d => d))
            {
                var dailyRevenue = await _context.Bills
                    .Where(b => b.CreatedAt.Date == day && b.Status == "Paid")
                    .SumAsync(b => b.TotalAmount);
                
                revenueChart.Add(new { date = day.ToString("MM/dd"), revenue = dailyRevenue });
            }

            return Ok(new
            {
                TotalMedicines = totalMedicines,
                TodayBills = todayBills,
                TotalRevenue = totalRevenue,
                RevenueChart = revenueChart
            });
        }
    }
}
