# Contexto compartilhado de IA

## Objetivo atual

Concluir os hardenings P0/P1 de segurança e confiabilidade já planejados e
validá-los primeiro em ambientes descartáveis e depois na infraestrutura ativa.

## Estado atual

- Arquitetura ativa: React/Vite na Vercel, rewrite `/api/*` para API ASP.NET
  Core na Railway e PostgreSQL no Neon. Brevo envia e-mails transacionais.
- Dados financeiros são isolados por `UserId`; autenticação usa JWT em cookie
  `HttpOnly`, CSRF para mutações, rate limit e auditoria de eventos sensíveis.
- A área autenticada exibe erro e permite repetir a leitura quando o histórico ou os
  compromissos necessários à tela estão indisponíveis. Informar a senha atual incorreta
  no perfil preserva a sessão.
- Saldos realizados, metas e histórico da previsão consideram lançamentos até o dia
  de referência. Parcelas futuras continuam disponíveis em Transações e Planejamento.
- A importação compara a data civil e a conta de destino na prévia. A migration
  `TrackTransactionImportBatches` registra uma assinatura do lote por usuário junto à
  gravação dos lançamentos. Reenvios do mesmo lote retornam zero itens importados.
- A migration `AnchorRecurringOccurrences` preserva lançamentos antigos e associa a
  conta à regra quando o histórico permite identificá-la. Novas ocorrências têm
  chave mensal única e seguem o dia de início da série.
- O processamento de notificações financeiras exige habilitação explícita.
- O cliente HTTP tem prazo total de 30 segundos, retry único apenas para
  leituras após rede/`502`/`503`/`504` e nenhum reenvio automático de escrita.
  Falha após uma mutação informa resultado incerto.
- Senhas atuais e alteradas desde a migration `AddPasswordHistory` não podem
  ser reutilizadas no perfil ou recuperação. O token de recuperação continua
  válido após rejeição de senha reutilizada.
- `/health` e `/health/live` são liveness; `/health/ready` verifica conexão e
  migrations, com resposta genérica e prazo de cinco segundos.
- O dashboard público expõe no máximo 100 lançamentos dos últimos 12 meses.
  Cada item contém somente mês, categoria, valor e tipo; descrição, contas,
  tags, identificadores e recorrência não são retornados. A interface informa o
  recorte e não oferece histórico completo.
- Os workflows `Validate` e `Post-deploy smoke` estabelecem checks de frontend,
  API, auditorias e um drill descartável de `pg_dump`/`pg_restore`. Ainda é
  necessário configurar a proteção de `main`, alertas Railway/Vercel/Neon e
  executar o primeiro drill completo em uma recuperação Neon isolada.
- A documentação foi conciliada com o código: Data Protection já persiste no
  `AppDbContext`; importação tem limite de 500 itens e teste de 501, enquanto o
  limite do tamanho total do payload permanece pendente. A Etapa 0 de identidade
  foi concluída e a trilha Azure é somente histórica.
- Antes da Etapa 7, o roadmap prevê pareamento seguro Telegram/WhatsApp iniciado
  por sessão autenticada, com código de uso único, vínculo ao `UserId`, webhook
  validado, deduplicação e revogação. Escritas exigem confirmação e comandos da
  aplicação existentes; resumo semanal de domingo exige worker/cron idempotente.
  Isso é planejamento, sem integração de canal, agente ou modelo implementada.

## Documentação e revisão atual

- `README.md` e `README-pt-BR.md` foram reescritos com conteúdo equivalente:
  proposta do produto, recursos disponíveis, arquitetura, execução local com
  PostgreSQL, testes e próximos passos. Os previews antigos foram removidos dos
  READMEs; os arquivos de mídia foram preservados.
- O setup explica que o exemplo local ainda usa SQL Server e deve ser adaptado
  para PostgreSQL; a factory do EF lê `ConnectionStrings__Default` do ambiente.
- O hardening de proxies já está commitado (`793662a`), conforme Git. A faixa
  `100.0.0.0/8` permanece provisória; configuração validada e testes locais não
  encerram a verificação da cadeia Vercel/Railway em produção.

## Decisões vigentes

- `Program.cs` é o ponto de composição de banco, autenticação, CORS, CSRF,
  rate limit, e-mail, health checks e configurações de proxy.
- A conexão de runtime Neon não recebe DDL. Migrations usam conexão administrativa
  separada e `Database__ApplyMigrationsOnStartup=true` apenas no deploy controlado.
- `/api` é o contrato de produção. `VITE_API_URL` é opcional, termina em `/api`
  e nunca contém segredo.
- A API só confia em headers encaminhados de peers configurados. Não habilitar
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, pois a aplicação rejeita esse modo.

## Verificação local em 25/11/2026

- Passaram 154 testes da API, 160 do cliente e 13 cenários Playwright em Chromium.
  Lint, build Vite e auditorias npm/NuGet passaram, incluindo dependências transitivas.
- No PostgreSQL 16 descartável, as seis migrations foram aplicadas e a restauração
  recuperou o histórico de migrations e um lançamento vinculado ao usuário de teste.
  Leituras concorrentes de recorrências e reenvios simultâneos de importação passaram
  também no banco restaurado. Uma base legada com contas ambíguas e lançamentos
  duplicados confirmou a preservação do histórico durante a atualização.
- O workflow pós-deploy consulta `/api/auth/csrf-token` pela origem do frontend.
  No ensaio HTTP local, o check rejeitou HTML, token vazio, resposta sem corpo e
  falha do upstream. O aceite na cadeia pública Vercel/Railway segue pendente.

## Pendências conhecidas

O CI do frontend usa Ubuntu 24.04 para instalar o Chromium compatível com o
Playwright do lockfile. O job de restauração executa seu próprio `dotnet restore`
antes de migrar o PostgreSQL descartável. Cada job recebe um checkout limpo.

### Confirmadas e bloqueantes para encerrar os hardenings

- Validar os fluxos de histórico de senhas em PostgreSQL e em produção, cobrindo
  perfil, recuperação e reenvio do mesmo token. A aplicação local da migration passou.
- Validar `/health/ready` com banco atualizado, migration pendente e banco
  indisponível em uma instância PostgreSQL descartável; repetir smoke na Railway.
- Confirmar a topologia e o IP efetivo pela Vercel e Railway. Só reduzir
  `100.0.0.0/8` com evidência da plataforma; testar headers forjados no endpoint
  público sem criar endpoint de diagnóstico permanente.
- Medir cold start real da Railway/Neon e revisar o prazo de 30 segundos se os
  dados exigirem; validar os estados visuais nos fluxos principais.

### Melhorias futuras que não bloqueiam o trabalho atual

- Paginação completa do histórico ficou para uma etapa separada por decisão do
  responsável. O contrato atual entrega a coleção usada por filtros, exportação,
  reconciliação de importações e cálculos dos painéis.
- Domínio final, cookies/CORS/callbacks e autenticação Brevo com SPF/DKIM/DMARC.
- Webhook Brevo assinado, deduplicado e com retenção definida antes de ativar
  notificações financeiras.
- Locks concorrentes de conta demo/notificações contra Neon com duas instâncias.
- Exercitar a recuperação em uma instância Neon isolada e validar o monitoramento ativo.

## Próximo passo sugerido

No próximo deploy controlado, seguir o runbook para aplicar as migrations e
executar o smoke pela origem pública do frontend. A validação da cadeia de proxies
e o ensaio de recuperação em Neon continuam pendentes.

## Manutenção deste arquivo

Atualize-o antes de encerrar uma tarefa relevante. Mantenha somente decisões,
estado, evidências e pendências úteis agora; remova histórico de conversa e
itens resolvidos. Informação que se tornar estrutural deve ser consolidada em
`AGENTS.md` ou `architecture-decisions.md`.
