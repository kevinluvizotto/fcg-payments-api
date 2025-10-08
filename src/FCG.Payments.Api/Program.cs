using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using FCG.Payments.Api.Models;

namespace FCG.Payments.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 🧩 Logging
            builder.Services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug);
            });

            // 💾 Banco SQL Server
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

            // 🔒 Políticas de autorização
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
                options.AddPolicy("UserOrAdmin", policy => policy.RequireRole("Admin", "User"));
            });

            // 🧾 Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "Insira o token JWT sem o 'Bearer' ou aspas.",
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

            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseAuthentication();
            app.UseAuthorization();

            // 🌡 Health check
            app.MapGet("/health", () =>
            {
                app.Logger.LogInformation("GET /health chamado.");
                return Results.Ok("OK");
            });

            // 🧾 CRUD de pagamentos padrão
            app.MapPost("/payments", async (Payment payment, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    logger.LogInformation("POST /payments chamado por {UserId}", payment.UserId);
                    payment.Id = Guid.NewGuid();
                    payment.Date = DateTime.UtcNow;
                    db.Payments.Add(payment);
                    await db.SaveChangesAsync();
                    return Results.Created($"/payments/{payment.Id}", payment);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao criar pagamento.");
                    return Results.StatusCode(500);
                }
            }).RequireAuthorization("UserOrAdmin");

            app.MapGet("/payments", async (PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    logger.LogInformation("GET /payments chamado.");
                    var payments = await db.Payments.ToListAsync();
                    return Results.Ok(payments);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao listar pagamentos.");
                    return Results.StatusCode(500);
                }
            }).RequireAuthorization("AdminOnly");

            app.MapGet("/payments/{id}", async (Guid id, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    logger.LogInformation("GET /payments/{Id} chamado.", id);
                    var payment = await db.Payments.FindAsync(id);
                    return payment is null ? Results.NotFound() : Results.Ok(payment);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao obter pagamento com Id {Id}.", id);
                    return Results.StatusCode(500);
                }
            }).RequireAuthorization("UserOrAdmin");

            app.MapPut("/payments/{id}", async (Guid id, Payment updated, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    var payment = await db.Payments.FindAsync(id);
                    if (payment is null) return Results.NotFound();

                    payment.UserId = updated.UserId;
                    payment.Amount = updated.Amount;
                    payment.Status = updated.Status;
                    payment.Date = DateTime.UtcNow;

                    await db.SaveChangesAsync();
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao atualizar pagamento {Id}", id);
                    return Results.StatusCode(500);
                }
            }).RequireAuthorization("AdminOnly");

            app.MapDelete("/payments/{id}", async (Guid id, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    var payment = await db.Payments.FindAsync(id);
                    if (payment is null) return Results.NotFound();

                    db.Payments.Remove(payment);
                    await db.SaveChangesAsync();
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao excluir pagamento {Id}", id);
                    return Results.StatusCode(500);
                }
            }).RequireAuthorization("AdminOnly");

            // 💳 NOVO ENDPOINT: Comprar jogo
            app.MapPost("/payments/buy", async (HttpContext http, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    var user = http.User;
                    if (user?.Identity == null || !user.Identity.IsAuthenticated)
                        return Results.Unauthorized();

                    var gameId = http.Request.Query["gameId"].ToString();
                    if (string.IsNullOrEmpty(gameId))
                        return Results.BadRequest(new { error = "Parâmetro 'gameId' é obrigatório." });

                    var userId = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
                        ?? user.Identity.Name
                        ?? "desconhecido";

                    var payment = new Payment
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        Amount = 100.00m, // 💡 valor fixo de exemplo; integrar com Games API futuramente
                        Status = "Completed",
                        Date = DateTime.UtcNow
                    };

                    db.Payments.Add(payment);
                    await db.SaveChangesAsync();

                    logger.LogInformation("Compra registrada: GameId={GameId}, User={UserId}", gameId, userId);

                    return Results.Ok(new
                    {
                        message = "Compra realizada com sucesso!",
                        paymentId = payment.Id,
                        userId,
                        gameId
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao processar compra.");
                    return Results.Json(new { error = "Erro interno ao processar compra." }, statusCode: 500);
                }
            })
            .RequireAuthorization("UserOrAdmin")
            .WithTags("Payments");

            // 🚀 Iniciar app
            app.Run();
        }
    }
}
