# TicketFlow API

Backend de portfólio simulando uma plataforma de venda de ingressos de alto
fluxo — o foco é resolver, de verdade, o problema de concorrência de
"milhares de pessoas tentando comprar o mesmo assento ao mesmo tempo", com
Clean Architecture, PostgreSQL/EF Core, e (nas próximas fases) Redis,
RabbitMQ e SignalR.

O raciocínio por trás de cada decisão está documentado em
[`docs/DECISOES-DE-ARQUITETURA.md`](docs/DECISOES-DE-ARQUITETURA.md) — vale
a leitura antes de mexer no código.

## Rodando localmente

Pré-requisitos: .NET 8 SDK, Docker Desktop.

```bash
docker compose up -d postgres
dotnet run --project src/TicketFlow.Api
```

A API sobe em `http://localhost:5299` (ou na porta do seu
`launchSettings.json`) e aplica as migrations do EF Core automaticamente em
ambiente de desenvolvimento. Documentação interativa (Scalar) em
`/scalar/v1`.

## Testes

```bash
dotnet test
```

## Estrutura

```
src/
  TicketFlow.Domain          — entidades e regras de negócio, zero dependências externas
  TicketFlow.Application     — casos de uso, interfaces, DTOs, validação
  TicketFlow.Infrastructure  — EF Core, JWT, hashing de senha
  TicketFlow.Api             — controllers, autenticação, composição
tests/
  TicketFlow.Domain.Tests    — testes xUnit das regras de domínio
docs/
  DECISOES-DE-ARQUITETURA.md — o porquê de cada decisão, fase a fase
```
