using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;

namespace FerreteriaAPI.Services;

// =========================================================================
// SMS (Twilio, API REST sin SDK)
// Configuración por variables de entorno en Render (nunca en el código):
//   Sms__AccountSid     SID de la cuenta de Twilio (AC...)
//   Sms__AuthToken      Auth Token de Twilio
//   Sms__Remitente      Número Twilio que envía, en formato E.164 (ej. +1XXXXXXXXXX)
//   Sms__TelefonoAdmin  (opcional) celular que recibe las alertas de stock bajo (ej. 3001234567)
// Sin AccountSid/AuthToken/Remitente el envío queda desactivado y se registra en los logs.
//
// Los mensajes se encolan DESPUÉS del commit de la venta y los envía un servicio en segundo plano:
// la respuesta al cajero nunca espera al proveedor, y si Twilio falla o tarda solo queda en los logs.
// =========================================================================
public record MensajeSms(string Destino, string Texto, string Motivo);

public class ColaSms
{
    // Cola acotada: si el proveedor está caído no se acumula memoria sin límite
    private readonly Channel<MensajeSms> _canal = Channel.CreateBounded<MensajeSms>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private readonly ILogger<ColaSms> _logger;

    public ColaSms(ILogger<ColaSms> logger) => _logger = logger;

    public ChannelReader<MensajeSms> Lector => _canal.Reader;

    // Nunca lanza: un número inválido o la cola llena no deben afectar la venta ya guardada
    public void Encolar(string? telefono, string texto, string motivo)
    {
        var destino = TelefonoColombia.AE164(telefono);
        if (destino == null)
        {
            _logger.LogInformation("SMS ({Motivo}) omitido: teléfono vacío o inválido ('{Telefono}')", motivo, telefono);
            return;
        }
        if (!_canal.Writer.TryWrite(new MensajeSms(destino, texto, motivo)))
        {
            _logger.LogWarning("SMS ({Motivo}) descartado: la cola de envío está cerrada", motivo);
        }
    }
}

public static class TelefonoColombia
{
    // Normaliza a E.164 con indicativo +57. Acepta "300 123 4567", "3001234567", "573001234567",
    // "+57 300-123-4567" y fijos de 10 dígitos (601...). Otros números con "+" se respetan.
    public static string? AE164(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono)) return null;
        var texto = telefono.Trim();
        var digitos = new string(texto.Where(char.IsDigit).ToArray());

        if (texto.StartsWith('+'))
        {
            return digitos.Length is >= 8 and <= 15 ? "+" + digitos : null;
        }
        if (digitos.StartsWith("00")) digitos = digitos[2..];            // 0057...
        if (digitos.Length == 12 && digitos.StartsWith("57")) return "+" + digitos;
        if (digitos.Length == 10 && (digitos[0] == '3' || digitos[0] == '6')) return "+57" + digitos;
        return null;
    }
}

public class EnvioSmsService : BackgroundService
{
    private static readonly TimeSpan TiempoMaximoPorEnvio = TimeSpan.FromSeconds(15);

    private readonly ColaSms _cola;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EnvioSmsService> _logger;

    public EnvioSmsService(ColaSms cola, IHttpClientFactory httpFactory, IConfiguration config, ILogger<EnvioSmsService> logger)
    {
        _cola = cola;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sid = _config["Sms:AccountSid"];
        var token = _config["Sms:AuthToken"];
        var remitente = _config["Sms:Remitente"];
        var configurado = !string.IsNullOrWhiteSpace(sid) && !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(remitente);

        if (!configurado)
        {
            _logger.LogWarning("Envío de SMS DESACTIVADO: faltan las variables de entorno Sms__AccountSid, Sms__AuthToken y/o Sms__Remitente.");
        }
        else
        {
            _logger.LogInformation("Envío de SMS activo (Twilio, remitente {Remitente})", remitente);
        }

        try
        {
            await foreach (var mensaje in _cola.Lector.ReadAllAsync(stoppingToken))
            {
                if (!configurado)
                {
                    _logger.LogWarning("SMS ({Motivo}) a {Destino} no enviado: proveedor sin configurar", mensaje.Motivo, Enmascarar(mensaje.Destino));
                    continue;
                }
                await EnviarAsync(mensaje, sid!, token!, remitente!, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado de la aplicación
        }
    }

    private async Task EnviarAsync(MensajeSms mensaje, string sid, string token, string remitente, CancellationToken stoppingToken)
    {
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            limite.CancelAfter(TiempoMaximoPorEnvio);

            var http = _httpFactory.CreateClient("twilio");
            using var peticion = new HttpRequestMessage(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/Messages.json")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["To"] = mensaje.Destino,
                    ["From"] = remitente,
                    ["Body"] = mensaje.Texto
                })
            };
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}")));

            using var respuesta = await http.SendAsync(peticion, limite.Token);
            if (respuesta.IsSuccessStatusCode)
            {
                _logger.LogInformation("SMS ({Motivo}) enviado a {Destino}", mensaje.Motivo, Enmascarar(mensaje.Destino));
            }
            else
            {
                var cuerpo = await respuesta.Content.ReadAsStringAsync(limite.Token);
                _logger.LogError("SMS ({Motivo}) a {Destino} rechazado por Twilio: HTTP {Codigo} {Cuerpo}",
                    mensaje.Motivo, Enmascarar(mensaje.Destino), (int)respuesta.StatusCode, cuerpo);
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError("SMS ({Motivo}) a {Destino}: Twilio no respondió en {Segundos} s", mensaje.Motivo, Enmascarar(mensaje.Destino), TiempoMaximoPorEnvio.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SMS ({Motivo}) a {Destino}: error al contactar a Twilio", mensaje.Motivo, Enmascarar(mensaje.Destino));
        }
    }

    // Los logs no guardan el número completo del cliente
    private static string Enmascarar(string telefono) =>
        telefono.Length <= 4 ? "****" : new string('*', telefono.Length - 4) + telefono[^4..];
}
