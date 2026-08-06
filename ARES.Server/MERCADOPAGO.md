# Mercado Pago en ARES

Configurar en Render, sin incluir los valores en GitHub:

- `MERCADOPAGO_ACCESS_TOKEN`: Access Token de la aplicación de Mercado Pago.
- `MERCADOPAGO_WEBHOOK_SECRET`: firma secreta de Webhooks.
- `ARES_USD_ARS_RATE`: cotización utilizada para convertir los precios comerciales en USD a pesos argentinos; usar punto decimal, por ejemplo `1450.00`.

URL pública de notificaciones:

`https://ares-3bic.onrender.com/api/billing/mercadopago/webhook?source_news=webhooks`

Eventos de suscripciones que utiliza ARES:

- `subscription_preapproval`
- `subscription_authorized_payment`

ARES no activa una licencia por la redirección del navegador. La activa únicamente después de recibir un webhook válido y comprobar el pago nuevamente contra la API de Mercado Pago.
