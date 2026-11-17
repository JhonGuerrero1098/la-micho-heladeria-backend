using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;

namespace FerreteriaAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RolesController : ControllerBase
    {
        private readonly FerreteriaContext _context;

        public RolesController(FerreteriaContext context)
        {
            _context = context;
        }

        
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Roles>>> GetRoles()
        {
            return await _context.Roles.AsNoTracking().ToListAsync();
        }

        
        [HttpGet("{id}")]
        public async Task<ActionResult<Roles>> GetRol(int id)
        {
            var rol = await _context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.IdRol == id);

            if (rol == null)
            {
                return NotFound();
            }

            return rol;
        }
    }
}