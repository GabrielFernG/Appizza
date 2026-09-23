using System.Text.Json;
using Appizza.Table.Core;

namespace Appizza.Table;

public sealed class PaymentPage : ContentPage
{
    private readonly Label _status = new() { FontAttributes = FontAttributes.Bold };
    private readonly VerticalStackLayout _content = new() { Padding = 24, Spacing = 12 };
    private JsonElement? _plan;
    private readonly PaymentAttemptActionState _attemptAction = new();
    private static PaymentJourneyClient Client => new(TableRuntime.Http);
    public PaymentPage()
    {
        Title = "Pagamento";
        _content.Add(_status);
        var refresh = new Button { Text = "Atualizar pagamento" }; refresh.Clicked += async (_, _) => await RefreshAsync(); _content.Add(refresh);
        var plan = new Button { Text = "Criar plano total" }; plan.Clicked += async (_, _) => await CreatePlanAsync(); _content.Add(plan);
        var create = new Button { Text = "Criar tentativa Pix" }; create.Clicked += async (_, _) => await CreateAttemptAsync(); _content.Add(create);
        Content = new ScrollView { Content = _content };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync() { try { var root = await Client.LoadBalanceAsync(); _status.Text = $"Sessão: {root.GetProperty("status").GetString()} | Total: {root.GetProperty("totalAmount").GetDecimal():C} | Pago: {root.GetProperty("paidAmount").GetDecimal():C} | Reservado: {root.GetProperty("reservedAmount").GetDecimal():C}"; } catch (Exception ex) { _status.Text = ex.Message; } }
    private async Task CreatePlanAsync() { try { if (TableRuntime.Context is not { SessionId: Guid sessionId }) throw new InvalidOperationException("Sessão ativa obrigatória."); _plan = await Client.CreatePlanAsync(sessionId, TableRuntime.PaymentPlanIdempotencyKey); _status.Text = "Plano autoritativo carregado."; } catch (Exception ex) { _status.Text = ex.Message; } }
    private async Task CreateAttemptAsync() { try { if (TableRuntime.Context is not { SessionId: Guid sessionId } || _plan is not JsonElement plan) throw new InvalidOperationException("Plano de pagamento indisponível."); var key = _attemptAction.BeginOrReuse(); var allocationIds = plan.GetProperty("allocations").EnumerateArray().Select(x => x.GetProperty("allocationId").GetGuid()); var result = await Client.CreateAttemptAsync(sessionId, plan.GetProperty("planId").GetGuid(), allocationIds, "pix", key); var status = result.GetProperty("status").GetString(); _status.Text = $"Tentativa: {status} | Valor: {result.GetProperty("amount").GetDecimal():C}"; _attemptAction.CompleteIfTerminal(status); } catch (Exception ex) { _status.Text = ex.Message; } }
}
