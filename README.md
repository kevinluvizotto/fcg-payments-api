# FCG Payments API

API de Pagamentos da plataforma FIAP Cloud Games (FCG).
Parte do projeto da Fase 3 (migração para microsserviços).

## 🚀 Tecnologias

* .NET 8 (Minimal APIs)
* Entity Framework Core (InMemory para dev)
* JWT Authentication (JSON Web Token)
* BCrypt (se necessário para hashing)
* Swagger (documentação e testes de endpoints)
* Docker

## 📦 Como rodar localmente

### Pré-requisitos

- .NET 8 SDK.
- (Opcional) Docker.

# Restaurar pacotes e compilar

dotnet build fcg-payments-api.sln

# Rodar a API

dotnet run --project src/FCG.Payments.Api

A aplicação sobe em: 👉 http://localhost:5192

Swagger UI disponível em: 👉 http://localhost:5192/swagger

🔑 Autenticação JWT
- Use tokens gerados no fcg-users-api.
- Authorize no Swagger: Cole o token (sem Bearer).

## Endpoints principais

Health GET /health → Status da API

Payments POST /payments → Criar pagamento (autenticado)

GET /payments → Listar pagamentos (autenticado)

GET /payments/{id} → Buscar pagamento (autenticado)

PUT /payments/{id} → Atualizar pagamento (autenticado)

DELETE /payments/{id} → Remover pagamento (autenticado)

## Rodando com Docker

# Build da imagem

docker build -t fcg-payments-api .

# Rodar container

docker run -d -p 5192:8080 fcg-payments-api

# API Local

A API ficará disponível em: http://localhost:5192