using FCG.Payments.Api;
using FCG.Payments.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

            // Configurar banco in-memory
            builder.Services.AddDbContext<PaymentsDbContext>(options =>
                options.UseInMemoryDatabase("PaymentsDb"));

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
                        ValidateIssuer = false,
                        ValidateAudience = false,
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
                            Console.WriteLine($"Falha na autenticação: {context.Exception.Message}");
                            return Task.CompletedTask;
                        }
                    };
                });

            // Configurar autorização e Swagger
            builder.Services.AddAuthorization();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "Insira o token JWT",
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
            app.MapGet("/health", () => "OK");

            app.MapPost("/payments", async (Payment payment, PaymentsDbContext db) =>
            {
                payment.Id = Guid.NewGuid();
                db.Payments.Add(payment);
                await db.SaveChangesAsync();
                return Results.Created($"/payments/{payment.Id}", payment);
            }).RequireAuthorization();

            app.MapGet("/payments", async (PaymentsDbContext db) =>
            {
                var payments = await db.Payments.ToListAsync();
                return payments.Any() ? Results.Ok(payments) : Results.Ok(new List<Payment>());
            }).RequireAuthorization();

            app.MapGet("/payments/{id}", async (Guid id, PaymentsDbContext db) =>
            {
                var payment = await db.Payments.FindAsync(id);
                return payment is null ? Results.NotFound() : Results.Ok(payment);
            }).RequireAuthorization();

            app.MapPut("/payments/{id}", async (Guid id, Payment updatedPayment, PaymentsDbContext db) =>
            {
                var payment = await db.Payments.FindAsync(id);
                if (payment is null)
                    return Results.NotFound();
                payment.UserId = updatedPayment.UserId;
                payment.Amount = updatedPayment.Amount;
                payment.Status = updatedPayment.Status;
                await db.SaveChangesAsync();
                return Results.NoContent();
            }).RequireAuthorization();

            app.MapDelete("/payments/{id}", async (Guid id, PaymentsDbContext db) =>
            {
                var payment = await db.Payments.FindAsync(id);
                if (payment is null)
                    return Results.NotFound();
                db.Payments.Remove(payment);
                await db.SaveChangesAsync();
                return Results.Ok();
            }).RequireAuthorization();

            app.Run();
        }
    }
}