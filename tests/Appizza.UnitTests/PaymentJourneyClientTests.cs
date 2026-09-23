using System.Net;
using System.Text;
using Appizza.Table.Core;

namespace Appizza.UnitTests;

public sealed class PaymentJourneyClientTests
{
    [Fact]
    public void LogicalActionReusesKeyAndTerminalStartsNewAction()
    {
        var state = new PaymentAttemptActionState();
        var first = state.BeginOrReuse();
        Assert.Equal(first, state.BeginOrReuse());
        state.CompleteIfTerminal("Approved");
        var second = state.BeginOrReuse();
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Processing")]
    [InlineData("Unknown")]
    [InlineData("AwaitingCustomerAction")]
    public void NonTerminalPreservesCurrentAction(string status)
    {
        var state = new PaymentAttemptActionState();
        var key = state.BeginOrReuse();
        state.CompleteIfTerminal(status);
        Assert.Equal(key, state.CurrentKey);
    }

    [Fact]
    public void DeclinedCompletesCurrentAction()
    {
        var state = new PaymentAttemptActionState();
        state.BeginOrReuse();
        state.CompleteIfTerminal("Declined");
        Assert.Null(state.CurrentKey);
    }
    [Fact]
    public async Task LoadsAuthoritativeBalanceAndPlan()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, "{\"status\":\"Processing\",\"totalAmount\":42.50,\"allocations\":[{\"allocationId\":\"11111111-1111-1111-1111-111111111111\"}] ,\"planId\":\"22222222-2222-2222-2222-222222222222\"}"));
        var client = new PaymentJourneyClient(new HttpClient(handler) { BaseAddress = new Uri("https://table.test/") });
        var balance = await client.LoadBalanceAsync();
        Assert.Equal("Processing", balance.GetProperty("status").GetString());
        var plan = await client.CreatePlanAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal("22222222-2222-2222-2222-222222222222", plan.GetProperty("planId").GetGuid().ToString());
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
    }

    [Fact]
    public async Task AttemptUsesAndReusesStableIdempotencyKeyOnRetry()
    {
        var key = Guid.NewGuid();
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, "{\"status\":\"Approved\",\"amount\":1.00}"));
        var client = new PaymentJourneyClient(new HttpClient(handler) { BaseAddress = new Uri("https://table.test/") });
        await client.CreateAttemptAsync(Guid.NewGuid(), Guid.NewGuid(), [], "pix", key);
        await client.CreateAttemptAsync(Guid.NewGuid(), Guid.NewGuid(), [], "pix", key);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(key.ToString(), r.Headers.GetValues("Idempotency-Key").Single()));
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task RefreshOnlyGetsAndApiErrorDoesNotRetryMutation()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.BadRequest, "{\"errorCode\":\"conflict\"}"));
        var client = new PaymentJourneyClient(new HttpClient(handler) { BaseAddress = new Uri("https://table.test/") });
        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreatePlanAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Declined")]
    public async Task PreservesAuthoritativeTerminalStatus(string status)
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, $"{{\"status\":\"{status}\"}}"));
        var result = await new PaymentJourneyClient(new HttpClient(handler) { BaseAddress = new Uri("https://table.test/") }).LoadBalanceAsync();
        Assert.Equal(status, result.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Processing")]
    [InlineData("Unknown")]
    [InlineData("AwaitingCustomerAction")]
    public async Task NonTerminalStatusDoesNotCreateAttemptAutomatically(string status)
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, $"{{\"status\":\"{status}\"}}"));
        var client = new PaymentJourneyClient(new HttpClient(handler) { BaseAddress = new Uri("https://table.test/") });
        var result = await client.LoadBalanceAsync();
        Assert.Equal(status, result.GetProperty("status").GetString());
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/attempts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReloadDoesNotCreateAnotherAttempt()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
            ? (HttpStatusCode.OK, "{\"status\":\"Pending\"}")
            : (HttpStatusCode.OK, "{\"status\":\"Pending\"}"));
        var client = new PaymentJourneyClient(new HttpClient(handler) { BaseAddress = new Uri("https://table.test/") });
        await client.CreateAttemptAsync(Guid.NewGuid(), Guid.NewGuid(), [], "pix", Guid.NewGuid());
        var before = handler.Requests.Count(r => r.Method == HttpMethod.Post);
        await client.LoadBalanceAsync();
        var after = handler.Requests.Count(r => r.Method == HttpMethod.Post);
        Assert.Equal(before, after);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var result = responder(request);
            return new HttpResponseMessage(result.Status) { Content = new StringContent(result.Body, Encoding.UTF8, "application/json") };
        }
    }
}
