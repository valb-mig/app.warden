# Warden — Notas de Propósito, Arquitetura e Segurança

Resumo completo da discussão sobre a evolução do app.warden, antes da migração PHP/TS → C#.

---

## 1. Propósito do projeto

Ferramenta de **uso pessoal** para gerenciamento remoto de máquinas próprias (Windows/Linux). Não é um produto multiusuário nem um substituto de Portainer/Cockpit/SSH — é uma **camada de ergonomia** sobre coisas que já são tecnicamente possíveis hoje, combinada com o motivo pessoal declarado de migrar/aprender C#.

## 2. Conceito: Agent / Console / Admin

O modelo evoluiu de dois pra três papéis ao longo da discussão:

- **Agent**: processo de fundo (Windows Service / systemd), sem GUI própria. Expõe duas superfícies diferentes:
  - API do **console**, acessível só pela interface do Tailscale
  - API de **admin**, acessível só localmente (IPC), nunca pela rede
- **Console**: quem acessa remotamente e comanda o Agent — não é um painel de leitura passiva, é comando de verdade. É uma URL (ex: porta 3000) acessada via Tailscale — sem instalação, sem app próprio.
- **Admin (tray app)**: processo separado, rodando na sessão gráfica do usuário, na própria máquina raiz. É quem configura, aprova pastas, cria scripts sem projeto, sincroniza, e mostra status do serviço (rodando/parado) via ícone na bandeja do sistema.

### Por que Agent e Admin precisam ser processos separados

Um serviço de fundo **não consegue** desenhar ícone de bandeja — não é limitação de framework, é restrição do SO: no Windows, serviços rodam isolados na Session 0 desde o Vista, sem acesso à sessão gráfica; no Linux, um serviço systemd fora de sessão gráfica não tem desktop pra desenhar nada. Por isso o modelo final é: **agent** (serviço, sem GUI) + **tray app** (processo de usuário, com GUI), conversando entre si por IPC local.

## 3. Como Agent e Admin (tray) se comunicam

- **Named Pipes** (Windows) / **Unix Domain Socket** (Linux) — o Kestrel do .NET 8+ suporta os dois de forma unificada (`ListenNamedPipe` / `ListenUnixSocket`), sem código condicional por SO no domínio da aplicação.
- Essa escolha é mais restrita que TCP em `127.0.0.1`: named pipe/socket local pode ter permissão de arquivo travada ao usuário do SO, resolvendo o cenário de "outra conta de usuário na mesma máquina" sem depender de bind de porta nenhum.
- **`localhost`/TCP sozinho não seria suficiente** pra esse cenário específico (multiusuário na mesma máquina física) — é a única situação identificada no debate onde IPC nativo supera bind em loopback.

## 4. Escopo do que o Warden executa

- **Sem Docker Engine API** — Docker não é mais o núcleo do projeto. Interação com containers via CLI (`docker compose ps / logs -f / stop / up`), quando o usuário indica os arquivos.
- **Descoberta de scripts** a partir de convenções existentes: `package.json`, `composer.json`, Justfile, entre outros.
- **Scripts complexos/sem projeto**: configurados manualmente pelo usuário em `~/.warden`, definidos em `.toml` — essa parte é função do **Admin**, não do console.
- **Usuário escolhe o diretório** explicitamente — sem indexação automática da máquina.

## 5. Decisões de segurança

- **Bind restrito por superfície**: console só na interface Tailscale; admin só via IPC local. Nenhuma delas expõe porta em `0.0.0.0`.
- **Vazamento do link do console não é o vetor de risco** — o bind restrito à interface Tailscale já torna a URL inofensiva fora da rede. O segredo real é participar do tailnet, não conhecer a URL.
- **Compartilhar o tailnet é decisão do usuário final**, fora da responsabilidade do Warden — documentar isso explicitamente no README.
- **Sem sistema de auth multiusuário no roadmap** — desnecessário pro escopo de uso pessoal.
- **Ações de confiança (aprovar pasta, criar script novo) devem viver só no Admin, nunca no console** — evita recriar "execução arbitrária remota" disfarçada de tela de configuração.

## 6. Pendências de engenharia identificadas

Independentes da linguagem, valem ser resolvidas antes ou durante a estruturação inicial:

1. **Assert de bind na inicialização**: o agent deve recusar subir se não detectar bind correto na interface do Tailscale (evita erro futuro tipo `0.0.0.0` acidental ou uso indevido de Funnel).
2. **Confiança de pasta não pode ser permanente e cega**: guardar hash do conteúdo do arquivo de scripts no momento da aprovação. Se o hash mudar depois (`git pull`, edição, `postinstall` de dependência), a próxima execução exige revisão antes de liberar o botão de novo.
3. **"Mostrar comando antes de rodar" precisa ser atômico**: o que é exibido na tela e o que é executado no clique devem vir da mesma leitura de arquivo — evita uma janela de tempo (TOCTOU) entre listar botões e executar.
4. **Prompt de confiança por pasta**: ao adicionar diretório novo, perguntar explicitamente antes de expor qualquer script como executável.
5. **Gerenciamento de processo filho do CLI do Docker**: matar o processo filho (ex: `docker compose logs -f`) quando a conexão do console cair, evitando processos órfãos.

## 7. Stack técnica

- **Linguagem escolhida**: C#/.NET.
- Com Docker Engine API fora do escopo e sem uso de `tsnet` embutido (rede depende do cliente Tailscale já instalado), os dois maiores argumentos técnicos que favoreciam Go (SDK oficial de Docker, embutir identidade Tailscale no binário) deixaram de se aplicar ao projeto. **C# é, hoje, uma escolha tecnicamente sólida pra esse escopo — não apenas uma escolha de carreira.**
- **Tauri e frameworks equivalentes (Electron, Wails) não se aplicam ao Agent** — ele não tem GUI local, é sempre acessado de fora (pelo Console). Esses frameworks resolvem "janela nativa leve", problema que o Agent não tem.
- **Para o Admin/tray, o candidato correto em C# é Avalonia** (cross-platform, Windows + Linux) — WPF/WinForms não servem, são Windows-only. Photino.NET é o equivalente C# do conceito Tauri (webview + backend), caso se prefira essa abordagem em vez de UI nativa Avalonia.
- **Ressalva sobre bandeja no Linux**: não é garantida mesmo com biblioteca madura — GNOME removeu suporte nativo a `StatusNotifierItem`, exigindo extensão do usuário. Funciona de forma confiável no Windows; no Linux, depende da desktop environment. Documentar essa diferença, não prometer paridade.
- Outras peças discutidas: ASP.NET Core Minimal API + Kestrel, SignalR, Generic Host (Windows Service/systemd), SQLite, `Tomlyn` (parsing de `.toml`), Web Push + `WebPush` (NuGet) para notificação com aba fechada.
- **Abstração para troca futura de transporte**: como o plano é usar Tailscale hoje e possivelmente construir um sistema próprio de conexão multi-aparelho depois, isolar isso atrás de uma interface (ex: `IConsoleTransport`) desde já, sem espalhar chamadas específicas de Tailscale pelo código de domínio. A implementação de hoje é `TailscaleTransport`; football futuro seria trocar por uma implementação própria sem tocar em execução de script, notificação ou histórico.
- **Ponto em aberto, sem decisão fechada**: Blazor Server tem fraqueza real em rede instável (celular trocando wifi/4G, app em background), porque o estado vive preso ao circuito SignalR do servidor. Uma UI com reconexão do lado do cliente tolera melhor esse cenário — vale decidir antes de investir na camada de frontend do console.

## 8. O debate sobre justificativa do projeto (SSH como baseline)

Essa parte do debate testou se o Warden resolve um problema real ou reinventa a roda. O concorrente mais forte identificado não foi Portainer nem Cockpit — foi **SSH com Tailscale**, que já é possível hoje, sem nenhuma linha do Warden.

**O que SSH puro já resolve, sem o Warden:**
- Acesso remoto multiplataforma (OpenSSH nativo em Windows e Linux desde 2018 — não é diferencial do Warden)
- Múltiplas máquinas com atalho de configuração (`~/.ssh/config` com aliases resolve "casa vs. trabalho" numa linha)
- Descoberta de scripts dentro de cada ecossistema: `npm run` (sem argumento), `just --list`, `composer run-script` já listam os comandos disponíveis — isso não é exclusividade do Warden

**As três frestas reais e distintas que sobraram, depois de descartar o que já existe:**
1. **Fricção de teclado**: digitar comando em teclado de celular é ruim de verdade; clicar em botão elimina isso — mas só pra scripts já catalogados, não pra comando ad-hoc
2. **Visão agregada de várias máquinas ao mesmo tempo**: SSH dá uma sessão por vez, focada numa máquina; não existe visão consolidada sem construir algo em cima — esse é o argumento mais forte a favor do projeto
3. **Normalização de convenções heterogêneas**: cada ecossistema tem sua própria sintaxe (`npm run`, `just`, `composer run-script`, ou nada, no caso de `.sh` solto). O usuário precisa lembrar qual convenção cada projeto usa; o Warden unifica isso num único formato de botão, independente do que há por trás

**Limite explícito, pra não inflar expectativa**: o Warden reduz a frequência de uso do terminal, não o elimina. Para qualquer coisa não pré-catalogada (comando ad-hoc, flag diferente do de sempre), o SSH continua sendo necessário — isso não é falha de design, é o limite natural de "scripts pré-configurados".

## 9. Avaliação honesta do projeto (opinião)

Sem inflar nem desinflar: o Warden **não preenche uma lacuna funcional inexistente no mercado** — tudo que ele faz já é tecnicamente possível hoje, espalhado entre SSH, `npm run`/`just`/`composer`, ntfy, e disciplina pessoal de organização. Nesse sentido, chamá-lo de categoria nova de software (como cheguei a especular quando Docker ainda estava no escopo) não se sustenta depois da revisão.

O que sustenta o projeto, de forma honesta, é a combinação de dois motivos legítimos e suficientes por si só:
- **Motivo de engenharia**: as três frestas da seção 8 são reais, específicas, e não resolvidas por nenhuma ferramenta única hoje — o valor está em juntar tudo isso num fluxo de um clique, multiplataforma, multimáquina, sem exigir que o usuário decore convenção nenhuma
- **Motivo pessoal**: aprender C# migrando de PHP/TS construindo algo real, com escopo que hoje é honesto tecnicamente (depois de tirar Docker do núcleo, C# deixou de ser desvantagem de stack)

Ao longo da conversa, boa parte do valor "revisado pra baixo" (Docker como diferencial, categoria nova, "inferno" sem o Warden) foi corrigido pra um tamanho mais realista — e o que sobrou depois dessa poda é, na minha avaliação, um projeto que vale a pena construir: não porque resolve algo impossível, mas porque resolve fricção real o suficiente pra justificar o esforço, com escopo de segurança bem definido (Tailscale + separação agent/admin) e uma escolha de stack (C#) que hoje é defensável tecnicamente, não só emocionalmente.

---

## 10. Decisões resolvidas (sessão 2026-07-14)

Fechamento dos pontos que ficaram em aberto nas seções 6-7, com solução concreta o suficiente pra virar código quando a migração começar.

### 10.1 Frontend do Console: reaproveitar o Next.js existente, Blazor sai de cogitação

O ponto em aberto da seção 7 ("Blazor Server tem fraqueza real em rede instável") tinha uma pergunta errada embutida: a comparação era Blazor Server vs. Blazor WASM, mas nenhuma das duas precisa existir. O `web/` atual (Next.js/PWA) já é o frontend real, já validado em produção (fase 6-7 do MVP: Playwright + celular real via Tailscale), e já resolve reconexão do lado do cliente — é exatamente a propriedade que faltava no Blazor Server.

**Decisão:** a migração troca o *backend* (Python → C#), não o frontend. `web/` continua Next.js/TS, só passa a apontar pra API do Agent em C# em vez da FastAPI. Pra log ao vivo, trocar o `WebSocket` cru por um Hub SignalR + cliente oficial `@microsoft/signalr` — ele já traz reconexão automática com backoff embutida, sem reinventar nada. Isso elimina o debate Blazor inteiro (não é mais uma escolha a fazer) e corta um bloco inteiro de esforço de migração (reescrever UI já madura). "Aprender C#" fica satisfeito pelo lado do Agent/Admin, que é onde a lógica de domínio mora mesmo.

**Por quê isso é seguro:** o contrato REST/eventos que o front já consome (`/projects`, `/projects/{id}/logs`, `/projects/{id}/git`, etc.) vira a especificação de compatibilidade do novo backend — se o Agent em C# expõe os mesmos formatos, o front não muda quase nada além do client de log.

### 10.2 Assert de bind — resolvido eliminando a janela de erro, não só detectando

A pendência #1 da seção 6 pedia "recusar subir se não detectar bind correto". Checar *depois* do Kestrel já ter subido tem uma janela onde já é tarde. Solução mais forte: nunca dar ao Kestrel a chance de escolher.

- No boot, resolver o IP Tailscale via `tailscale ip -4` (shell out, mesma filosofia já adotada pra Docker CLI — sem SDK/`tsnet` embutido). Se o comando falhar ou não retornar IP (`tailscaled` não rodando, máquina fora da tailnet), o Agent **não sobe** — falha de boot explícita, não fallback silencioso.
- A superfície do Console é configurada com `ConfigureKestrel(o => o.Listen(new IPEndPoint(IPAddress.Parse(tailscaleIp), porta)))` — um `IPEndPoint` explícito, nunca `UseUrls`/`ASPNETCORE_URLS`/wildcard. Não existe caminho de código que aceite `0.0.0.0` pra essa superfície.
- Defesa em profundidade (cinto e suspensório): depois do `app.Start()`, ler `IServerAddressesFeature.Addresses` e assertar que bate exatamente com o IP resolvido. Qualquer divergência (alguém reintroduziu `UseUrls` num merge futuro, por exemplo) → `Environment.FailFast` imediato, não log-e-continua.
- A superfície do Admin nunca entra nesse caminho — não tem `Listen` de rede nenhum, só named pipe/unix socket (seção 10.6).

### 10.3 Confiança de pasta — hash do manifesto derivado, não do arquivo cru

A pendência #2 pedia invalidar aprovação quando o conteúdo muda. Hashear o arquivo bruto geraria reaprovação por qualquer mudança cosmética (comentário, espaço). Em vez disso, o hash é sobre a **superfície executável já resolvida**: a lista canônica `(nome do botão, argv do comando, cwd)` depois de parseada do `package.json`/`composer.json`/Justfile/`.toml` — serializada de forma determinística (JSON ordenado) e passada por SHA-256.

- `TrustStore` (tabela nova no SQLite já existente, `trusted_manifests`): `project_id`, `digest`, `approved_at`. Aprovação é ação exclusiva do Admin (tray, sessão local) — nunca um endpoint HTTP do Console.
- A cada carregamento do projeto (mesmo poll de hot-reload que o Python já usa pra `.toml`), o Agent recalcula o digest do manifesto atual e compara com o aprovado. Divergência → projeto entra em estado `revisão pendente`: botões continuam visíveis no Console mas desabilitados, com o motivo explícito, até o Admin reaprovar.
- Reaprovar no Admin mostra o diff (lista antiga aprovada vs. lista nova resolvida) antes do clique — não é reaprovação cega de "confia de novo".

### 10.4 Atomicidade mostrar/executar — snapshot imutável, não reread

A pendência #3 (TOCTOU) some por construção se "o que é mostrado" e "o que é executado" forem literalmente o mesmo objeto em memória. Design: o parse do manifesto (10.3) produz um `CommandManifest` imutável, mantido em memória por projeto, só trocado atomicamente quando o watcher detecta mudança **e** ela já foi reaprovada (10.3). O endpoint que lista botões pro Console devolve `manifest.Commands` por referência/id; o executor, ao rodar um botão, indexa nesse **mesmo objeto** (`manifest.Commands[buttonId]`) — nunca reabre o arquivo no clique. Não existe caminho onde o comando executado seja lido de uma versão do arquivo diferente da que foi exibida.

### 10.5 Ciclo de vida do processo filho do Docker CLI — atrelado ao disconnect do Hub

A pendência #5 (matar `docker compose logs -f` órfão quando o Console cai) tem solução natural no modelo SignalR: `ChildProcessRegistry` (singleton) mapeia `connectionId → List<Process>`. Todo `Process.Start` de streaming (docker logs, exec interativo) registra ali antes de escrever a primeira linha. O Hub implementa `OnDisconnectedAsync` chamando `Kill(entireProcessTree: true)` (suporte nativo desde .NET 5, mata a árvore em Windows e Linux) pra tudo que essa conexão tinha aberto, e remove do registro. Mesmo padrão cobre timeout de conexão morta (SignalR já detecta via keep-alive/ping), sem precisar reinventar detecção de disconnect.

### 10.6 Token escopado — tabela SQLite com hash, nunca token cru em disco

Resolve a decisão #19 do TODO.md (que ficou em aberto: "falta formato de armazenamento"), já pensando na migração pra C#: tabela `scoped_tokens` no mesmo SQLite (não arquivo TOML novo — evita permissão 600 manual e mantém tudo num único ponto de auditoria). Colunas: `id`, `label`, `token_hash` (SHA-256 do token, nunca o valor cru), `allowed_project_ids` (JSON array), `created_at`, `revoked_at` (nullable). Emissão é só CLI local (`warden token create --projects=...`), imprime o token uma vez só — igual GitHub PAT/API key. Middleware de auth da API compara hash do bearer recebido contra a coluna, nunca decripta nada (não precisa, é comparação de hash).

### 10.7 Named pipe / unix socket do Admin — permissão presa ao usuário do SO

Detalhe que faltava nas seções 2-3: o socket precisa negar acesso a outro usuário da mesma máquina, não só existir. Windows: `PipeSecurity` restringindo ACL ao SID do usuário atual na criação do `NamedPipeServerStream`. Linux: unix socket em `$XDG_RUNTIME_DIR/warden/admin.sock` (ou `~/.warden/admin.sock` como fallback), diretório criado com `0700`, socket com `0600` — mesma disciplina já usada pro `api_token` do Python (permissão 600).

---

## 11. Status de maturidade — pré-condição pra iniciar a migração

O CONTEXT.md diz explicitamente "não iniciar em paralelo ao MVP atual". Olhando o TODO.md: fases 1-10 do MVP Python estão ✅ fechadas, incluindo os itens que antes estavam "em aberto" (detecção de erro PHP, hot-reload de TOML, install.sh em máquina limpa). O único item realmente pendente é a decisão #19 (token escopado), que a seção 10.6 acima já resolve o formato — falta só implementar no lado Python se quiser, ou pular direto pra versão C# já com o formato certo. **Na prática, a pré-condição de maturidade já foi atingida**; o que falta pra começar o scaffold é decisão de prioridade do Valb, não bloqueio técnico.

## 12. Fases de migração (rascunho, ordem por dependência real)

Mesma filosofia do TODO.md do MVP Python: cada fase produz algo testável antes de acoplar a próxima.

1. ~~**Scaffold da solução .NET**~~ ✅ (2026-07-14) — `agent/Warden.sln` (.NET 10) com `Warden.Contracts` (classlib, DTOs compartilhados Agent↔Admin — ainda vazio, schemas entram na fase 5), `Warden.Domain` (classlib + pacote `Tomlyn` pro parse `.toml`, ainda vazio — adapters/registry entram na fase 2), `Warden.Agent` (`webapi` minimal API, referencia Domain+Contracts), `Warden.Admin` (Avalonia desktop app, referencia só Contracts — não fala com Domain direto, só via IPC com o Agent), `Warden.Domain.Tests` (xunit, referencia Domain). Regra de dependência fixada aqui: **Domain não depende de Contracts** — a camada de domínio não conhece DTO de transporte, só o Agent faz o mapeamento nas bordas. `dotnet build`/`dotnet test` limpos (0 avisos, 0 erros); pin manual de `Microsoft.OpenApi` pra `2.7.5` no `Warden.Agent.csproj` porque o template trouxe `2.0.0` transitivo com vulnerabilidade alta conhecida (GHSA-v5pm-xwqc-g5wc, corrigida só a partir de `2.7.5`). Targets novos no `justfile` raiz: `just dotnet-build`, `just dotnet-test`, `just dotnet-agent`, `just dotnet-admin`. Ambiente: Arch separa SDK/runtime/targeting-pack do ASP.NET em três pacotes pacman distintos (`dotnet-sdk`, `aspnet-runtime`, `aspnet-targeting-pack`) — os três precisam estar instalados ou o `dotnet restore` de um projeto `Microsoft.NET.Sdk.Web` falha com `NETSDK1226`.
2. ~~**Domínio + adapters**~~ ✅ (2026-07-14) — `IAdapter` (start/stop/status/logs/services, com `SetOnExit`/`Services` como default interface methods, mesmo papel dos métodos não-abstratos da ABC Python) + `AdapterFactory` (switch em `ProjectConfig.Type`, que fica como `string`, não enum — Tomlyn mapeia enum pra número por padrão, então manter string evita converter custom e mantém o dado igual ao `.toml`, mesma escolha do `Literal[...]` do Pydantic). `ProcessAdapter` (base owned: python/raw/node/php/just, cada um uma subclasse de uma linha com primary constructor, igual aos arquivos-docstring do Python) usa **`Porta.Pty`** (pacote NuGet, PTY cross-platform de verdade — ConPTY no Windows, `forkpty` nativo no Linux/macOS) só quando `capture_stdout=true`; sem captura, sobe processo comum sem redirecionar (evita o risco de dead-lock de um PTY sem ninguém lendo). `DockerAdapter` portado 1:1 (shell-out `docker compose`, parse de JSON tolerante a array-ou-NDJSON dependendo da versão instalada, igual ao Python). Descoberta de porta: `LinuxPortDiscovery` cruza `/proc/[pid]/fd/*` (via `readlink()` cru por P/Invoke — a API de alto nível do .NET corrompe o alvo porque `socket:[N]` não é um path de verdade) com `/proc/net/tcp[6]`; `WindowsPortDiscovery` via `GetExtendedTcpTable`/iphlpapi — **implementado mas não validado em Windows real** (dev é Linux/Arch), revisitar antes de confiar em produção Windows. `VitalsSampler` usa `System.Diagnostics.Process.TotalProcessorTime`/`WorkingSet64` (cross-platform nativo do .NET, sem precisar de p/invoke tipo psutil) — CPU não normalizado por núcleo, mesma convenção do `psutil.cpu_percent()`. `Stop()` manda SIGTERM cru via P/Invoke em `libc.kill()` antes do kill forçado — `Process.Kill()` do .NET manda SIGKILL incondicional em Unix, não existe graceful shutdown nativo na API gerenciada. 27 testes xUnit (mirror de `engine/tests/test_config.py`, `test_registry.py`, `test_factory.py`, `test_logbuffer.py`, `test_process_adapter.py`, `test_docker_adapter.py`), todos passando **de verdade** — inclusive spawn real de `python3` via PTY e docker compose real (não mockado), igual ao rigor de validação que o MVP Python já seguia.
3. ~~**Bind guard + `IConsoleTransport`**~~ ✅ (2026-07-14) — `Warden.Agent/Transport/`: `IConsoleTransport.ResolveEndpoint(port)` (hoje só `TailscaleTransport`, shell out pra `tailscale ip -4`, valida a faixa CGNAT 100.64.0.0/10 antes de aceitar o IP — proteção extra contra resolução suspeita) roda **antes** de configurar o Kestrel; `Program.cs` passa o `IPEndPoint` resolvido direto pra `ConfigureKestrel(o => o.Listen(...))`, nunca `UseUrls`/wildcard (comportamento documentado do Kestrel: `Listen` explícito ignora *qualquer* endereço vindo de `ASPNETCORE_URLS`/config — a defesa primária realmente elimina a janela, não só detecta depois). `BindGuard.AssertSingleExpectedAddress` roda em `ApplicationStarted` como defesa em profundidade (cinto e suspensório) e `Environment.FailFast` se divergir — pega regressão futura tipo alguém reintroduzindo `UseUrls` num merge. Sem `tailscaled` rodando ou fora da tailnet, o Agent recusa subir (exit 1, mensagem clara), sem fallback silencioso. **Validado de ponta a ponta nesta máquina**: `dotnet run` sobe só em `http://100.122.90.18:8420` (IP Tailscale real), `curl` nesse IP devolve 200, `curl` em `localhost:8420`/`127.0.0.1:8420` recusa conexão — confirma que não há bind wildcard nem loopback acidental. HTTPS redirect removido do template (`UseHttpsRedirection`) — decisão explícita: a criptografia de transporte já é o WireGuard do Tailscale, não faz sentido gerenciar certificado TLS pra um IP interno da tailnet. 7 testes xUnit novos (`Warden.Agent.Tests`), incluindo resolução real do Tailscale desta máquina (mesmo rigor de "sem mock" das fases anteriores).
4. ~~**Trust layer**~~ ✅ (2026-07-14) — `Warden.Domain/Trust/`: `ManifestBuilder.Build(ProjectConfig)` resolve `[start]`+`[[actions]]` num `CommandManifest` imutável (descoberta automática de script ainda não entrou — depende do `scaffold.py`/`discovery.py` ainda não portados); `ManifestDigest` hasheia (SHA-256) o JSON canônico dos comandos já resolvidos, não o `.toml` cru — mudança cosmética no arquivo não força reaprovação, só mudança que altera o que um botão executa de fato. `SqliteTrustStore` grava em `trusted_manifests` no mesmo `warden.db` (não arquivo novo), guardando o **JSON do manifesto aprovado** além do digest — permite diff "aprovado vs. atual" numa UI de Admin futura, não só "bateu/não bateu hash". `ManifestRegistry` guarda um `ProjectManifestSnapshot` imutável por projeto (`ConcurrentDictionary`, trocado atomicamente no `Refresh`) com status `NeverApproved`/`PendingReview`/`Approved` — a mesma referência de snapshot serve listar E executar (nunca releitura entre as duas, ver §10.4); `Approve()` sempre recalcula antes de gravar, pra nunca aprovar um conteúdo desatualizado por corrida entre disco e clique. Projeto novo nasce `NeverApproved` (nunca auto-aprovado), mudança de conteúdo depois de aprovado vira `PendingReview`, não fica "aprovado por engano" nem "trava silenciosamente no conteúdo antigo". Pacote `Microsoft.Data.Sqlite` (+ pin manual de `SQLitePCLRaw.lib.e_sqlite3` pra `2.1.12` — a versão puxada por default tinha CVE-2025-6965 conhecido, sem patch disponível ainda na 2.1.11). 16 testes xUnit novos (manifest builder/digest determinístico e sensível a mudança de conteúdo/ordem, trust store real em SQLite de verdade incluindo persistência entre reaberturas do arquivo, registry cobrindo as três transições de status).
5. ~~**API REST + Hub SignalR (Console)**~~ ✅ (2026-07-16) — `Warden.Domain/Engine.cs` (novo): facade que amarra `Registry`+`AdapterFactory`+`ManifestRegistry`, cache de adapter por projeto (`ConcurrentDictionary`, mesmo padrão do `Engine._adapter` do Python). Decisão explícita de escopo: git/linguagens/histórico/audit ficam de fora — dependem de domínio ainda não portado (`git.py`, `languages.py`, `EventStore`), entram na fase 8. `Warden.Agent/Api/`: rotas REST espelhando o FastAPI atual onde já dá (`GET /projects`, `start`/`stop`/`status`/`logs`/`services`/`actions`/`actions/{name}`), JSON em snake_case (`ConfigureHttpJsonOptions` com `JsonNamingPolicy.SnakeCaseLower`) pra bater com o `web/src/lib/api.ts` existente. Erros de domínio viram status HTTP via `app.UseExceptionHandler` central (equivalente aos `@app.exception_handler` do FastAPI), não try/catch espalhado por rota.
   - **Extensão real em relação ao Python: `start` e `actions/{name}` passam pelo trust gate da fase 4** (`TrustStatus.Approved`), não só as ações — `ManifestBuilder` já incluía `start` no `CommandManifest` desde a fase 4 exatamente pra isso: mudar o comando de start também exige reaprovação antes de rodar de novo. Efeito colateral aceito conscientemente: **sem o Admin (fase 6) ainda não existir, nenhum projeto pode ser iniciado via API** até alguém chamar `Engine.Approve(projectId)` por um caminho de confiança (hoje só testes; a partir da fase 6, o handler IPC do Admin) — não existe (nem devia existir) rota HTTP pra aprovar.
   - `Warden.Domain/Trust/CommandExecutor.cs`: roda um `ManifestCommand` até o fim capturando stdout+stderr combinados, timeout de 300s — mesma semântica do `subprocess.run(..., timeout=300)` do `engine.run_action` Python. Ação interativa → 400; destrutiva sem `confirm=true` → 409; projeto/ação não aprovados → 403 (novo em relação ao Python).
   - Auth: `TokenStore.LoadOrCreate` reaproveita o **mesmo arquivo** `~/.warden/api_token` do engine Python (mesmo token bearer nos dois lados, permissão 600 via `File.SetUnixFileMode`) — valida ao vivo nesta máquina que o Agent lê o token real e responde 401/200 igual ao Python pros dois projetos já registrados (`lead-master-web`, `maps-scraper`).
   - SignalR: `LogsHub` substitui o WS cru do Python — auth via `access_token` na query string (mesma solução que o WS puro já usava, já que browser não seta header custom em WebSocket/SignalR nativamente); `Subscribe(projectId, service)` inicia um poll de 500ms por conexão sobre o `RingBuffer` do adapter (via `Engine.Logs`), empurra só as linhas novas (`Clients.Caller.SendAsync("LogLines", ...)`). `ChildProcessRegistry` (`Warden.Agent/Hubs/`) mapeia connectionId→processos e mata a árvore inteira em `OnDisconnectedAsync` (10.5) — hoje é só infra: o tailing atual não abre processo por conexão (lê do `RingBuffer` já existente), o primeiro produtor real (`docker compose logs -f`/exec interativo) chega na fase 8, mas a limpeza já funciona assim que algo passar a registrar processo ali.
   - **Testável de ponta a ponta sem mock**: `WebApplicationFactory<Program>` real (pacote `Microsoft.AspNetCore.Mvc.Testing`) contra Kestrel/TestServer de verdade, `Microsoft.AspNetCore.SignalR.Client` real pro Hub (transporte cai pra long polling só no teste, já que `TestServer` não faz WebSocket de verdade). `Program.cs` ganhou um branch `IsEnvironment("Testing")` que pula a resolução real de Tailscale/bind guard (só faz sentido pra bind de Kestrel de verdade) — nenhuma lógica de domínio é mockada, só esse gate de rede específico de teste.
   - **Validado ao vivo nesta máquina contra o `~/.warden` real** (não só teste automatizado): `dotnet run` bindou em `http://100.122.90.18:8420` de novo, `curl` sem token → 401, com token real do Python → 200 listando os 2 projetos reais, `start` sem aprovação → 403, `localhost:8420` recusa conexão (bind guard da fase 3 intacto). `trusted_manifests` apareceu no `warden.db` real (schema aditivo, mesma tabela da fase 4) sem tocar nas outras tabelas (`events`, `action_audit`) nem em nenhum projeto real.
   - 33 testes xUnit novos (15 em `Warden.Domain.Tests`: 11 de `Engine`, 4 de `CommandExecutor`; 18 em `Warden.Agent.Tests`: 13 de API REST via `WebApplicationFactory`, 2 de `LogsHub` via cliente SignalR real, 3 de `ChildProcessRegistry` com spawn real). Total da solução: 83 testes (58 `Warden.Domain.Tests` + 25 `Warden.Agent.Tests`).
6. **Admin IPC + tray (Avalonia)** — named pipe/UDS (10.7), fluxo de aprovar pasta nova, editar config.
7. **Frontend repoint** — `web/` aponta pro Agent em C#; troca client de `WebSocket` por `@microsoft/signalr` (10.1). Se o contrato do passo 5 ficou fiel ao atual, essa fase é a mais barata da lista.
8. **Paridade de feature vs. engine Python** — git (leitura+comandos+watch), linguagens, filtro de log, vitals (CPU/RAM), notifier — portar um de cada vez, na mesma ordem em que o Python validou (reduz risco: cada um já tem comportamento de referência pra comparar).
9. **Cutover** — rodar os dois engines lado a lado (projetos diferentes ou flag), validar de novo via Tailscale em dispositivo real, só então aposentar o engine Python (ou manter como referência, decisão do Valb na hora).