# Warden — Notas de Propósito, Arquitetura e Segurança

Resumo completo da discussão sobre a evolução do app.warden, antes da migração PHP/TS → C#.

---

## 1. Propósito do projeto

Ferramenta de **uso pessoal** para gerenciamento remoto de máquinas próprias (Windows/Linux). Não é um produto multiusuário nem um substituto de Portainer/Cockpit/SSH — é uma **camada de ergonomia** sobre coisas que já são tecnicamente possíveis hoje, combinada com o motivo pessoal declarado de migrar/aprender C#.

## 2. Conceito: Ator / Plateia / Admin

O modelo evoluiu de dois pra três papéis ao longo da discussão:

- **Ator**: processo de fundo (Windows Service / systemd), sem GUI própria. Expõe duas superfícies diferentes:
  - API da **plateia**, acessível só pela interface do Tailscale
  - API de **admin**, acessível só localmente (IPC), nunca pela rede
- **Plateia**: quem acessa remotamente. Diferente do sentido comum da palavra, aqui a plateia **comanda** o ator, não só observa. É uma URL (ex: porta 3000) acessada via Tailscale — sem instalação, sem app próprio.
- **Admin (tray app)**: processo separado, rodando na sessão gráfica do usuário, na própria máquina raiz. É quem configura, aprova pastas, cria scripts sem projeto, sincroniza, e mostra status do serviço (rodando/parado) via ícone na bandeja do sistema.

### Por que Ator e Admin precisam ser processos separados

Um serviço de fundo **não consegue** desenhar ícone de bandeja — não é limitação de framework, é restrição do SO: no Windows, serviços rodam isolados na Session 0 desde o Vista, sem acesso à sessão gráfica; no Linux, um serviço systemd fora de sessão gráfica não tem desktop pra desenhar nada. Por isso o modelo final é: **ator** (serviço, sem GUI) + **tray app** (processo de usuário, com GUI), conversando entre si por IPC local.

## 3. Como Ator e Admin (tray) se comunicam

- **Named Pipes** (Windows) / **Unix Domain Socket** (Linux) — o Kestrel do .NET 8+ suporta os dois de forma unificada (`ListenNamedPipe` / `ListenUnixSocket`), sem código condicional por SO no domínio da aplicação.
- Essa escolha é mais restrita que TCP em `127.0.0.1`: named pipe/socket local pode ter permissão de arquivo travada ao usuário do SO, resolvendo o cenário de "outra conta de usuário na mesma máquina" sem depender de bind de porta nenhum.
- **`localhost`/TCP sozinho não seria suficiente** pra esse cenário específico (multiusuário na mesma máquina física) — é a única situação identificada no debate onde IPC nativo supera bind em loopback.

## 4. Escopo do que o Warden executa

- **Sem Docker Engine API** — Docker não é mais o núcleo do projeto. Interação com containers via CLI (`docker compose ps / logs -f / stop / up`), quando o usuário indica os arquivos.
- **Descoberta de scripts** a partir de convenções existentes: `package.json`, `composer.json`, Justfile, entre outros.
- **Scripts complexos/sem projeto**: configurados manualmente pelo usuário em `~/.warden`, definidos em `.toml` — essa parte é função do **Admin**, não da plateia.
- **Usuário escolhe o diretório** explicitamente — sem indexação automática da máquina.

## 5. Decisões de segurança

- **Bind restrito por superfície**: plateia só na interface Tailscale; admin só via IPC local. Nenhuma delas expõe porta em `0.0.0.0`.
- **Vazamento do link da plateia não é o vetor de risco** — o bind restrito à interface Tailscale já torna a URL inofensiva fora da rede. O segredo real é participar do tailnet, não conhecer a URL.
- **Compartilhar o tailnet é decisão do usuário final**, fora da responsabilidade do Warden — documentar isso explicitamente no README.
- **Sem sistema de auth multiusuário no roadmap** — desnecessário pro escopo de uso pessoal.
- **Ações de confiança (aprovar pasta, criar script novo) devem viver só no Admin, nunca na plateia** — evita recriar "execução arbitrária remota" disfarçada de tela de configuração.

## 6. Pendências de engenharia identificadas

Independentes da linguagem, valem ser resolvidas antes ou durante a estruturação inicial:

1. **Assert de bind na inicialização**: o ator deve recusar subir se não detectar bind correto na interface do Tailscale (evita erro futuro tipo `0.0.0.0` acidental ou uso indevido de Funnel).
2. **Confiança de pasta não pode ser permanente e cega**: guardar hash do conteúdo do arquivo de scripts no momento da aprovação. Se o hash mudar depois (`git pull`, edição, `postinstall` de dependência), a próxima execução exige revisão antes de liberar o botão de novo.
3. **"Mostrar comando antes de rodar" precisa ser atômico**: o que é exibido na tela e o que é executado no clique devem vir da mesma leitura de arquivo — evita uma janela de tempo (TOCTOU) entre listar botões e executar.
4. **Prompt de confiança por pasta**: ao adicionar diretório novo, perguntar explicitamente antes de expor qualquer script como executável.
5. **Gerenciamento de processo filho do CLI do Docker**: matar o processo filho (ex: `docker compose logs -f`) quando a conexão da plateia cair, evitando processos órfãos.

## 7. Stack técnica

- **Linguagem escolhida**: C#/.NET.
- Com Docker Engine API fora do escopo e sem uso de `tsnet` embutido (rede depende do cliente Tailscale já instalado), os dois maiores argumentos técnicos que favoreciam Go (SDK oficial de Docker, embutir identidade Tailscale no binário) deixaram de se aplicar ao projeto. **C# é, hoje, uma escolha tecnicamente sólida pra esse escopo — não apenas uma escolha de carreira.**
- **Tauri e frameworks equivalentes (Electron, Wails) não se aplicam ao Ator** — ele não tem GUI local, é sempre acessado de fora (pela plateia). Esses frameworks resolvem "janela nativa leve", problema que o Ator não tem.
- **Para o Admin/tray, o candidato correto em C# é Avalonia** (cross-platform, Windows + Linux) — WPF/WinForms não servem, são Windows-only. Photino.NET é o equivalente C# do conceito Tauri (webview + backend), caso se prefira essa abordagem em vez de UI nativa Avalonia.
- **Ressalva sobre bandeja no Linux**: não é garantida mesmo com biblioteca madura — GNOME removeu suporte nativo a `StatusNotifierItem`, exigindo extensão do usuário. Funciona de forma confiável no Windows; no Linux, depende da desktop environment. Documentar essa diferença, não prometer paridade.
- Outras peças discutidas: ASP.NET Core Minimal API + Kestrel, SignalR, Generic Host (Windows Service/systemd), SQLite, `Tomlyn` (parsing de `.toml`), Web Push + `WebPush` (NuGet) para notificação com aba fechada.
- **Abstração para troca futura de transporte**: como o plano é usar Tailscale hoje e possivelmente construir um sistema próprio de conexão multi-aparelho depois, isolar isso atrás de uma interface (ex: `IPlateiaTransport`) desde já, sem espalhar chamadas específicas de Tailscale pelo código de domínio. A implementação de hoje é `TailscaleTransport`; football futuro seria trocar por uma implementação própria sem tocar em execução de script, notificação ou histórico.
- **Ponto em aberto, sem decisão fechada**: Blazor Server tem fraqueza real em rede instável (celular trocando wifi/4G, app em background), porque o estado vive preso ao circuito SignalR do servidor. Uma UI com reconexão do lado do cliente tolera melhor esse cenário — vale decidir antes de investir na camada de frontend da plateia.

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

Ao longo da conversa, boa parte do valor "revisado pra baixo" (Docker como diferencial, categoria nova, "inferno" sem o Warden) foi corrigido pra um tamanho mais realista — e o que sobrou depois dessa poda é, na minha avaliação, um projeto que vale a pena construir: não porque resolve algo impossível, mas porque resolve fricção real o suficiente pra justificar o esforço, com escopo de segurança bem definido (Tailscale + separação ator/admin) e uma escolha de stack (C#) que hoje é defensável tecnicamente, não só emocionalmente.