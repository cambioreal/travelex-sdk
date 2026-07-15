# CambioReal.Travelex.Client.SandboxTests

Integração homolog real (mTLS), opt-in — fora de `Travelex.slnx`, nunca em CI.
Variáveis: ver topo de `TravelexSandboxTests.cs` (origem `pass cambio-real-v2/providers/travelex/*`).

## Última execução ao vivo — 2026-07-15
```
Passed AuthenticatesLiveOverMtlsWithRawJwtBody — POST /auth (Basic+mTLS): ok, JWT cru.
Passed BalanceReadsLive — GET v1/saldo: 200, saldo real presente.
Passed CobByFictitiousTxIdReturnsDomain404 — 404 de domínio (problem-detail).
Passed: 3, Failed: 0
```
Achado registrado: WAF exige User-Agent (403 sem ele) — o SDK envia `CambioReal/1.0` por default.
