using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using FCG.Payments.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
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
            _factory = factory;
            _client = factory.CreateClient();
        }

        private string GenerateJwtToken(string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "TestUser"),
                new Claim(ClaimTypes.Role, role)
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("super_secret_dev_key_1234567890_LONGER_KEY"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: "fcg-users-api",
                audience: "fcg-users-api",
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [Fact]
        public async Task Get_Health_ReturnsOk()
        {
            var response = await _client.GetAsync("/health");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            // Remover aspas extras, se houver
            var trimmedContent = content.Trim('"');
            Assert.Equal("OK", trimmedContent);
        }

        [Fact]
        public async Task Post_Payment_WithValidToken_ReturnsCreated()
        {
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateJwtToken("User"));
            var payment = new Payment
            {
                UserId = "7390acc8-2c65-409a-b10f-103f93cb884b",
                Amount = 99.99m,
                Status = "Pending"
            };
            var response = await _client.PostAsJsonAsync("/payments", payment);
            response.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task Post_Payment_WithoutToken_ReturnsUnauthorized()
        {
            _client.DefaultRequestHeaders.Authorization = null;
            var payment = new Payment
            {
                UserId = "7390acc8-2c65-409a-b10f-103f93cb884b",
                Amount = 99.99m,
                Status = "Pending"
            };
            var response = await _client.PostAsJsonAsync("/payments", payment);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Get_Payments_WithValidToken_ReturnsOk()
        {
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateJwtToken("Admin"));
            var response = await _client.GetAsync("/payments");
            response.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Get_Payments_WithoutToken_ReturnsUnauthorized()
        {
            _client.DefaultRequestHeaders.Authorization = null;
            var response = await _client.GetAsync("/payments");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Put_Payment_WithValidToken_ReturnsNoContent()
        {
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateJwtToken("Admin"));
            var payment = new Payment
            {
                UserId = "7390acc8-2c65-409a-b10f-103f93cb884b",
                Amount = 99.99m,
                Status = "Pending"
            };
            var postResponse = await _client.PostAsJsonAsync("/payments", payment);
            postResponse.EnsureSuccessStatusCode();
            var createdPayment = await postResponse.Content.ReadFromJsonAsync<Payment>();
            var updatedPayment = new Payment
            {
                UserId = createdPayment!.UserId,
                Amount = 149.99m,
                Status = "Completed"
            };
            var putResponse = await _client.PutAsJsonAsync($"/payments/{createdPayment!.Id}", updatedPayment);
            Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);
        }

        [Fact]
        public async Task Delete_Payment_WithValidToken_ReturnsNoContent()
        {
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateJwtToken("Admin"));
            var payment = new Payment
            {
                UserId = "7390acc8-2c65-409a-b10f-103f93cb884b",
                Amount = 99.99m,
                Status = "Pending"
            };
            var postResponse = await _client.PostAsJsonAsync("/payments", payment);
            postResponse.EnsureSuccessStatusCode();
            var createdPayment = await postResponse.Content.ReadFromJsonAsync<Payment>();
            var deleteResponse = await _client.DeleteAsync($"/payments/{createdPayment!.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        [Fact]
        public async Task Get_PaymentById_WithValidToken_ReturnsOk()
        {
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateJwtToken("User"));
            var payment = new Payment
            {
                UserId = "7390acc8-2c65-409a-b10f-103f93cb884b",
                Amount = 99.99m,
                Status = "Pending"
            };
            var postResponse = await _client.PostAsJsonAsync("/payments", payment);
            postResponse.EnsureSuccessStatusCode();
            var createdPayment = await postResponse.Content.ReadFromJsonAsync<Payment>();
            var response = await _client.GetAsync($"/payments/{createdPayment!.Id}");
            response.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}