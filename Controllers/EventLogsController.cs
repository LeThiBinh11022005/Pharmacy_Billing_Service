using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyBillingService.Data;
using PharmacyBillingService.Models;

namespace PharmacyBillingService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin, Pharmacist, Cashier")]
    public class EventLogsController : ControllerBase
    {
        private readonly PharmacyDbContext _context;

        public EventLogsController(PharmacyDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EventLog>>> GetEventLogs()
        {
            return await _context.EventLogs.OrderByDescending(e => e.Timestamp).ToListAsync();
        }
    }
}
