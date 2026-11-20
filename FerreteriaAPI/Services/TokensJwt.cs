using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FerreteriaAPI.Models;
using Microsoft.IdentityModel.Tokens;

namespace FerreteriaAPI.Services;

// Emisión y validación de los tokens de sesión (JWT HS256).
// La clave se lee de la variable de entorno Jwt__Clave (Render). Si no está configurada se genera
// una clave aleatoria al arrancar: la API sigue protegida, pero cada reinicio/despliegue invalida
// los tokens y los usuarios deben volver a iniciar sesión.
public class TokensJwt
{
    public const string Emisor = "la-micho-heladeria-backend";
    public const string Audiencia = "la-micho-heladeria-frontend";
    public const string ClaimRol = "rol";
    public static readonly TimeSpan Duracion = TimeSpan.FromHours(8); // igual que la sesión del frontend

    public SymmetricSecurityKey Clave { get; }

    public TokensJwt(IConfiguration configuracion, ILogger<TokensJwt> logger)
    {
        var claveTexto = configuracion["Jwt:Clave"];
        if (string.IsNullOrWhiteSpace(claveTexto) || Encoding.UTF8.GetByteCount(claveTexto) < 32)
        {
            logger.LogWarning("Jwt:Clave no está configurada (o tiene menos de 32 bytes). Se usa una clave temporal: " +
                              "los tokens dejarán de valer en cada reinicio. Configure la variable de entorno Jwt__Clave en Render.");
            Clave = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        }
        else
        {
            Clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(claveTexto));
        }
    }

    public TokenValidationParameters ParametrosValidacion() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Emisor,
        ValidateAudience = true,
        ValidAudience = Audiencia,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = Clave,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        RoleClaimType = ClaimRol,
        NameClaimType = JwtRegisteredClaimNames.Email
    };

    public (string Token, DateTime ExpiraUtc) Emitir(Usuarios usuario)
    {
        var expira = DateTime.UtcNow.Add(Duracion);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.IdUsuario.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
            new Claim(ClaimRol, usuario.IdRol.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: Emisor,
            audience: Audiencia,
            claims: claims,
            expires: expira,
            signingCredentials: new SigningCredentials(Clave, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
