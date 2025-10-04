using FCG.Payments.Api;
using FCG.Payments.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FCG.Payments.Api.Tests
{
    public class PaymentsApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public PaymentsApiTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<PaymentsDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddDbContext<PaymentsDbContext>(options =>
                        options.UseInMemoryDatabase("TestPaymentsDb"));
                });
            });
            _client = _factory.CreateClient();
        }

        private string GenerateTestToken()
        {
            var key = Encoding.UTF8.GetBytes("super_secret_dev_key_1234567890_LONGER_KEY");
            var issuer = "fcg-users-api";
            var audience = "fcg-payments-api";
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "56b91968-c95e-4971-bc1b-f3605b947ecf"),
                new Claim(ClaimTypes.Email, "admin2@gmail.com"),
                new Claim(JwtRegisteredClaimNames.Iss, issuer),
                new Claim(JwtRegisteredClaimNames.Aud, audience)
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        [Fact]
        public async Task Post_Payments_WithValidToken_ReturnsCreated()
        {
            // Arrange
            var token = GenerateTestToken();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payment = new Payment
            {
                UserId = "12345",
                Amount = 99.99m,
                Status = "Pending"
            };
            var content = new StringContent(JsonSerializer.Serialize(payment), Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/payments", content);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(responseContent), "Response content should not be empty");
            var createdPayment = JsonSerializer.Deserialize<Payment>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(createdPayment);
            Assert.Equal(payment.UserId, createdPayment.UserId);
            Assert.Equal(payment.Amount, createdPayment.Amount);
            Assert.Equal(payment.Status, createdPayment.Status);
            Assert.NotEqual(Guid.Empty, createdPayment.Id);
        }

        [Fact]
        public async Task Post_Payments_WithoutToken_ReturnsUnauthorized()
        {
            // Arrange
            _client.DefaultRequestHeaders.Authorization = null;
            var payment = new Payment
            {
                UserId = "12345",
                Amount = 99.99m,
                Status = "Pending"
            };
            var content = new StringContent(JsonSerializer.Serialize(payment), Encoding.UTF8, "application/json");

            // Act
            var response = await _client.PostAsync("/payments", content);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Get_Health_ReturnsOk()
        {
            // Act
            var response = await _client.GetAsync("/health");

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("OK", content);
        }

        [Fact]
        public async Task Get_Payments_WithValidToken_ReturnsOk()
        {
            // Arrange
            var token = GenerateTestToken();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payment = new Payment { UserId = "12345", Amount = 99.99m, Status = "Pending" };
            var content = new StringContent(JsonSerializer.Serialize(payment), Encoding.UTF8, "application/json");
            var postResponse = await _client.PostAsync("/payments", content);
            Assert.Equal(System.Net.HttpStatusCode.Created, postResponse.StatusCode);

            // Act
            var response = await _client.GetAsync("/payments");

            // Assert
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.Fail($"GET /payments falhou com status {response.StatusCode}: {errorContent}");
            }
            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(responseContent), "Response content should not be empty");
            var payments = JsonSerializer.Deserialize<List<Payment>>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(payments);
            Assert.Contains(payments, p => p.UserId == payment.UserId && p.Amount == payment.Amount);
        }

        [Fact]
        public async Task Get_PaymentById_WithValidToken_ReturnsOk()
        {
            // Arrange
            var token = GenerateTestToken();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payment = new Payment { UserId = "12345", Amount = 99.99m, Status = "Pending" };
            var content = new StringContent(JsonSerializer.Serialize(payment), Encoding.UTF8, "application/json");
            var postResponse = await _client.PostAsync("/payments", content);
            Assert.Equal(System.Net.HttpStatusCode.Created, postResponse.StatusCode);
            var postContent = await postResponse.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(postContent), "Post response content should not be empty");
            var createdPayment = JsonSerializer.Deserialize<Payment>(postContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(createdPayment);

            // Act
            var response = await _client.GetAsync($"/payments/{createdPayment.Id}");

            // Assert
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.Fail($"GET /payments/{createdPayment.Id} falhou com status {response.StatusCode}: {errorContent}");
            }
            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(responseContent), "Response content should not be empty");
            var retrievedPayment = JsonSerializer.Deserialize<Payment>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(retrievedPayment);
            Assert.Equal(createdPayment.Id, retrievedPayment.Id);
            Assert.Equal(createdPayment.UserId, retrievedPayment.UserId);
            Assert.Equal(createdPayment.Amount, retrievedPayment.Amount);
            Assert.Equal(createdPayment.Status, retrievedPayment.Status);
        }

        [Fact]
        public async Task Put_Payment_WithValidToken_ReturnsNoContent()
        {
            // Arrange
            var token = GenerateTestToken();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payment = new Payment { UserId = "12345", Amount = 99.99m, Status = "Pending" };
            var postContent = new StringContent(JsonSerializer.Serialize(payment), Encoding.UTF8, "application/json");
            var postResponse = await _client.PostAsync("/payments", postContent);
            Assert.Equal(System.Net.HttpStatusCode.Created, postResponse.StatusCode);
            var postResponseContent = await postResponse.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(postResponseContent), "Post response content should not be empty");
            var createdPayment = JsonSerializer.Deserialize<Payment>(postResponseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(createdPayment);
            var updatedPayment = new Payment { UserId = "67890", Amount = 199.99m, Status = "Completed" };

            // Act
            var putContent = new StringContent(JsonSerializer.Serialize(updatedPayment), Encoding.UTF8, "application/json");
            var response = await _client.PutAsync($"/payments/{createdPayment.Id}", putContent);

            // Assert
            if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.Fail($"PUT /payments/{createdPayment.Id} falhou com status {response.StatusCode}: {errorContent}");
            }

            // Verify update
            var getResponse = await _client.GetAsync($"/payments/{createdPayment.Id}");
            if (getResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var errorContent = await getResponse.Content.ReadAsStringAsync();
                Assert.Fail($"GET /payments/{createdPayment.Id} falhou com status {getResponse.StatusCode}: {errorContent}");
            }
            var getContent = await getResponse.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(getContent), "Get response content should not be empty");
            var retrievedPayment = JsonSerializer.Deserialize<Payment>(getContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(retrievedPayment);
            Assert.Equal(updatedPayment.UserId, retrievedPayment.UserId);
            Assert.Equal(updatedPayment.Amount, retrievedPayment.Amount);
            Assert.Equal(updatedPayment.Status, retrievedPayment.Status);
        }

        [Fact]
        public async Task Delete_Payment_WithValidToken_ReturnsOk()
        {
            // Arrange
            var token = GenerateTestToken();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payment = new Payment { UserId = "12345", Amount = 99.99m, Status = "Pending" };
            var postContent = new StringContent(JsonSerializer.Serialize(payment), Encoding.UTF8, "application/json");
            var postResponse = await _client.PostAsync("/payments", postContent);
            Assert.Equal(System.Net.HttpStatusCode.Created, postResponse.StatusCode);
            var postResponseContent = await postResponse.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(postResponseContent), "Post response content should not be empty");
            var createdPayment = JsonSerializer.Deserialize<Payment>(postResponseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(createdPayment);

            // Act
            var response = await _client.DeleteAsync($"/payments/{createdPayment.Id}");

            // Assert
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.Fail($"DELETE /payments/{createdPayment.Id} falhou com status {response.StatusCode}: {errorContent}");
            }

            // Verify deletion
            var getResponse = await _client.GetAsync($"/payments/{createdPayment.Id}");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, getResponse.StatusCode);
        }
    }
}