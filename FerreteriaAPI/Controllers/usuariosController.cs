using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;
using FerreteriaAPI.Services;
using Npgsql;

namespace FerreteriaAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsuariosController : ControllerBase
    {
        private readonly FerreteriaContext _context;
        private readonly IPasswordHasher<Usuarios> _passwordHasher;
        private readonly TokensJwt _tokens;
        private readonly ILogger<UsuariosController> _logger;

        // Roles: 1 = Administrador, 2 = Cajero, 3 = Superusuario
        private const string RolesGestion = "1,3";

        public UsuariosController(FerreteriaContext context, IPasswordHasher<Usuarios> passwordHasher, TokensJwt tokens, ILogger<UsuariosController> logger)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _tokens = tokens;
            _logger = logger;
        }

        // GET: api/Usuarios (solo Administrador o Superusuario)
        [HttpGet]
        [Authorize(Roles = RolesGestion)]
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
        // Antes era público: cualquiera podía crearse una cuenta de Administrador. Ahora exige sesión de
        // Administrador (solo crea Cajeros) o Superusuario (crea Administradores o Cajeros).
        [HttpPost("Registro")]
        [Authorize(Roles = RolesGestion)]
        public async Task<IActionResult> Registro([FromBody] UsuarioRegistroDto dto)
        {
            // BLOQUEO DE SEGURIDAD: Nadie puede crear un Superusuario (Rol 3) desde la app web/API
            if (dto.IdRol == 3)
            {
                return BadRequest(new { exito = false, mensaje = "Por seguridad, las cuentas con rol de Superusuario solo pueden ser creadas directamente desde la base de datos." });
            }
            if (dto.IdRol != 1 && dto.IdRol != 2)
            {
                return BadRequest(new { exito = false, mensaje = "Rol no válido." });
            }
            if (User.IsInRole("1") && dto.IdRol != 2)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { exito = false, mensaje = "Un Administrador solo puede registrar cuentas de Cajero." });
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
                // El detalle técnico va a los logs, no al cliente
                _logger.LogError(dbEx, "Error de base de datos al registrar usuario");
                return BadRequest(new { exito = false, mensaje = "No se pudo registrar el usuario." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al registrar usuario");
                return StatusCode(500, new { exito = false, mensaje = "Ocurrió un error al registrar el usuario." });
            }
        }

        // POST: api/Usuarios/Login
        // Devuelve el usuario y un token JWT (8 h) que el frontend envía en "Authorization: Bearer ...".
        [HttpPost("Login")]
        [AllowAnonymous]
        public async Task<ActionResult> Login(UsuarioLoginDto dto)
        {
            // Los correos se registran en minúsculas: se normaliza para no fallar por mayúsculas/espacios
            var emailLimpio = (dto.Email ?? string.Empty).Trim().ToLower();
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email.ToLower() == emailLimpio); // también usuarios antiguos con mayúsculas
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

            var (token, expiraUtc) = _tokens.Emitir(usuario);
            return Ok(new { mensaje = "Inicio de sesión exitoso", usuario, token, expiraEn = expiraUtc });
        }
    }
}