# TicketFlow API

Backend de portfólio simulando uma plataforma de venda de ingressos de alto
fluxo — o foco é resolver, de verdade, o problema de concorrência de
"milhares de pessoas tentando comprar o mesmo assento ao mesmo tempo", com
Clean Architecture, PostgreSQL/EF Core, Redis (lock distribuído, cache e
backplane do SignalR), RabbitMQ (confirmação assíncrona via worker), e
SignalR (mapa de assentos e confirmação de pedido em tempo real), e rate
limiting nos endpoints mais sensíveis a abuso.

O raciocínio por trás de cada decisão está documentado em
[`docs/DECISOES-DE-ARQUITETURA.md`](docs/DECISOES-DE-ARQUITETURA.md) — vale
a leitura antes de mexer no código.

## Rodando localmente

Pré-requisitos: .NET 8 SDK, Docker Desktop.

```bash
docker compose up -d postgres redis rabbitmq
dotnet run --project src/TicketFlow.Api
```

Em outro terminal, o worker que processa as confirmações de pedido:

```bash
dotnet run --project src/TicketFlow.Worker
```

A API sobe em `http://localhost:5299` (ou na porta do seu
`launchSettings.json`) e aplica as migrations do EF Core automaticamente em
ambiente de desenvolvimento. Documentação interativa (Scalar) em
`/scalar/v1`. Painel de administração do RabbitMQ em
`http://localhost:15672` (usuário/senha: `guest`/`guest`). Hub do SignalR em
`/hubs/ticketflow` (token JWT via query string `?access_token=...`, já que
o handshake do WebSocket não permite header customizado).

Limites de requisição: 100 req/min por IP (global), 5 req/min por IP em
`/api/auth/register` e `/api/auth/login`, e 5 reservas por 10s por usuário
em `/api/orders/reserve` (token bucket). Estourar o limite retorna `429`
com header `Retry-After`.

## Testes

```bash
dotnet test
```

## Estrutura

```
src/
  TicketFlow.Domain          — entidades e regras de negócio, zero dependências externas
  TicketFlow.Application     — casos de uso, interfaces, DTOs, validação
  TicketFlow.Infrastructure  — EF Core, Redis, RabbitMQ, SignalR Hub, JWT, hashing de senha
  TicketFlow.Api              — controllers, autenticação, composição
  TicketFlow.Worker          — consome a fila de confirmação de pedidos
tests/
  TicketFlow.Domain.Tests    — testes xUnit das regras de domínio
docs/
  DECISOES-DE-ARQUITETURA.md — o porquê de cada decisão, fase a fase
```
