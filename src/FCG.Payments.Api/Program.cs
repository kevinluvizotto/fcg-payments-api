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

            // Configurar logging
            builder.Services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug);
            });

            // Configurar banco SQL Server
            var connectionString = builder.Configuration["ConnectionStrings:FCGDatabase"]
                ?? throw new InvalidOperationException("Connection string 'FCGDatabase' não está configurada.");
            builder.Services.AddDbContext<PaymentsDbContext>(options =>
                options.UseSqlServer(connectionString));

            // Configurar autenticação JWT
            var jwtKey = builder.Configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(jwtKey))
            {
                throw new InvalidOperationException("JWT Key não está configurada no appsettings.json.");
            }

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

            // Configurar autorização com políticas
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
                options.AddPolicy("UserOrAdmin", policy => policy.RequireRole("Admin", "User"));
            });

            // Configurar Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "Insira o token JWT sem o Berear ou Aspas",
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

            // Endpoints
            app.MapGet("/health", () =>
            {
                app.Logger.LogInformation("GET /health chamado.");
                return Results.Ok("OK");
            });

            app.MapPost("/payments", async (Payment payment, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    logger.LogInformation("POST /payments chamado com userId: {UserId}, amount: {Amount}, status: {Status}",
                        payment.UserId, payment.Amount, payment.Status);
                    payment.Id = Guid.NewGuid();
                    payment.Date = DateTime.UtcNow;
                    db.Payments.Add(payment);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Pagamento criado com Id: {Id}", payment.Id);
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
                    logger.LogInformation("Retornados {Count} pagamentos.", payments.Count);
                    return payments.Any() ? Results.Ok(payments) : Results.Ok(new List<Payment>());
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
                    if (payment == null)
                    {
                        logger.LogWarning("Pagamento com Id {Id} não encontrado.", id);
                        return Results.NotFound();
                    }
                    logger.LogInformation("Pagamento encontrado: {Id}", id);
                    return Results.Ok(payment);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao obter pagamento com Id {Id}.", id);
                    return Results.StatusCode(500);
                }
            }).RequireAuthorization("UserOrAdmin");

            app.MapPut("/payments/{id}", async (Guid id, Payment updatedPayment, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    logger.LogInformation("PUT /payments/{Id} chamado com userId: {UserId}, amount: {Amount}, status: {Status}",
                        id, updatedPayment.UserId, updatedPayment.Amount, updatedPayment.Status);
                    var payment = await db.Payments.FindAsync(id);
                    if (payment == null)
                    {
                        logger.LogWarning("Pagamento com Id {Id} não encontrado.", id);
                        return Results.NotFound();
                    }
                    payment.UserId = updatedPayment.UserId;
                    payment.Amount = updatedPayment.Amount;
                    payment.Status = updatedPayment.Status;
                    payment.Date = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    logger.LogInformation("Pagamento com Id {Id} atualizado.", id);
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao atualizar pagamento com Id {Id}.", id);
                    return Results.StatusCode(500);
                }
            }).RequireAuthorization("AdminOnly");

            app.MapDelete("/payments/{id}", async (Guid id, PaymentsDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    logger.LogInformation("DELETE /payments/{Id} chamado.", id);
                    var payment = await db.Payments.FindAsync(id);
                    if (payment == null)
                    {
                        logger.LogWarning("Pagamento com Id {Id} não encontrado.", id);
                        return Results.NotFound();
                    }
                    db.Payments.Remove(payment);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Pagamento com Id {Id} excluído.", id);
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao excluir pagamento com Id {Id}.", id);
                    return Results.StatusCode(500);
                }
            }).RequireAuthorization("AdminOnly");

            app.Run();
        }
    }
}