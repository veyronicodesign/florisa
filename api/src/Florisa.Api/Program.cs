using System.Threading.RateLimiting;
using Florisa.Api.Contratos;
using Florisa.Api.Dados;
using Florisa.Api.Dominio;
using Florisa.Api.Endpoints;
using Florisa.Api.Servicos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Banco: SQLite para desenvolver, PostgreSQL em produção ----------
builder.Services.AddDbContext<FlorisaDb>(o =>
{
    var conexao = config.GetConnectionString("Florisa") ?? "Data Source=florisa.db";
    if (string.Equals(config["BancoDeDados"], "postgres", StringComparison.OrdinalIgnoreCase)) o.UseNpgsql(conexao);
    else o.UseSqlite(conexao);
});

// ---------- Serviços ----------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HorarioLoja>();
builder.Services.AddScoped<ServicoFrete>();
builder.Services.AddScoped<ServicoPedidos>();
builder.Services.AddScoped<ServicoCodigos>();
builder.Services.AddSingleton<ServicoTokens>();
builder.Services.AddSingleton<IEnvioMensagens, EnvioMensagensLog>();

// Provedor de pagamento. Hoje só existe o simulado; o real entra aqui quando a Florisa escolher.
var provedor = config["Pagamento:Provedor"] ?? "simulado";
switch (provedor)
{
    case "simulado":
        builder.Services.AddScoped<IGatewayPagamento, GatewaySimulado>();
        break;
    default:
        throw new InvalidOperationException($"Provedor de pagamento desconhecido: {provedor}");
}

// ---------- Login (JWT) ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = ServicoTokens.Emissor(config),
        ValidAudience = ServicoTokens.Emissor(config),
        IssuerSigningKey = ServicoTokens.Chave(config),
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Cliente", p => p.RequireClaim(Claims.Tipo, "cliente"));
    o.AddPolicy("Equipe", p => p.RequireClaim(Claims.Tipo, "equipe"));
    o.AddPolicy("Atendimento", p => p.RequireClaim(Claims.Tipo, "equipe").RequireClaim(Claims.Papel, Papeis.Admin, Papeis.Atendente));
    o.AddPolicy("Estoque", p => p.RequireClaim(Claims.Tipo, "equipe").RequireClaim(Claims.Papel, Papeis.Admin, Papeis.Estoque));
    o.AddPolicy("Admin", p => p.RequireClaim(Claims.Tipo, "equipe").RequireClaim(Claims.Papel, Papeis.Admin));
});

// ---------- CORS: o site fica em outro domínio ----------
var origens = config.GetSection("Cors:Origens").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origens).AllowAnyHeader().AllowAnyMethod()));

// ---------- Limite de chamadas (evita abuso no envio de código e criação de pedidos) ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
        await ctx.HttpContext.Response.WriteAsJsonAsync(new ErroResp("Muitas tentativas. Espere um pouco e tente de novo."), ct);
    o.AddPolicy("codigos", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("Limites:CodigosPorMinuto", 10), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("pedidos", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("Limites:PedidosPorMinuto", 20), Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

var app = builder.Build();

// ---------- Erros sempre no formato { "mensagem": "..." } ----------
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var erro = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, mensagem) = erro switch
    {
        RegraException r => (r.Status, r.Message),
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "Dados inválidos. Confira e tente de novo."),
        _ => (StatusCodes.Status500InternalServerError, "Tivemos um problema aqui. Tente de novo em instantes ou chame no WhatsApp.")
    };
    if (status >= 500) app.Logger.LogError(erro, "Erro não tratado");
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new ErroResp(mensagem));
}));

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ---------- Banco: cria as tabelas e os dados iniciais ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FlorisaDb>();
    await db.Database.EnsureCreatedAsync();
    await Semente.AplicarAsync(db, config, scope.ServiceProvider.GetRequiredService<TimeProvider>());
}

app.MapGet("/", () => Results.Ok(new { servico = "Florisa API", pagamento = provedor }));
app.MapGet("/saude", () => Results.Ok(new { ok = true }));
app.MapLoja();
app.MapPainel();

app.Run();

public partial class Program { }
