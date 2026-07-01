---
name: keep-context-todo-updated
description: >
  Use this skill during any Warden project conversation where architecture,
  scope, status, or a design decision changes or gets discussed — not just
  when the user explicitly asks to update docs. Trigger on: decisões
  fechadas ou revertidas, escopo mudou, novo componente/adapter decidido,
  algo saiu do "em aberto" pra "fechado" (ou vice-versa), MVP mudou, stack
  técnica mudou, nome/estrutura de pasta mudou, "vamos fazer diferente",
  "decidi que", "muda pra", "esquece aquilo", "adiciona X ao escopo". Also
  trigger at the natural end of any substantive design/architecture
  discussion, even without an explicit doc-update request. When active,
  check whether CONTEXT.md and/or TODO.md at the Warden project root are
  now stale, and if so propose the concrete diff — don't just remind
  yourself silently and don't auto-edit without confirmation.
---

# Keep CONTEXT.md / TODO.md Updated

Warden trata `TODO.md` como fonte da verdade de decisões/arquitetura e
`CONTEXT.md` como visão + design consolidado. Os dois divergem rápido da
realidade se decisão tomada em conversa não é escrita — próxima sessão
(ou próximo humano) lê os arquivos e acha coisa desatualizada ou perde
decisão inteira. Esta skill existe pra fechar esse gap no momento em que
a decisão acontece, não depois.

## Quando checar

Depois de qualquer trecho de conversa onde:

- Uma decisão de arquitetura foi tomada, mudada ou revertida.
- Escopo do projeto (core, MVP, "fora do core") mudou.
- Item saiu de "em aberto" pra "fechado" ou o contrário.
- Stack técnica, estrutura de pastas, ou nome de algo mudou.
- Um novo adapter/componente/fluxo foi definido.
- O usuário concluiu uma discussão de design com uma direção clara.

Não precisa esperar o usuário pedir "atualiza o TODO" — esse é o ponto:
lembrar sozinho.

## O que fazer

1. Releia mentalmente o que mudou na conversa vs. o que `CONTEXT.md` e
   `TODO.md` dizem hoje (leia os arquivos se não estiverem já no contexto).
2. Decida qual arquivo é afetado:
   - **TODO.md** → decisão nova/mudada, item de "decisões fechadas",
     item de "em aberto", mudança de stack.
   - **CONTEXT.md** → mudança de visão, fluxo, modelo de dados, diagrama,
     ou resumo de decisões (a seção "Decisões fechadas (resumo do
     TODO.md)" existe justamente pra espelhar o TODO — se o TODO muda,
     essa seção do CONTEXT também precisa mudar).
   - Muitas vezes é os dois — TODO é a fonte, CONTEXT resume/expande.
3. Proponha o edit concreto (texto exato a mudar/adicionar), não um
   lembrete vago tipo "acho que devia atualizar os docs".
4. Pergunte confirmação antes de aplicar, a menos que o usuário já tenha
   pedido explicitamente pra atualizar os arquivos nesta mensagem.

## Formato da proposta

```
Docs desatualizados: [TODO.md / CONTEXT.md / ambos]

**TODO.md** — seção "[nome da seção]":
[diff ou texto novo proposto]

**CONTEXT.md** — seção "[nome da seção]":
[diff ou texto novo proposto]

Aplico?
```

Se só um arquivo for afetado, omite o outro — não force menção aos dois
toda vez.

## Fora do escopo

- Não dispara pra perguntas puramente exploratórias sem decisão tomada
  ("e se a gente fizesse X?" sem resolução não é decisão — só quando
  vira "vamos fazer X").
- Não edita `mainidea.png` / `fragmentando.png` (são rascunhos visuais,
  fora do alcance de texto).
- Não reformata os arquivos inteiros — só toca a seção afetada pela
  mudança discutida.
