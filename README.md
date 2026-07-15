# travelex-sdk

Cliente .NET tipado (`CambioReal.Travelex.Client`) para a **Travelex Bank (TIHUM) Corebanking
API** — PIX payin no formato cob BACEN (`PUT v1/cob/{txId}`), payout por chave, devolução, saldo.
Particularidades encapsuladas: **mTLS** (PEM→PKCS#12 em memória), auth **Basic→JWT cru no corpo**
(TTL 3600s herdado do legado) e **User-Agent obrigatório** (`CambioReal/1.0` — o WAF responde 403
sem UA; era o falso "403 de allowlist" do diagnóstico de 07-04).

Validação viva (2026-07-15, homolog): auth JWT ok, `v1/saldo` com saldo real, `v1/cob/{fictício}`
404 de domínio RFC 7807. 8 unit + 3 sandbox verdes. Payout/devolução = **financial-write**, nunca
executados (goal §0.5). Cert mTLS válido até 2034.

Secrets: `pass cambio-real-v2/providers/travelex/homologation-{env,client-cert,client-key}`.
Discovery: `docs/providers/travelex/discovery.md`.
