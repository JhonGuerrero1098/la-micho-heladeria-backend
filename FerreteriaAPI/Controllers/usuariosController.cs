using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;
using Npgsql;

namespace FerreteriaAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsuariosController : ControllerBase
    {
        private readonly FerreteriaContext _context;
        private readonly IPasswordHasher<Usuarios> _passwordHasher;

        public UsuariosController(FerreteriaContext context, IPasswordHasher<Usuarios> passwordHasher)
        {
            _context = context;
            _passwordHasher = passwordHasher;
        }

        // GET: api/Usuarios
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UsuarioDto>>> GetUsuarios()
        {
            // Proyección: no se lee la columna del hash de la contraseña
            return await _context.Usuarios
                .AsNoTracking()
                .Select(u => new UsuarioDto
                {
                    IdUsuario = u.IdUsuario,
                    NombreCompleto = u.NombreCompleto,
                    Email = u.Email,
                    IdRol = u.IdRol,
                    Estado = u.Estado,
                    FechaRegistro = u.FechaRegistro
                })
                .ToListAsync();
        }

        // POST: api/Usuarios/Registro
        [HttpPost("Registro")]
        public async Task<IActionResult> Registro([FromBody] UsuarioRegistroDto dto)
        {
            // BLOQUEO DE SEGURIDAD: Nadie puede crear un Superusuario (Rol 3) desde la app web/API
            if (dto.IdRol == 3)
            {
                return BadRequest(new { exito = false, mensaje = "Por seguridad, las cuentas con rol de Superusuario solo pueden ser creadas directamente desde la base de datos." });
            }

            try
            {
                // Normalizar el correo para evitar duplicados por mayúsculas, minúsculas o espacios accidentales
                var emailLimpio = dto.Email.Trim().ToLower();

                // Validar si el correo ya está registrado en la base de datos
                var existeCorreo = await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == emailLimpio);
                if (existeCorreo)
                {
                    return BadRequest(new { exito = false, mensaje = "El registro ya existe: hay un usuario registrado con este correo electrónico." });
                }

                var nuevoUsuario = new Usuarios
                {
                    NombreCompleto = dto.NombreCompleto,
                    Email = emailLimpio, // Guardar normalizado
                    IdRol = dto.IdRol,
                    Estado = true,
                    FechaRegistro = DateTime.UtcNow
                };

                // Hashear la contraseña de forma segura
                nuevoUsuario.ContrasenaHash = _passwordHasher.HashPassword(nuevoUsuario, dto.Contrasena);

                _context.Usuarios.Add(nuevoUsuario);
                await _context.SaveChangesAsync();

                return Ok(new { exito = true, mensaje = "Usuario creado con éxito.", usuario = nuevoUsuario });
            }
            catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Captura definitiva (23505) por si un reintento de red o concurrencia intenta duplicar el correo en Supabase
                return BadRequest(new { exito = false, mensaje = "El registro ya existe: el correo electrónico ya se encuentra registrado." });
            }
            catch (DbUpdateException dbEx)
            {
                return BadRequest(new { exito = false, mensaje = "No se pudo registrar el usuario.", detalle = dbEx.InnerException?.Message ?? dbEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = "Ocurrió un error al registrar el usuario.", detalle = ex.InnerException?.Message ?? ex.Message });
            }
        }

        // POST: api/Usuarios/Login
        [HttpPost("Login")]
        public async Task<ActionResult> Login(UsuarioLoginDto dto)
        {
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (usuario == null || usuario.Estado == false)
            {
                return Unauthorized("Correo o contraseña incorrectos.");
            }

            PasswordVerificationResult resultado;
            try
            {
                resultado = _passwordHasher.VerifyHashedPassword(usuario, usuario.ContrasenaHash, dto.Contrasena);
            }
            catch (FormatException)
            {
                // Usuarios antiguos con la contraseña guardada en texto plano (no es un hash válido)
                resultado = PasswordVerificationResult.Failed;
            }

            if (resultado == PasswordVerificationResult.Failed)
            {
                return Unauthorized("Correo o contraseña incorrectos.");
            }

            // El hash fue generado con parámetros antiguos: se actualiza al formato actual
            if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
            {
                usuario.ContrasenaHash = _passwordHasher.HashPassword(usuario, dto.Contrasena);
                await _context.SaveChangesAsync();
            }

            return Ok(new { mensaje = "Inicio de sesión exitoso", usuario });
        }
    }
}