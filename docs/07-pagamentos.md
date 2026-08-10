# 07 — Pagamentos

Pagamento ocorre no fechamento.

Divisões MVP:
- total;
- participantes;
- itens;
- valor;
- igualitária.

Métodos MVP:
- Pix;
- crédito;
- débito;
- dinheiro;
- SoftPOS/NFC;
- terminal externo/manual.

Tentativas têm reserva financeira e estados:
Created, AwaitingCustomerAction, Processing, Approved, Declined, Expired, Cancelled, Unknown.

Unknown bloqueia nova cobrança equivalente até reconciliação.

Pix depende de confirmação do provedor.
Dinheiro depende de funcionário.
Estorno não apaga pagamento.

SoftPOS usa abstração de provedor e depende de hardware/provedor compatível.
