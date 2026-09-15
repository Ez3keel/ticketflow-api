# Decisões de arquitetura — TicketFlow

Este documento existe para você aprender com o raciocínio por trás de cada
decisão, não só ver o código pronto. Cada seção corresponde a um bloco de
commits e segue o mesmo formato: **o que foi feito**, **por que**, e **o que
teria acontecido se tivéssemos feito diferente**.

Vou atualizar este arquivo a cada nova fase do roadmap.

---

## Fase 0/1 — Estrutura da solution

**O que foi feito**: 4 projetos (`Domain`, `Application`, `Infrastructure`,
`Api`) mais um projeto de testes (`Domain.Tests`), com referências que só
apontam "para dentro": `Api` e `Infrastructure` dependem de `Application`,
que depende de `Domain`. `Domain` não depende de nada.

**Por quê**: essa é a "Regra da Dependência" da Clean Architecture — as
camadas mais internas (regras de negócio) nunca devem saber que camadas
externas (banco, fila, web) existem. Isso é o que permite trocar o
PostgreSQL por outro banco, ou adicionar RabbitMQ, sem tocar em uma linha
sequer do `Domain` ou do `Application`.

**O que aconteceria diferente**: se `Domain` referenciasse o Entity
Framework (como muita gente faz colocando atributos `[Key]`, `[Column]`
direto nas entidades), qualquer troca de ORM ou de banco exigiria reescrever
as entidades. Isolar isso agora é o que vai deixar a Fase 2 (Postgres/EF
Core) ser "só" uma implementação nova de interfaces já existentes, sem
mexer no que já funciona.

---

## Fase 0 — Modelagem do domínio (`Seat`, `Order`, `Event`)

**O que foi feito**: `Seat` é uma máquina de estados
(`Available → Reserved → Sold`) com métodos que validam a própria transição
(`Reserve`, `Confirm`, `Release`) em vez de um setter público de `Status`.
O mesmo vale para `Order` (`PendingPayment → Confirmed/Cancelled/Expired`).

**Por quê**: isso é o padrão **Rich Domain Model** (em oposição ao *Anemic
Domain Model*, onde entidades são só sacos de propriedades e toda a lógica
fica espalhada em serviços). Colocar a regra dentro da entidade garante que
é **impossível** deixar um `Seat` num estado inválido em qualquer lugar do
código — o compilador força você a passar pelo método, que valida antes de
mudar o estado.

**O que aconteceria diferente**: se `Status` fosse uma propriedade pública
com `set`, bastaria alguém escrever `seat.Status = SeatStatus.Sold` em
qualquer lugar (num controller, num teste, num script de seed) pra pular
toda validação — e aí seu sistema de ingressos venderia o mesmo assento duas
vezes sem nenhum erro.

**Trade-off consciente**: o comentário em
[`ReservationService.cs`](../src/TicketFlow.Application/Reservations/ReservationService.cs)
é importante — a checagem "todo mundo disponível?" seguida de "reserva
todo mundo" é seguro dentro de um processo (graças ao `lock` implícito do
`ConcurrentDictionary`), mas **não é seguro entre múltiplas instâncias da
API rodando ao mesmo tempo**. Isso é proposital: é exatamente o problema que
o Redis vai resolver na Fase 3. Construir a versão "ingênua" primeiro e
sentir o problema na pele é mais didático do que já começar com Redis sem
entender por que ele é necessário.

---

## Fase 1 — `User` / `RefreshToken` com rotação

**O que foi feito**: cada refresh token tem um `ExpiresAtUtc` e um
`RevokedAtUtc`. Ao usar um refresh token pra pegar um novo access token
(`RotateRefreshToken`), o token antigo é **revogado** e um novo é emitido —
o token antigo nunca mais funciona, mesmo que ainda não tivesse expirado.

**Por quê**: isso é chamado de *refresh token rotation* e é a defesa padrão
contra roubo de token. Se alguém roubar um refresh token e usá-lo, o dono
legítimo vai tentar usar o token dele (que já foi revogado pelo atacante) e
vai falhar — isso é um sinal de comprometimento que sistemas de produção
usam pra revogar *todos* os tokens daquele usuário automaticamente. Aqui não
implementei essa detecção completa (seria over-engineering para o escopo
atual), mas a estrutura de dados já suporta.

**Senha com PBKDF2 em vez de uma lib de terceiros**: usei
`Rfc2898DeriveBytes.Pbkdf2`, que já vem embutido no .NET, em vez de
BCrypt.Net ou similar. Duas razões: (1) evita mais uma dependência externa
pra algo que o framework já resolve bem, e (2) `CryptographicOperations.
FixedTimeEquals` na comparação evita *timing attacks* — comparar hashes com
`==` normal vazaria informação sobre quantos caracteres bateram, porque
`==` para de comparar no primeiro byte diferente.

---

## Fase 1 — Camada `Application` (casos de uso)

**O que foi feito**: interfaces (`IEventRepository`, `IUserRepository`,
`IPasswordHasher`, `IJwtTokenGenerator`, `IDateTimeProvider`) definidas
*aqui*, implementadas depois na `Infrastructure`. Os serviços
(`AuthService`, `EventCatalogService`, `ReservationService`) dependem só
dessas interfaces, nunca de uma implementação concreta.

**Por quê**: isso é **Inversão de Dependência** (o "D" do SOLID). A camada
de regras de negócio dita o contrato ("preciso de algo que salve um
usuário"); a infraestrutura obedece esse contrato. Isso também é o que
torna os serviços testáveis sem precisar de banco de dados de verdade — dá
pra testar `AuthService` com um `IUserRepository` fake em memória.

**`IDateTimeProvider` em vez de `DateTime.UtcNow` direto**: parece um
detalhe bobo, mas sem isso seria impossível escrever um teste
determinístico tipo "criei um pedido, avancei o relógio 11 minutos, o
assento deveria estar liberado" — o teste ficaria refém do tempo real de
execução. Isso vai ficar mais importante ainda quando a expiração de
reserva virar automática (fila/worker).

**Por que não usei MediatR/CQRS**: é um padrão popular em projetos Clean
Architecture no .NET, mas adiciona uma camada de indireção (commands,
handlers, pipeline behaviors) que você ainda não pediu pra aprender. Serviços
de aplicação simples com métodos diretos cobrem o mesmo objetivo de
separação de camadas com menos conceitos novos de uma vez. Se depois você
quiser adicionar CQRS como aprendizado extra, dá pra migrar gradualmente.

---

## Fase 1 — `Infrastructure` (repositórios em memória, JWT)

**O que foi feito**: repositórios `InMemoryEventRepository`,
`InMemoryOrderRepository`, `InMemoryUserRepository` guardando dados em
`ConcurrentDictionary`, registrados como **Singleton** no DI (não Scoped).

**Por quê Singleton**: no ASP.NET Core, uma classe registrada como `Scoped`
ganha uma instância *nova* a cada requisição HTTP. Se os repositórios em
memória fossem Scoped, cada requisição começaria com um dicionário vazio —
você criaria um evento numa requisição e ele "sumiria" na próxima. Singleton
garante que a mesma instância (e os mesmos dados) sobrevive durante toda a
vida do processo.

**O que muda na Fase 2**: quando trocarmos para EF Core + PostgreSQL, os
repositórios passam a depender de um `DbContext`, e `DbContext` **deve** ser
Scoped (ele não é thread-safe e representa uma transação/unidade de
trabalho por requisição). Ou seja, a mudança de Singleton → Scoped não é
acidental, é uma consequência direta de trocar "dicionário em memória" por
"conexão de banco real".

**JWT com `MapInboundClaims = false`**: por padrão, o
`JwtSecurityTokenHandler` do .NET remapeia silenciosamente o claim `sub`
para `ClaimTypes.NameIdentifier` (um nome de claim antigo do WS-Federation).
Isso causa um bug clássico e confuso: você emite um token com `sub`, mas ao
tentar ler `User.FindFirstValue(JwtRegisteredClaimNames.Sub)` no controller,
não encontra nada. Desabilitei esse remapeamento pra manter os nomes dos
claims exatamente como foram emitidos.

---

## Fase 1 — `Api` (controllers, middleware de erro)

**O que foi feito**: um `ExceptionHandlingMiddleware` central que
transforma `NotFoundException` → 404, `DomainException` → 409, e qualquer
outra exceção não tratada → 500 com log estruturado. Os controllers não têm
nenhum `try/catch`.

**Por quê**: sem isso, cada método de cada controller precisaria repetir o
mesmo bloco try/catch pra transformar exceções de negócio em respostas
HTTP corretas. Centralizar isso em um middleware significa que a regra "erro
de domínio = 409" é escrita uma vez só, e todo controller novo já ganha esse
comportamento de graça.

**Por que 409 (Conflict) para `DomainException` e não 400 (Bad Request)**:
400 é reservado, por convenção, para "a requisição em si está malformada"
(é isso que o FluentValidation cobre — email inválido, campo vazio). 409 diz
"a requisição está bem formada, mas conflita com o estado atual do
recurso" — que é exatamente o caso de "esse assento não está mais
disponível".

**Validação manual com `IValidator<T>` em vez de um filtro global**: dava
pra automatizar isso com um `IActionFilter` que valida todo DTO
automaticamente. Optei por deixar explícito em cada endpoint
(`await _validator.ValidateAsync(...)`) porque, no seu nível atual, ver o
"onde" e "quando" a validação acontece é mais valioso pra aprendizado do que
a conveniência de automatizar — é um replace fácil de fazer depois, quando
o padrão já estiver internalizado.

---

## Documentação com Scalar

**O que foi feito**: troquei a Swagger UI clássica pela interface do
Scalar (`/scalar/v1`), mantendo o Swashbuckle apenas para *gerar* o
documento OpenAPI (`/openapi/v1.json`) — o Scalar só consome esse JSON e
desenha a interface.

**Por quê**: Scalar é mais moderno visualmente, tem um recurso de
"try it out" mais fluido (inclusive gera snippets de código em várias
linguagens) e é o que a maior parte de projetos .NET novos vem adotando no
lugar do Swagger UI. Tecnicamente as duas fazem a mesma coisa — renderizar
um documento OpenAPI — a diferença é só a camada de apresentação.

---

## Fase 2 — PostgreSQL + EF Core

**O que foi feito**: os repositórios em memória foram substituídos por
implementações EF Core (`EfEventRepository`, `EfOrderRepository`,
`EfUserRepository`) sobre um `TicketFlowDbContext` + PostgreSQL rodando em
Docker. As entidades de domínio **não mudaram nada** — nenhum atributo de
EF, nenhuma anotação, nenhum setter público novo. Todo o mapeamento vive em
classes `IEntityTypeConfiguration<T>` separadas, dentro da `Infrastructure`.

**Por que isso confirma a Fase 0/1**: essa era exatamente a promessa da
Clean Architecture — trocar "onde os dados vivem" sem tocar em "como as
regras de negócio funcionam". O `Seat.Reserve()`, `Order.Confirm()`, etc.
continuam exatamente iguais; só o que estava por trás de `IEventRepository`
mudou.

**Como o EF Core lida com coleções encapsuladas**: `Event.Sessions` é
`IReadOnlyCollection<EventSession>`, sem setter, apoiada num campo privado
`_sessions`. Configurei cada navegação com
`.UsePropertyAccessMode(PropertyAccessMode.Field)`, dizendo ao EF Core pra
ler/escrever direto no campo em vez de exigir um setter público — é o
padrão oficial da documentação do EF Core pra trabalhar com Rich Domain
Models sem abrir mão do encapsulamento.

**Bug real que apareci e o porquê (vale a pena entender de verdade)**: ao
testar `POST /events/{id}/sessions` contra o Postgres de verdade, recebi um
`DbUpdateConcurrencyException` dizendo que um `UPDATE` afetou 0 linhas —
mas a sessão era **nova**, deveria ter sido um `INSERT`.

Causa: nossas entidades geram o próprio `Id` no construtor
(`Guid.NewGuid()`, na classe base `Entity`). Quando o EF Core "descobre"
uma entidade nova só através de uma navegação (`Event.Sessions` ganhou um
item novo) — em vez de via `DbSet.Add()` explícito — ele usa uma heurística
pra decidir se é um `INSERT` ou um `UPDATE`: *"essa chave primária já tem um
valor diferente do padrão (`Guid.Empty`)? Então deve já existir no banco."*
Como nossos GUIDs nunca são `Guid.Empty` (já nascem preenchidos), o EF
Core sempre concluía errado que a entidade já existia.

A correção: `builder.Property(e => e.Id).ValueGeneratedNever();` em cada
configuração. Isso diz ao EF Core "eu nunca deleguei a geração desse Id pra
você, então não tente adivinhar Added vs. Modified pelo valor — confie no
que o change tracker já sabe". Depois disso, entidades descobertas via
navegação passaram a ser corretamente tratadas como novas.

**Por que esse bug é importante de entender**: é uma armadilha clássica e
muito comum em qualquer projeto EF Core que usa GUIDs gerados no domínio
(em vez de deixar o banco gerar via `gen_random_uuid()` ou similar) — e é
exatamente o tipo de coisa que só aparece testando contra um banco real,
nunca contra um repositório em memória. Isso também é uma ótima resposta
pra dar numa entrevista técnica se perguntarem "já teve algum bug estranho
com EF Core?".

**Índice único como defesa em profundidade**: `Seat` ganhou um índice único
em `(EventSessionId, Row, Number)`. A regra "não pode haver assento
duplicado" já existe no domínio (`EventSession.AddSeat`), mas o índice
garante que ela vale **mesmo que** algum código futuro escreva direto no
banco (uma migração de dados, um script administrativo) e ignore o
aggregate root. Defesa em camadas: a aplicação previne, o banco garante.

**Migrations aplicadas automaticamente só em desenvolvimento**: o
`Program.cs` chama `dbContext.Database.MigrateAsync()` na inicialização,
mas só quando `Environment.IsDevelopment()`. Em produção, aplicar
migrations automaticamente a cada deploy é arriscado (puxa o schema junto
com o binário, sem controle explícito de quando/como) — o padrão correto
seria um passo de release separado (`dotnet ef database update` num
pipeline de CI/CD, antes de trocar a versão da API no ar).

**Validado contra o banco real**: registro → login → refresh (com detecção
de reuso de token revogado, `409`) → criar evento/sessão/assentos → reservar
2 assentos → tentar reservar de novo o mesmo assento (`409`) → confirmar
pedido → conferido também via `psql` direto que as linhas gravadas batem
exatamente com o que a API retornou.

---

## Fase 3 — Redis (lock distribuído + cache)

**O que foi feito**: duas coisas separadas, com propósitos bem diferentes:

1. **Lock distribuído** (`IDistributedLockProvider` / `RedisDistributedLockProvider`)
   pra proteger a janela "checar disponibilidade → reservar" do
   `ReservationService` — o ponto exato que o comentário desde a Fase 1
   já avisava que só era seguro dentro de um processo só.
2. **Cache-aside** (`ICacheService` / `RedisCacheService`) no
   `GET /events/sessions/{id}` — o mapa de assentos é lido com muito mais
   frequência do que é escrito (todo mundo olhando o show, poucos
   comprando), então é um candidato natural a cache.

**Por que o problema anterior era real e não só teórico**: com EF Core, o
`ReservationService` carregava o assento, checava `Status == Available`, e
chamava `seat.Reserve()` — tudo dentro do mesmo `DbContext`. Sem nenhuma
trava, duas requisições concorrentes (em duas instâncias da API, ou até na
mesma, dependendo do timing) podiam **as duas** carregar o assento como
`Available`, **as duas** passarem na validação, e **as duas** chamarem
`SaveChanges()` — a segunda simplesmente sobrescrevia a primeira, porque a
coluna `Status` não tem controle de concorrência otimista. Ou seja: sem
Redis, o sistema venderia o mesmo assento duas vezes **silenciosamente**,
sem nenhum erro. Provei isso na prática disparando 15 requisições paralelas
tentando reservar o mesmo assento: com o lock, exatamente 1 teve sucesso e
14 caíram em `409` — sem o lock (é só remover a seção de
`_lockProvider.TryAcquireAsync` mentalmente) esse mesmo teste teria dado
mais de um "sucesso".

**Por que a trava é por assento, não por sessão inteira**: travar a sessão
inteira (`session-lock:{sessionId}`) seria mais simples de implementar, mas
serializaria **todas** as compras daquele show — ninguém mais conseguiria
comprar enquanto uma pessoa está no meio de uma compra, mesmo que sejam
assentos completamente diferentes. Travando por assento individual, duas
pessoas comprando assentos diferentes do mesmo show continuam em paralelo;
só quem disputa o **mesmo** assento é serializado.

**Por que os locks são adquiridos em ordem (`OrderBy(s => s.Id)`)**:
imagine duas compras concorrentes, uma pedindo assentos `[A, B]` e outra
pedindo `[B, A]`. Sem ordenar, a primeira poderia travar `A` e esperar por
`B`, enquanto a segunda trava `B` e espera por `A` — *deadlock*. Ordenando
os IDs antes de adquirir os locks, as duas requisições sempre tentam travar
na mesma sequência, então uma delas sempre consegue progredir.

**Por que o TTL do lock é curto (5s) enquanto a reserva em si dura 10
minutos**: são coisas diferentes. O lock do Redis só precisa sobreviver ao
tempo de ida-e-volta ao banco (milissegundos, poucos segundos no pior
caso) — ele existe só pra proteger a operação de "ler e escrever" contra
concorrência. O "esse assento está reservado por 10 minutos" é um fato de
negócio, e por isso vive **duravelmente no Postgres**
(`Seat.ReservedUntil`), não no Redis. Se o Redis reiniciasse agora, nenhuma
reserva em andamento seria perdida — só o lock efêmero da operação que
talvez estivesse no meio.

**Por que `ReloadSeatsAsync` é necessário**: os assentos são carregados do
banco **antes** de conseguirmos o lock (precisamos deles pra saber os IDs a
travar). Isso significa que, entre o carregamento e a garantia do lock,
outra requisição pode ter reservado esse mesmo assento e já ter dado
commit. O EF Core, por padrão, nunca re-consulta o banco pra uma entidade
que já está sendo rastreada na mesma sessão do `DbContext` (o "mapa de
identidade") — então, sem forçar um `ReloadAsync` depois de garantir o
lock, a checagem de disponibilidade estaria olhando pra uma cópia
potencialmente desatualizada em memória, mesmo com o lock em mãos.

**Por que o cache usa invalidação explícita e não só TTL**: toda escrita
que muda o estado dos assentos de uma sessão (`AddSeatsAsync`,
`ReserveSeatsAsync`, `ConfirmOrderAsync`, `CancelOrderAsync`) remove a
chave do cache explicitamente. O TTL de 30s existe só como rede de
segurança — se algum caminho de escrita futuro esquecer de invalidar, o
dado errado no máximo fica visível por até 30 segundos, não indefinidamente.
Confirmei isso na prática: depois de confirmar um pedido, a chave
`session:{id}` já não existia mais no Redis (`EXISTS` retornou `0`), antes
mesmo do TTL vencer.

**Validado na prática**: 15 requisições paralelas pro mesmo assento → 1
sucesso, 14 `409`; cache populado com TTL de 30s no primeiro `GET`;
invalidação confirmada via `redis-cli` logo após um `confirm`.

---

## Fase 4 — RabbitMQ (confirmação assíncrona via worker)

**O que foi feito**: `POST /orders/{id}/confirm` não confirma mais nada
diretamente. Ele só marca o pedido como `Processing` e publica uma
mensagem `{ OrderId }` na fila `order-confirmations`, respondendo
`202 Accepted` em ~180ms. Um **processo separado**
(`TicketFlow.Worker`, um `BackgroundService` do .NET, não um endpoint da
API) consome essa fila, simula 2 segundos de processamento de pagamento, e
só então executa a confirmação de verdade (assentos → `Sold`, pedido →
`Confirmed`). O cliente descobre que a compra foi confirmada fazendo
`GET /orders/{id}` — implementado nesta fase também, já que antes não
existia um jeito de consultar um pedido isoladamente.

**Por que um novo estado `Processing` no domínio**: sem ele, "confirmar"
teria que pular direto de `PendingPayment` para `Confirmed`, e não haveria
como representar "a confirmação foi pedida, mas ainda não aconteceu" — nem
como impedir alguém de clicar "confirmar" duas vezes e publicar duas
mensagens pra fila do mesmo pedido (a segunda tentativa de
`MarkAsProcessing()` falha porque o pedido já não está mais
`PendingPayment`). O estado extra também deixa o fluxo auditável: dá pra
ver, pelo histórico, que um pedido ficou "em processamento" por X segundos
antes de confirmar.

**Por que isso é um projeto .NET separado (`TicketFlow.Worker`), não um
`BackgroundService` dentro da própria API**: a API e o worker têm perfis de
carga completamente diferentes — a API precisa responder rápido a picos de
tráfego HTTP, o worker precisa processar mensagens de forma constante e
previsível. Sendo processos separados, cada um escala independentemente
(mais instâncias da API não significa mais processamento de fila, e
vice-versa) e um consegue cair sem derrubar o outro. Ambos reutilizam a
mesma `TicketFlow.Infrastructure` e `TicketFlow.Application` — é o mesmo
`AddInfrastructure()` de sempre, só que chamado a partir de um
`Host.CreateApplicationBuilder` (worker genérico) em vez de um
`WebApplication.CreateBuilder` (API web).

**Por que existem dois "serviços de confirmação" separados**
(`ReservationService.RequestConfirmationAsync` vs.
`OrderProcessingService.ConfirmOrderAsync`) em vez de um método só: eles
têm autorização e gatilhos completamente diferentes. O primeiro é chamado
por um usuário autenticado, via HTTP, e só pode agir sobre o **próprio**
pedido. O segundo é chamado só pelo worker, a partir de uma mensagem que já
passou pela fila — não faz sentido (nem seria seguro) expor esse método
como endpoint HTTP, e não faz sentido o worker precisar simular um
`userId` pra chamar um método pensado pra validar propriedade de recurso
via HTTP.

**Fila simples (default exchange) em vez de exchange dedicada**: usei o
padrão mais básico do RabbitMQ — publicar direto pra uma fila nomeada via
exchange padrão (`""`) — porque só existe **um** tipo de consumidor
interessado nessa mensagem (o worker de confirmação). Uma exchange
`topic`/`fanout` só ganharia sentido se, no futuro, mais de um serviço
precisasse reagir ao mesmo evento (por exemplo, um serviço de e-mail
enviando a confirmação por fora do worker principal) — nesse caso o
roteamento por exchange faria diferença. Introduzir isso agora seria
complexidade sem propósito imediato.

**`BasicQos(prefetchCount: 1)`**: sem isso, o RabbitMQ empurraria todas as
mensagens pendentes pro worker de uma vez, mesmo que ele só processe uma
por vez — isso quebraria o equilíbrio de carga se você rodasse várias
instâncias do worker (uma instância acumularia um monte de mensagens
enquanto outra ficaria ociosa). Com `prefetchCount: 1`, cada worker só
recebe uma mensagem nova depois de confirmar (`ack`) a anterior.

**O que acontece se o processamento falhar**: a mensagem é rejeitada com
`requeue: false` (descartada). Documentei no código que isso é uma
simplificação deliberada — o correto em produção seria configurar uma
*dead-letter exchange*, pra mensagens com falha caírem numa fila separada
onde um humano (ou um processo de retry) possa investigar, em vez de
simplesmente desaparecerem.

**Validado na prática**: cheguei a rodar dois processos do worker ao mesmo
tempo sem perceber (sobra de um teste anterior) — o RabbitMQ, corretamente,
distribuiu mensagens entre os dois, e como eu só olhava o log de um deles,
pareceu que uma confirmação tinha "sumido". Conferir
`rabbitmqctl list_queues name messages consumers` mostrou 2 consumidores
na fila quando eu esperava 1 — foi assim que achei o processo duplicado.
Isso não chegou a ser um bug de código, mas foi um lembrete de que "quantas
instâncias do consumidor estão rodando" é um detalhe operacional real que
vale a pena verificar, não só assumir. Depois de matar o processo
duplicado, confirmei o comportamento esperado: `202` imediato com status
`Processing`, status continua `Processing` logo em seguida, e só vira
`Confirmed` (assento incluso, verificado direto no Postgres) depois do
delay simulado de pagamento.

---

## Fase 5 — SignalR (tempo real, via backplane no Redis)

**O que foi feito**: dois tipos de notificação em tempo real, via
WebSocket (`/hubs/ticketflow`):

1. **`SeatStatusChanged`** — enviado pro grupo `session:{sessionId}` toda
   vez que um assento muda de estado (`Reserved` na reserva, `Sold` na
   confirmação, `Available` no cancelamento). Todo cliente olhando o mapa
   de assentos daquela sessão recebe, sem precisar dar F5.
2. **`OrderConfirmed`** — enviado só pro usuário dono do pedido, quando o
   worker termina de confirmar a compra. Substitui o polling em
   `GET /orders/{id}` que a Fase 4 deixou como única opção.

**O problema que motivou reaproveitar o Redis pela terceira vez**: as
conexões WebSocket de verdade só existem no processo da **API** (é lá que
`MapHub<TicketFlowHub>()` roda). Mas quem confirma o pedido — e portanto
quem sabe que precisa mandar `SeatStatusChanged: Sold` e `OrderConfirmed`
— é o **worker**, que não tem, e não deveria ter, nenhuma conexão WebSocket
própria. Sem alguma ponte entre os dois processos, o worker simplesmente
não teria como "falar" com os clientes conectados na API.

A solução padrão do SignalR pra isso é um **backplane**: todo servidor com
Hub (aqui, só a API) se inscreve num canal do Redis via pub/sub; qualquer
processo que tenha um `IHubContext<TicketFlowHub>` configurado com o mesmo
backplane pode publicar nesse canal, mesmo sem hospedar conexão nenhuma —
é exatamente esse "modo publicador-apenas" que o worker usa. Isso é
literalmente para o que o backplane do SignalR foi desenhado (o caso de
uso mais comum é múltiplas *instâncias da própria API* atrás de um load
balancer se sincronizando), mas funciona igualmente bem entre dois
processos com responsabilidades completamente diferentes — API e worker,
nesse caso. É a **terceira** finalidade distinta que o Redis ganha neste
projeto (lock distribuído na Fase 3, cache na Fase 3, backplane de pub/sub
aqui), e cada uma delas resolve um problema diferente — vale a pena notar
isso numa entrevista: "Redis" não é uma ferramenta de um truque só.

**Por que `IRealtimeNotifier` é uma interface na `Application`** (e não os
serviços chamando `IHubContext` diretamente): pelo mesmo motivo de sempre
— `Application` não pode depender de um detalhe de infraestrutura como
SignalR. Se um dia trocarmos WebSocket por outra tecnologia de push
(Server-Sent Events, por exemplo), só a implementação em `Infrastructure`
muda; `ReservationService` e `OrderProcessingService` continuam chamando
`_realtimeNotifier.NotifySeatStatusChangedAsync(...)` sem saber nem se
importar com o que está por trás.

**`Clients.User()` exige um `IUserIdProvider` customizado**: o SignalR, por
padrão, identifica o "usuário" de uma conexão pelo claim
`ClaimTypes.NameIdentifier`. Como desde a Fase 1 configuramos
`MapInboundClaims = false` no JWT (pra manter os nomes dos claims exatamente
como emitidos, lembra do bug do `sub`?), o claim de identidade do usuário
continua sendo literalmente `"sub"`. Sem um `IUserIdProvider` próprio lendo
esse claim, `Clients.User(userId)` nunca encontraria a conexão de
ninguém — mais uma consequência direta (e, dessa vez, esperada) daquela
decisão da Fase 1.

**Token JWT via query string, só pra rota do Hub**: navegadores não deixam
adicionar um header `Authorization` customizado no handshake HTTP que
inicia uma conexão WebSocket. A prática padrão (documentada pela própria
Microsoft) é mandar o token como `?access_token=...` na URL de conexão, e
capturá-lo de volta no pipeline de autenticação via
`JwtBearerEvents.OnMessageReceived` — mas só quando o caminho da requisição
começa com `/hubs`, pra não abrir uma segunda forma de autenticar endpoints
REST normais (que continuam exigindo o header `Authorization` de sempre).

**Validado na prática, de um jeito que prova exatamente o ponto**: escrevi
um cliente SignalR descartável (`.NET`, fora do repositório, só pra esse
teste), conectei ele na sessão, e disparei reserva + confirmação por HTTP
enquanto ele escutava. A sequência de eventos recebida:

```
[EVENT] SeatStatusChanged: {"seatId":"...","status":"Reserved"}   <- publicado pela Api
[EVENT] SeatStatusChanged: {"seatId":"...","status":"Sold"}        <- publicado pelo Worker
[EVENT] OrderConfirmed:    {"orderId":"..."}                       <- publicado pelo Worker
```

As duas últimas mensagens vieram de um processo (`TicketFlow.Worker`) que
nunca teve — e nunca terá — uma conexão WebSocket aberta com esse cliente.
Isso só funciona por causa do backplane no Redis. Essa é a prova mais
convincente que fiz até agora nesse projeto de que a arquitetura em
camadas realmente compensa: a lógica de "avisar o cliente" foi escrita uma
vez só (`IRealtimeNotifier`), e funciona igual não importa de qual
processo ela é chamada.

---

## Fase 6 — Rate limiting

**O que foi feito**: três políticas de limite de requisição, usando o
middleware nativo do ASP.NET Core (`Microsoft.AspNetCore.RateLimiting`, sem
pacote NuGet extra):

1. **Global (fallback), por IP** — 100 requisições/minuto, aplicado a toda
   rota que não tenha uma política mais específica. É uma rede de segurança
   genérica, não a defesa principal.
2. **`auth`, por IP** — 5 requisições/minuto em `POST /api/auth/register` e
   `POST /api/auth/login`. Protege contra força bruta de senha e
   *credential stuffing*.
3. **`reserve`, por usuário** — token bucket com 5 tokens, recarregando 5 a
   cada 10 segundos, em `POST /api/orders/reserve`. Essa é a política que
   realmente importa pro domínio: é o endpoint que um bot tentando travar
   todos os assentos de um show de propósito ia martelar.

**Por que `reserve` é particionado por usuário e não por IP**: esse
endpoint já exige `[Authorize]`, então particionar por `sub` (o claim do
usuário no JWT) significa que trocar de IP não dá a um atacante nenhum
token extra — ele precisaria de contas novas, o que já é uma barreira bem
mais alta. Os endpoints de `auth`, por outro lado, particionam por IP
porque é justamente ali que ainda **não existe** usuário autenticado — é o
único identificador disponível nesse ponto.

**Por que token bucket pro `reserve` e janela fixa pro resto**: uma janela
fixa (*fixed window*) é mais simples de raciocinar, mas tem um problema
conhecido nas bordas — alguém pode disparar o limite inteiro no último
segundo de uma janela e o limite inteiro de novo no primeiro segundo da
janela seguinte, efetivamente dobrando a taxa real por um instante. Pro
`reserve`, onde a preocupação é especificamente um bot rajando pedidos,
essa brecha importa mais. Token bucket evita isso ao recarregar
continuamente (5 tokens a cada 10s) em vez de resetar tudo de uma vez —
e ainda deixa uma pessoa legítima reservando ingressos pra vários amigos
de uma vez fazer isso sem tropeçar no limite.

**A pegadinha que confirmei testando**: o rate limiter roda **antes** do
model binding / validação do FluentValidation — ele conta a requisição
pelo simples fato de ter chegado no endpoint, não pelo que tem dentro do
corpo. Descobri isso na prática: rodei um teste com payload malformado
(um bug no meu próprio script de teste, não na API) e mesmo assim vi o
padrão exato de "5 primeiras contam, as 3 seguintes tomam 429" — o que na
verdade acabou sendo uma boa confirmação *independente* de que o contador
do limitador funciona certo, já que nem chegou a importar se o corpo da
requisição era válido. Depois repeti com payloads corretos e confirmei:
5 reservas `200`, as 3 seguintes `429`.

**Corpo da resposta e `Retry-After`**: assim como o
`ExceptionHandlingMiddleware` da Fase 1, um `429` vem com corpo
`application/problem+json` consistente com o resto da API, mais o header
`Retry-After` (em segundos) — pra quem está automatizando o consumo da API
saber exatamente quanto esperar antes de tentar de novo, em vez de ficar
adivinhando ou tentando imediatamente (o que só faria o backoff exponencial
de um cliente bem-comportado demorar mais pra convergir).

---

## Fase 7 — Testes de integração de ponta a ponta (Testcontainers)

**O que foi feito**: um projeto novo, `TicketFlow.IntegrationTests`, que
sobe a API **de verdade** (via `WebApplicationFactory<Program>`) contra
Postgres, Redis e RabbitMQ **reais**, rodando em containers Docker
descartáveis criados e destruídos automaticamente a cada execução via
[Testcontainers](https://testcontainers.com/). O teste principal dispara 5
requisições `POST /orders/reserve` em paralelo para o **mesmo** assento e
prova, de forma automatizada e repetível, que exatamente uma tem sucesso —
o mesmo cenário que eu validava manualmente com `curl` desde a Fase 3, mas
agora rodando com `dotnet test`, sem nenhum passo manual.

**Por que não bastava mockar os repositórios**: os 33 testes de domínio
provam que as *regras de negócio* estão certas — `Seat.Reserve()` não pode
ser chamado duas vezes, etc. Mas a race condition que o Redis resolve não
vive na regra de negócio, vive na **interação entre processos concorrentes
e um banco de dados real**. Um teste de unidade com repositórios falsos
(uma lista em memória, por exemplo) não seria capaz de provar isso — daria
"verde" mesmo se o lock distribuído estivesse quebrado, porque nada ali
seria realmente concorrente do jeito que múltiplas instâncias da API
disputando o Postgres são. Só um teste contra a infraestrutura de verdade
prova a coisa real.

**Por que Testcontainers em vez de exigir `docker compose up` antes de
rodar os testes**: com Testcontainers, `dotnet test` sozinho já sobe (e
depois destrói) os containers necessários — ninguém que clonar esse
repositório precisa lembrar de nenhum passo manual antes de rodar a suíte
inteira, e os containers de teste nunca colidem com os do
`docker-compose.yml` de desenvolvimento (portas mapeadas dinamicamente,
banco de dados isolado). É também exatamente como pipelines de CI/CD fazem
isso hoje em dia.

**Dois bugs reais que esse teste pegou, que nenhum teste de unidade jamais
pegaria**:

1. **Credenciais do RabbitMQ**: o container de teste do RabbitMQ gera
   usuário/senha aleatórios por padrão — bastou fixar `guest`/`guest`
   explicitamente na configuração do container de teste pra bater com o
   que a aplicação esperava.
2. **Um problema de design real, não só um problema de teste**: o teste
   revelou que `POST /orders/reserve` — que nunca usa a fila — estava,
   mesmo assim, **exigindo uma conexão viva com o RabbitMQ** pra
   funcionar. A causa: o ASP.NET Core resolve a árvore de dependências
   inteira do construtor assim que alguém precisa de uma instância.
   `OrdersController` depende de `ReservationService`, que depende de
   `IOrderQueue`, que (antes da correção) dependia de `IConnection`
   diretamente — então, só de **construir** `ReservationService` pra
   atender *qualquer* rota do controller, a aplicação tentava conectar no
   RabbitMQ, mesmo que a rota específica (`reserve`, `cancel`, `get`)
   nunca fosse tocar a fila. Corrigi trocando a dependência de
   `RabbitMqOrderQueue` de `IConnection` para `Lazy<IConnection>` — agora
   a conexão só é estabelecida de verdade dentro de
   `EnqueueConfirmationAsync`, que só `RequestConfirmationAsync` chama.
   Isso significa que, em produção, se o RabbitMQ cair, reservar e
   cancelar continuam funcionando normalmente — só confirmar pagamento
   fica indisponível, que é o raio de impacto correto (muito menor do que
   "a venda de ingressos inteira para" que era o comportamento antes).

Esse segundo achado é o tipo de coisa que só aparece quando você testa
contra infraestrutura de verdade — com repositórios/filas falsos, o "bug"
nem existiria, porque um fake nunca falha ao conectar.

**Limite de concorrência ajustado pra caber no rate limit**: os testes
disparam só 5 requisições simultâneas (não 20, como na primeira versão),
porque o token bucket da Fase 6 já limita reservas a 5 por 10s por
usuário. Rodar mais que isso misturaria duas causas possíveis de rejeição
(`409` do lock vs. `429` do rate limiter) numa única asserção, tornando
uma falha futura ambígua sobre qual mecanismo quebrou.

**Adendo (achado na Fase 9): os testes desta fase passavam pelo motivo
errado**. Ao começar a Fase 9 rodei `docker compose down` pra testar tudo
containerizado do zero — e, pra minha surpresa, `dotnet test` nos testes de
integração passou a falhar com "Failed to connect to 127.0.0.1:5432"
(a porta *fixa* do Postgres, não a porta dinâmica que o Testcontainers
mapeia). Investigando, descobri que `TicketFlowApiFactory` sobrescrevia a
connection string via `ConfigureWebHost` → `ConfigureAppConfiguration`, mas
isso roda **depois** que o próprio `Program.cs` já executou
`AddInfrastructure(builder.Configuration)` — que lê `configuration.
GetConnectionString("Postgres")` e captura o valor numa variável local
imediatamente, antes de qualquer coisa que o `WebApplicationFactory`
injete depois. Ou seja: o teste **nunca** usou o container do
Testcontainers — ele sempre se conectou, sem eu perceber, no Postgres do
meu `docker-compose` local, que por acaso já estava rodando na porta 5432
o tempo todo durante as Fases 3 a 8. Só quebrou quando derrubei esse
container. A correção: sobrescrever via **variável de ambiente**
(`Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", ...)`)
antes de acessar `Services` — variáveis de ambiente são lidas por
`WebApplication.CreateBuilder` no início da execução do `Main`, então
chegam a tempo, ao contrário de uma configuração adicionada depois que o
código já leu e capturou o valor. É a mesma técnica (e o mesmo motivo) que
o `docker-compose.yml` da Fase 9 usa pra apontar os serviços um pro outro.

O motivo de eu estar contando isso com essa riqueza de detalhe: é um
lembrete de que "os testes passam" não é a mesma coisa que "os testes
provam o que eu acho que provam" — vale a pena, de vez em quando,
propositalmente quebrar a dependência externa que um teste diz usar (aqui,
derrubar o Postgres local) só pra confirmar que ele falha do jeito
esperado quando ela não existe.

---

## Fase 8 — Observabilidade (OpenTelemetry: métricas + tracing distribuído)

**O que foi feito**: duas coisas complementares, ambas via OpenTelemetry:

1. **Métricas de negócio** — um `Meter` customizado (`TicketFlowMetrics`, na
   `Application`) com contadores (assentos reservados, rejeições de lock,
   pedidos confirmados) e um histograma (duração da reserva). Exposto em
   formato Prometheus: `/metrics` na API (via `AddPrometheusExporter` +
   `MapPrometheusScrapingEndpoint`), e `:9464/metrics` no worker (via
   `AddPrometheusHttpListener`, já que o worker não tem Kestrel/ASP.NET
   Core pra hospedar um endpoint normal).
2. **Tracing distribuído** — instrumentação automática de ASP.NET Core,
   `HttpClient` e EF Core, mais spans manuais nos pontos que importam
   (`ReserveSeats`, `AcquireSeatLock`, `ConfirmOrder`), exportados via OTLP
   pro Jaeger.

**Por que um `Meter`/`ActivitySource` na `Application` em vez de chamar a
API do OpenTelemetry direto**: `System.Diagnostics.Metrics.Meter` e
`System.Diagnostics.ActivitySource` são tipos do **BCL** (`System.
Diagnostics.DiagnosticSource`), não do pacote OpenTelemetry — é assim que o
.NET foi desenhado de propósito, pra separar "instrumentar seu código" de
"escolher pra onde os dados vão". A `Application` cria contadores e spans
sem saber que OpenTelemetry existe; é a `Infrastructure` quem "escuta"
esses nomes (`AddMeter("TicketFlow")`, `AddSource("TicketFlow")`) e decide
exportar pra Jaeger/Prometheus. Trocar o backend de observabilidade no
futuro (Application Insights, Datadog, etc.) não tocaria uma linha sequer
da lógica de negócio.

**O problema real que o tracing resolve**: até aqui, entender "o que
aconteceu com o pedido X" significava caçar linhas de log em dois
terminais diferentes (API e worker) e casar manualmente pelo `orderId`.
Com tracing distribuído, um clique no Jaeger mostra a árvore inteira — do
`POST /orders/reserve` até o `ConfirmOrder` rodando no worker, minutos
depois, em outro processo — como uma coisa só.

**A parte tecnicamente mais interessante: propagar contexto através do
RabbitMQ**. HTTP propaga contexto de trace automaticamente (headers
`traceparent`/`tracestate`, padrão W3C) — é por isso que a instrumentação
automática de `HttpClient` "simplesmente funciona". Uma mensagem de fila
**não tem esse mecanismo de graça**: é só um array de bytes. Sem fazer nada
extra, o span do worker processando a mensagem apareceria no Jaeger como um
trace **completamente novo e desconectado** do trace da API que publicou a
mensagem — tecnicamente "tracing", mas sem o "distribuído" que dá valor a
isso.

A correção manual, nos dois lados:
- **Publicando** (`RabbitMqOrderQueue`): pega o contexto do trace atual
  (`Activity.Current`) e injeta ele nos *headers* da mensagem AMQP, usando
  `Propagators.DefaultTextMapPropagator.Inject(...)` — o mesmo mecanismo
  que a instrumentação automática de HTTP usa por baixo dos panos, só que
  chamado à mão.
- **Consumindo** (`OrderConfirmationConsumer`): lê esses headers de volta
  com `.Extract(...)` e usa o resultado como **pai** do novo span
  (`ActivityKind.Consumer`), em vez de deixar o span nascer sem pai.

O resultado, verificado na prática consultando a API do Jaeger: **um único
`traceID`** contendo spans dos dois processos —
`[Api] POST .../confirm` → `[Api] order-confirmations publish` →
`[Worker] order-confirmations process` → `[Worker] ConfirmOrder` — exatamente
o "caminho completo de uma compra" que o roadmap original pedia.

**Por que os exportadores de Prometheus são diferentes na API e no
worker**: `AddPrometheusExporter` (pacote `...AspNetCore`) precisa de
roteamento de endpoints do ASP.NET Core pra funcionar — a API já tem isso.
O worker é um *generic host* sem Kestrel; usar o mesmo exportador exigiria
adicionar hospedagem web só pra isso. O pacote
`OpenTelemetry.Exporter.Prometheus.HttpListener` existe exatamente pra esse
cenário: sobe um `System.Net.HttpListener` cru, sem framework nenhum por
trás, servindo o mesmo formato de texto Prometheus.

**Validado na prática**: reservei e confirmei um pedido real; consultei
`GET http://localhost:16686/api/traces?service=TicketFlow.Worker&
operation=order-confirmations%20process` e confirmei um `traceID` só
contendo spans de `TicketFlow.Api` e `TicketFlow.Worker`; conferi
`/metrics` na API (`ticketflow_seats_reserved_seats_total`,
`ticketflow_reservation_duration_milliseconds`) e `:9464/metrics` no
worker (`ticketflow_orders_confirmed_orders_total`), ambos com valores
reais refletindo exatamente a operação que acabara de rodar.

---

## Fase 9 — Empacotamento final

**O que foi feito**: a API e o worker ganharam `Dockerfile`s (multi-stage
build) e entraram no `docker-compose.yml`, que agora sobe o sistema
inteiro — banco, cache, fila, tracing, **e a aplicação** — com um único
`docker compose up -d --build`. Até a Fase 8, o compose só subia
infraestrutura; API e worker sempre rodaram via `dotnet run` direto na
máquina. Também entrou um diagrama de arquitetura (Mermaid) no `README.md`
e uma tabela resumindo as 9 fases.

**Por que manter as duas formas de rodar** (tudo em container vs. API/worker
locais + infra em container): são públicos diferentes. "Tudo em container"
é o que alguém avaliando o projeto roda pra ver funcionando sem instalar
nada além de Docker. "API/worker locais" é o que você usa no dia a dia
desenvolvendo — sem rebuild de imagem a cada mudança de código. O
`docker-compose.yml` sozinho serve os dois casos: `docker compose up -d
postgres redis rabbitmq jaeger` sobe só a infra (fluxo de desenvolvimento);
acrescentar `--build` sem restringir os serviços sobe tudo.

**Bug real encontrado containerizando o worker**: a imagem final do
`Dockerfile` do worker inicialmente usava `mcr.microsoft.com/dotnet/
runtime:8.0` — parecia certo, já que o worker é um *generic host* sem
Kestrel, não uma aplicação web. Ao rodar, ele crashava na inicialização
pedindo o framework compartilhado `Microsoft.AspNetCore.App`, que essa
imagem não tem. Causa: `TicketFlow.Worker` referencia
`TicketFlow.Infrastructure`, que contém `TicketFlowHub : Hub` (SignalR) —
mesmo o worker nunca hospedando esse Hub (só publica nele via `IHubContext`
através do backplane), o tipo `Hub` em si exige o runtime do ASP.NET Core
carregado no processo. Troquei a imagem final pra
`mcr.microsoft.com/dotnet/aspnet:8.0` (a mesma que a API usa) e resolveu.
Isso só apareceu rodando containerizado — `dotnet run` local nunca teria
esse problema, porque o SDK instalado na máquina já tem todos os runtimes
disponíveis, mascarando a dependência real.

**Connection strings diferentes por ambiente, via variáveis de ambiente**:
dentro de um container, `localhost` aponta pro próprio container, não pros
outros serviços do compose. O `docker-compose.yml` sobrescreve
`ConnectionStrings__Postgres`, `ConnectionStrings__Redis`, `RabbitMq__*` e
`Otel__OtlpEndpoint` nos serviços `api` e `worker` pra usar os nomes dos
serviços do Docker (`postgres`, `redis`, `rabbitmq`, `jaeger`) — a mesma
técnica de sempre (variável de ambiente sobrescrevendo `appsettings.
Development.json`, igual a chave JWT desde a Fase 1), só que aplicada a
mais um ambiente (container) além de "local" e "teste".

**Uma pegadinha a mais, específica do worker**: o listener HTTP do
Prometheus (Fase 8) estava fixo em `http://localhost:9464/`, o que funciona
rodando local mas seria inacessível de fora do container (ninguém consegue
fazer *scrape* de uma porta que só escuta loopback). Virou configurável
(`Otel:PrometheusListenerPrefix`), com `localhost` como padrão seguro pro
`dotnet run` (evita precisar de privilégio de administrador no Windows pra
abrir um `HttpListener` em wildcard) e `http://+:9464/` só quando
containerizado (sem essa restrição dentro do Linux do container).

**Validado na prática**: build das duas imagens, subida do stack completo
(6 containers), e o mesmo teste de ponta a ponta de sempre — registro,
criar evento/sessão/assento, reservar, confirmar — rodando inteiramente
contra os serviços containerizados. Conferi também `/metrics` nos dois
processos e o Jaeger recebendo traces de `TicketFlow.Api` e
`TicketFlow.Worker`, tudo rodando de dentro de containers Docker.

---

Esse documento acompanhou as 9 fases do roadmap original, da estrutura
inicial da solution até um sistema completo rodando containerizado com
observabilidade de ponta a ponta. Se você chegou até aqui lendo tudo:
esse é o material — junto com o código e os commits — que sustenta uma
conversa de entrevista técnica sobre cada decisão tomada neste projeto.
