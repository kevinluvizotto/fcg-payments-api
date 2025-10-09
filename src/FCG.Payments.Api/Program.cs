using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using FCG.Payments.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FCG.Payments.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 🧾 Logging
            builder.Services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            });

            // 💾 Banco de dados SQL Server
            var connectionString = builder.Configuration["ConnectionStrings:FCGDatabase"]
                ?? throw new InvalidOperationException("Connection string 'FCGDatabase' não está configurada.");
            builder.Services.AddDbContext<PaymentsDbContext>(options =>
                options.UseSqlServer(connectionString));

            // 🔐 Autenticação JWT
            var jwtKey = builder.Configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(jwtKey))
                throw new InvalidOperationException("JWT Key não está configurada no appsettings.json.");

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        context.NoResult();
                        context.Response.StatusCode = 401;
                        Console.WriteLine($"Falha na autenticação: {context.Exception.Message}");
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        Console.WriteLine("Token inválido ou ausente.");
                        return context.Response.WriteAsync("{\"error\": \"Token inválido ou ausente.\"}");
                    }
                };
            });

            // 🧩 Autorização
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
                options.AddPolicy("UserOrAdmin", policy => policy.RequireRole("Admin", "User"));
            });

            // 📘 Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "Insira o token JWT sem o prefixo 'Bearer'",
                    Name = "Authorization",
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                });
                c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    {
                        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                        {
                            Reference = new Microsoft.OpenApi.Models.OpenApiReference
                            {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            var app = builder.Build();

            // 🌐 Pipeline
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseAuthentication();
            app.UseAuthorization();

            // ✅ Health check
            app.MapGet("/health", () =>
            {
                app.Logger.LogInformation("GET /health chamado.");
                return Results.Ok("OK");
            });

            // ✅ Criar pagamento
            app.MapPost("/payments", async (Payment payment, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    payment.Id = Guid.NewGuid();
                    payment.Date = DateTime.UtcNow;
                    db.Payments.Add(payment);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Pagamento criado com sucesso: {PaymentId}", payment.Id);
                    return Results.Created($"/payments/{payment.Id}", payment);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao criar pagamento.");
                    return Results.Json(new { error = "Erro interno ao criar pagamento." }, statusCode: 500);
                }
            }).RequireAuthorization("UserOrAdmin");

            // ✅ Listar pagamentos (Admin)
            app.MapGet("/payments", async (PaymentsDbContext db) =>
            {
                var payments = await db.Payments.ToListAsync();
                return Results.Ok(payments);
            }).RequireAuthorization("AdminOnly");

            // ✅ Buscar pagamento por Id
            app.MapGet("/payments/{id}", async (Guid id, PaymentsDbContext db) =>
            {
                var payment = await db.Payments.FindAsync(id);
                return payment is null ? Results.NotFound() : Results.Ok(payment);
            }).RequireAuthorization("UserOrAdmin");

            // ✅ Atualizar pagamento (Admin)
            app.MapPut("/payments/{id}", async (Guid id, Payment updated, PaymentsDbContext db) =>
            {
                var payment = await db.Payments.FindAsync(id);
                if (payment is null) return Results.NotFound();

                payment.UserId = updated.UserId;
                payment.Amount = updated.Amount;
                payment.Status = updated.Status;
                payment.Date = DateTime.UtcNow;
                await db.SaveChangesAsync();

                return Results.NoContent();
            }).RequireAuthorization("AdminOnly");

            // ✅ Excluir pagamento (Admin)
            app.MapDelete("/payments/{id}", async (Guid id, PaymentsDbContext db) =>
            {
                var payment = await db.Payments.FindAsync(id);
                if (payment is null) return Results.NotFound();

                db.Payments.Remove(payment);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }).RequireAuthorization("AdminOnly");

            // 🛒 NOVO — Endpoint de compra completa (pagamento + biblioteca)
            app.MapPost("/payments/buy", async (
                [FromQuery] Guid? gameId,
                HttpContext http,
                PaymentsDbContext db,
                ILogger<Program> logger,
                IConfiguration config) =>
            {
                try
                {
                    if (gameId == null || gameId == Guid.Empty)
                        return Results.BadRequest(new { error = "O parâmetro gameId é obrigatório." });

                    // 📦 Extrai o UserId do token JWT
                    var userId = http.User.Claims.FirstOrDefault(c =>
                        c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

                    if (string.IsNullOrEmpty(userId))
                        return Results.Unauthorized();

                    logger.LogInformation("🛒 Iniciando compra — UserId: {UserId}, GameId: {GameId}", userId, gameId);

                    // 💳 Cria o pagamento local
                    var payment = new Payment
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        Amount = 100.00m,
                        Status = "Completed",
                        Date = DateTime.UtcNow
                    };

                    db.Payments.Add(payment);
                    await db.SaveChangesAsync();

                    logger.LogInformation("💾 Pagamento registrado com sucesso (Id: {Id})", payment.Id);

                    // 🎮 Adiciona o jogo na biblioteca via Users API
                    var usersApiUrl = config["UsersApiUrl"] ?? "https://fcg-apim-fiap-klztt.azure-api.net/users";
                    var addGameUrl = $"{usersApiUrl}/users/me/games?gameId={gameId}";

                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("Authorization", http.Request.Headers["Authorization"].ToString());
                    client.DefaultRequestHeaders.Add("Accept", "application/json");

                    // 🚀 Faz a requisição sem body (Users API espera o gameId na query)
                    var response = await client.PostAsync(addGameUrl, null);

                    var body = await response.Content.ReadAsStringAsync();
                    logger.LogInformation("📡 Chamada à Users API => {Url} | Status: {StatusCode} | Body: {Body}", addGameUrl, response.StatusCode, body);

                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning("⚠️ Falha ao registrar jogo na biblioteca: {Body}", body);
                        return Results.Json(new
                        {
                            warning = "Pagamento criado, mas não foi possível adicionar o jogo à biblioteca.",
                            payment.Id,
                            payment.UserId,
                            GameId = gameId.ToString()
                        }, statusCode: 202);
                    }

                    logger.LogInformation("🎮 Jogo adicionado à biblioteca do usuário {UserId}", userId);

                    return Results.Created($"/payments/{payment.Id}", new
                    {
                        message = "Compra realizada e jogo adicionado à biblioteca com sucesso!",
                        payment.Id,
                        payment.UserId,
                        GameId = gameId.ToString()
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao processar compra completa.");
                    return Results.Json(new { error = "Erro interno ao processar compra." }, statusCode: 500);
                }
            }).RequireAuthorization("UserOrAdmin");

            app.Run();
        }
    }
}
