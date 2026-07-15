# Travelex Bank (TIHUM) — Discovery

Status: descoberta e SDK concluídos (2026-07-15). Sonda §0.8 **verde** — sem bloqueio.
**Achado que reescreve o diagnóstico anterior**: o "403 — provável allowlist de IP" registrado no
PROVIDER-MAP (teste de 07-04) era na verdade o **WAF rejeitando requisições sem User-Agent** — com
`User-Agent: CambioReal/1.0` (paridade com o legado), auth e leituras respondem normalmente.
Provider order position: **7 of 9**.
Verified: 2026-07-15, contra `pass cambio-real-v2/providers/travelex/homologation-*` na
homologação viva (`hml-api-corebanking-external.tihum.com`) + legado `cerebro` (read-only).

## 1. Perfil no Provider Protocol

**`Sync`** — PIX payin no formato **cob BACEN** (PUT `v1/cob/{txId}` com
calendario/valor/devedor) + payout PIX por chave (única modalidade — o legado rejeita payout por
conta explicitamente) + saldo + webhooks payin/payout.

## 2. Ambiente, mTLS e auth

| | Homologação | Produção |
|---|---|---|
| Base URL | `https://hml-api-corebanking-external.tihum.com/` | `https://api-corebanking.travelexbank.com.br/` |
| mTLS | Obrigatório — PEMs em `pass .../travelex/homologation-client-{cert,key}` (cert válido até **2034**) | material próprio |
| Credenciais | `pass .../travelex/homologation-env` (USERNAME/PASSWORD/BRANCH_NUMBER/ACCOUNT_NUMBER/PIX_KEY) | não aprovisionadas |

- Auth: `POST /auth` com **Basic** (user:pass) sobre mTLS → resposta é o **JWT cru no corpo**
  (não JSON estruturado); TTL não estruturado — legado assume 3600s (cache 3600−60), SDK mantém
  (configurável). Bearer nos recursos; 1 retry em 401.
- **User-Agent obrigatório na prática** (WAF) — o SDK envia `CambioReal/1.0` por default.
- `agencia`/`conta` como query params na maioria das chamadas (injetados das options).
- Erros: RFC 7807-style (`{type,title,status,detail,instance,properties}` — confirmado ao vivo)
  ou `{message}`; statuses UPPER pt-BR.

## 3. Matriz de cobertura

| # | Endpoint | Recurso SDK | Efeito | Cleanup | Status homolog |
|---|---|---|---|---|---|
| 1 | `POST /auth` | `TravelexTokenProvider` | read/auth | n/a | ✅ vivo (JWT cru; WAF exige UA) |
| 2 | `GET v1/saldo` | `GetBalanceAsync` | read | n/a | ✅ vivo (saldo real, formato "R$ ...") |
| 3 | `PUT v1/cob/{txId}` | `PutCobAsync` | non-financial-write (QR não pago; expira via calendario) | expira sozinho | 🟡 elegível a E2E com expiração curta (não executado por default) |
| 4 | `GET v1/cob/{txId}` | `GetCobAsync` | read (verdade do payin: `CONCLUIDA` = pago) | n/a | ✅ vivo (404 de domínio p/ fictício) |
| 5 | `POST v1/cob/{e2e}/devolucao/{ag}/{conta}` | `CreateDevolucaoAsync` | **financial-write** (refund; codigoMotivoDevolucao MD06) | n/a | 🔴 contrato-only (§0.5) |
| 6 | `POST v1/payout/{txId}` | `CreatePayoutAsync` | **financial-write** (payout por chave) | n/a | 🔴 contrato-only (§0.5) |
| 7 | `GET v1/payout/{txId}` | `GetPayoutAsync` | read (statuses APROVADO/CRIADO/ABERTO/PARCIAL/REJEITADO/TIMEOUT/CANCELADO/NAO_FINALIZADO/ERRO/SUPER_LOTADO/REJEICAO_COMPLIANCE) | n/a | ⚪ unit (mesma família de #4) |
| 8 | `POST v1/webhook*` payin/payout + DELETE | fora do SDK v1 (paths a confirmar — AccountService::create*Webhook) | non-financial-write com cleanup | DELETE existe | ⚪ decisão: incremento futuro |

## 4. Decisões e lacunas

1. Webhooks: inscrição existe no legado mas o consumo é re-poll — fora do SDK/gateway v1
   (mesma decisão dos demais; paths de inscrição a confirmar antes de expor).
2. Payout por conta bancária NÃO existe (confirmado no legado) — o gateway não o oferece.
3. `saldo` vem como string "R$ ..." — repassado cru (parse é política do consumidor).
4. Statuses UPPER pt-BR com conjunto aberto ⇒ strings + constantes.

## 5. Limites de responsabilidade

SDK = API nativa (mTLS + Basic→JWT + UA encapsulados); gateway = `/v1/travelex/*` canônico com
devolução/payout documentados FINANCIAL-WRITE; plataforma = polling e orquestração.

## 6. Nenhuma contradição arquitetural

Padrão canônico Sync + SDK/gateway standalone.
