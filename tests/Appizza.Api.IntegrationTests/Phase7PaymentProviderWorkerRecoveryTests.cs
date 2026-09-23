using Appizza.Modules.Payments;
using Appizza.Payments.Application;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Appizza.Modules.Tables;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7PaymentProviderWorkerRecoveryTests(Phase1ApiFixture fixture)
{
    [Fact] public async Task WorkerRecoversPersistedIntentBeforeProviderCallUsingSameKey()
    {
        var x = await CreateAttemptAsync();
        var key = $"attempt:{x.AttemptId:N}";
        var executionId = await CreateExecutionAsync(x, PaymentProviderExecutionStatus.Pending, key);
        var provider = new ControllablePaymentProvider("processing");
        await using var db = fixture.CreateDbContext();
        var recovery = new PaymentProviderRecoveryService(db, provider, new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)));
        await recovery.ProcessOneAsync(x.Tenant, x.AttemptId);
        await using var read = fixture.CreateDbContext();
        var execution = await read.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e => e.Id == executionId);
        Assert.Equal(executionId, execution.Id);
        Assert.Equal(key, execution.ProviderIdempotencyKey);
        Assert.Equal(1, provider.StartPaymentInvocationCount);
        Assert.Equal(key, provider.ObservedProviderIdempotencyKeys.Single());
        Assert.Equal(1, await read.Set<PaymentProviderExecution>().CountAsync(e => e.PaymentAttemptId == x.AttemptId));
    }

    [Fact] public async Task SafeRetryReusesSameExecutionAndSameProviderKey()
    {
        var x = await CreateAttemptAsync();
        var key = $"attempt:{x.AttemptId:N}";
        var executionId = await CreateExecutionAsync(x, PaymentProviderExecutionStatus.Pending, key);
        var provider = new ControllablePaymentProvider("processing") { StartFailureClassification = PaymentRecoveryClassification.SafeToRetry, FailOnlyFirstStart = true };
        await using var db = fixture.CreateDbContext();
        var service = new PaymentProviderRecoveryService(db, provider, new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)));
        await service.ProcessOneAsync(x.Tenant, x.AttemptId);
        await using var secondDb = fixture.CreateDbContext();
        var secondService = new PaymentProviderRecoveryService(secondDb, provider, new PaymentProcessingService(secondDb, provider, new PaymentAttemptLifecycleService(secondDb)));
        var retryState = await secondDb.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        Assert.Equal(PaymentProviderExecutionStatus.Pending, retryState.Status);
        Assert.Null(retryState.ClaimedBy);
        await secondService.ProcessOneAsync(x.Tenant, x.AttemptId);
        await using var read = fixture.CreateDbContext();
        var execution = await read.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        Assert.Equal(executionId, execution.Id);
        Assert.Equal(key, execution.ProviderIdempotencyKey);
        Assert.Equal(2, provider.StartPaymentInvocationCount);
        Assert.All(provider.ObservedProviderIdempotencyKeys, observed => Assert.Equal(key, observed));
    }

    [Fact] public async Task AmbiguousProviderFailureTransitionsToUnknownWithoutBlindRetry()
    {
        var x = await CreateAttemptAsync();
        var key = $"attempt:{x.AttemptId:N}";
        var executionId = await CreateExecutionAsync(x, PaymentProviderExecutionStatus.Pending, key);
        var provider = new ControllablePaymentProvider("unknown", lookupStatus: "unknown") { StartFailureClassification = PaymentRecoveryClassification.AmbiguousRequiresReconciliation };
        await using (var beforeDb = fixture.CreateDbContext())
        {
            var beforeExecution = await beforeDb.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e => e.Id == executionId);
            var beforeAttempt = await beforeDb.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a => a.Id == x.AttemptId);
            Assert.Equal(PaymentProviderExecutionStatus.Pending, beforeExecution.Status);
            Assert.Equal(PaymentAttemptStatus.Created, beforeAttempt.Status);
            Assert.Equal(key, beforeExecution.ProviderIdempotencyKey);
            Assert.Null(beforeExecution.ClaimedBy);
        }
        await using var db = fixture.CreateDbContext();
        var service = new PaymentProviderRecoveryService(db, provider, new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)));
        await service.ProcessOneAsync(x.Tenant, x.AttemptId);
        Assert.True(provider.StartEntered);
        Assert.Equal(x.AttemptId, provider.LastStartAttemptId);
        await using var read = fixture.CreateDbContext();
        var afterAct1 = await read.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        var act1Attempt = await read.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a => a.Id == x.AttemptId);
        var act1Session = await read.Set<TableSession>().AsNoTracking().SingleAsync(s => s.Id == act1Attempt.TableSessionId);
        var act1Snapshot = $"ACT1: executionId={afterAct1.Id}; status={afterAct1.Status}; key={afterAct1.ProviderIdempotencyKey}; claimedBy={afterAct1.ClaimedBy}; claimedUntil={afterAct1.ClaimedUntil}; attemptStatus={act1Attempt.Status}; reserved={act1Session.ReservedAmount}; paid={act1Session.PaidAmount}; startCount={provider.StartPaymentInvocationCount}; lookupCount={provider.LookupPaymentInvocationCount}; trace=[{string.Join(" || ", provider.CallTrace)}]";
        Assert.True(provider.StartPaymentInvocationCount == 1, act1Snapshot);
        Assert.Equal(PaymentProviderExecutionStatus.Unknown, afterAct1.Status);
        Assert.Equal(executionId, afterAct1.Id);
        Assert.Equal(key, afterAct1.ProviderIdempotencyKey);
        Assert.Contains("#1 Start attempt=" + x.AttemptId.ToString("N") + " key=" + key + " -> THROW AmbiguousRequiresReconciliation", provider.CallTrace.First());
        await using var preAct2Db = fixture.CreateDbContext();
        var preAct2 = await preAct2Db.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        var preAct2Attempt = await preAct2Db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a => a.Id == x.AttemptId);
        var preAct2Session = await preAct2Db.Set<TableSession>().AsNoTracking().SingleAsync(s => s.Id == preAct2Attempt.TableSessionId);
        var preAct2Snapshot = $"ACT2_PRE: executionId={preAct2.Id}; status={preAct2.Status}; key={preAct2.ProviderIdempotencyKey}; claimedBy={preAct2.ClaimedBy}; claimedUntil={preAct2.ClaimedUntil}; attemptStatus={preAct2Attempt.Status}; reserved={preAct2Session.ReservedAmount}; paid={preAct2Session.PaidAmount}";
        await using var secondDb = fixture.CreateDbContext();
        var secondService = new PaymentProviderRecoveryService(secondDb, provider, new PaymentProcessingService(secondDb, provider, new PaymentAttemptLifecycleService(secondDb)));
        await secondService.ProcessOneAsync(x.Tenant, x.AttemptId);
        await using var postAct2Db = fixture.CreateDbContext();
        var postAct2 = await postAct2Db.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        var postAct2Attempt = await postAct2Db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a => a.Id == x.AttemptId);
        var postAct2Session = await postAct2Db.Set<TableSession>().AsNoTracking().SingleAsync(s => s.Id == postAct2Attempt.TableSessionId);
        var postAct2Snapshot = $"ACT2_POST: executionId={postAct2.Id}; status={postAct2.Status}; key={postAct2.ProviderIdempotencyKey}; claimedBy={postAct2.ClaimedBy}; claimedUntil={postAct2.ClaimedUntil}; attemptStatus={postAct2Attempt.Status}; reserved={postAct2Session.ReservedAmount}; paid={postAct2Session.PaidAmount}; startCount={provider.StartPaymentInvocationCount}; lookupCount={provider.LookupPaymentInvocationCount}; trace=[{string.Join(" || ", provider.CallTrace)}]";
        Assert.True(provider.StartPaymentInvocationCount == 1, $"Expected StartCount = 1\n{act1Snapshot}\n{preAct2Snapshot}\n{postAct2Snapshot}");
        Assert.Equal(1, provider.LookupPaymentInvocationCount);
        Assert.Single(provider.ObservedLookupProviderIdempotencyKeys);
        Assert.Equal(2, provider.CallTrace.Count);
    }

    [Fact] public async Task UnknownReconciliationApprovedSettlesExactlyOnce()
    {
        var x = await CreateAttemptAsync();
        await CreateExecutionAsync(x, PaymentProviderExecutionStatus.Unknown, $"attempt:{x.AttemptId:N}");
        var provider = new ControllablePaymentProvider("unknown", lookupStatus: "approved");
        await using var db = fixture.CreateDbContext();
        var service = new PaymentProviderRecoveryService(db, provider, new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)));
        var before = await FinancialAsync(x);
        var beforeAudit = await SemanticAuditCountAsync(x.AttemptId);
        var beforeOutbox = await SemanticOutboxCountAsync(x.Tenant, x.AttemptId);
        await service.ProcessOneAsync(x.Tenant, x.AttemptId);
        var after = await FinancialAsync(x);
        Assert.Equal(0, provider.StartPaymentInvocationCount);
        Assert.Equal(1, provider.LookupPaymentInvocationCount);
        Assert.Equal(PaymentAttemptStatus.Approved, after.Status);
        Assert.Equal(before.Paid + x.Amount, after.Paid);
        Assert.Equal(before.Reserved - x.Amount, after.Reserved);
        Assert.Equal(beforeAudit + 1, await SemanticAuditCountAsync(x.AttemptId));
        Assert.Equal(beforeOutbox + 1, await SemanticOutboxCountAsync(x.Tenant, x.AttemptId));
        await service.ProcessOneAsync(x.Tenant, x.AttemptId);
        var repeated = await FinancialAsync(x);
        Assert.Equal(after.Paid, repeated.Paid);
        Assert.Equal(after.Reserved, repeated.Reserved);
        Assert.Equal(beforeAudit + 1, await SemanticAuditCountAsync(x.AttemptId));
        Assert.Equal(beforeOutbox + 1, await SemanticOutboxCountAsync(x.Tenant, x.AttemptId));
    }

    [Fact] public async Task UnknownReconciliationDeclinedReleasesReservationExactlyOnce()
    {
        var x = await CreateAttemptAsync();
        await CreateExecutionAsync(x, PaymentProviderExecutionStatus.Unknown, $"attempt:{x.AttemptId:N}");
        var provider = new ControllablePaymentProvider("unknown", lookupStatus: "declined");
        await using var db = fixture.CreateDbContext();
        var service = new PaymentProviderRecoveryService(db, provider, new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)));
        var before = await FinancialAsync(x);
        await service.ProcessOneAsync(x.Tenant, x.AttemptId);
        var after = await FinancialAsync(x);
        Assert.Equal(PaymentAttemptStatus.Declined, after.Status);
        Assert.Equal(before.Paid, after.Paid);
        Assert.Equal(before.Reserved - x.Amount, after.Reserved);
        await service.ProcessOneAsync(x.Tenant, x.AttemptId);
        var repeated = await FinancialAsync(x);
        Assert.Equal(after.Paid, repeated.Paid);
        Assert.Equal(after.Reserved, repeated.Reserved);
    }

    [Fact] public async Task UnknownReconciliationStillUnknownKeepsReservation()
    {
        var x = await CreateAttemptAsync();
        var key = $"attempt:{x.AttemptId:N}";
        await CreateExecutionAsync(x, PaymentProviderExecutionStatus.Unknown, key);
        var provider = new ControllablePaymentProvider("unknown", lookupStatus: "unknown");
        var before = await FinancialAsync(x);
        await using var db = fixture.CreateDbContext();
        var service = new PaymentProviderRecoveryService(db, provider, new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)));
        await service.ProcessOneAsync(x.Tenant, x.AttemptId);
        var after = await FinancialAsync(x);
        Assert.Equal(0, provider.StartPaymentInvocationCount);
        Assert.Equal(before.Reserved, after.Reserved);
        Assert.Equal(before.Paid, after.Paid);
        Assert.Equal(key, (await ReloadExecutionAsync(x)).ProviderIdempotencyKey);
    }
    [Fact] public async Task OutcomeObservedConvergesLifecycleWithoutCallingProviderAgain() { var x=await CreateAttemptAsync(); var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.OutcomeObserved,$"attempt:{x.AttemptId:N}"); await using(var seed=fixture.CreateDbContext()){var e=await seed.Set<PaymentProviderExecution>().SingleAsync(e=>e.Id==id); e.NormalizedOutcome="approved"; e.ProviderReference="provider-ref"; await seed.SaveChangesAsync();} await using(var pre=fixture.CreateDbContext()){var a=await pre.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a=>a.Id==x.AttemptId); var e=await pre.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.NotEqual(PaymentAttemptStatus.Approved,a.Status); Assert.Equal(PaymentProviderExecutionStatus.OutcomeObserved,e.Status);} var before=await FinancialAsync(x); var auditBefore=await SemanticAuditCountAsync(x.AttemptId); var outboxBefore=await SemanticOutboxCountAsync(x.Tenant,x.AttemptId); var p=new ControllablePaymentProvider("unknown"); await using var d=fixture.CreateDbContext(); var s=new PaymentProviderRecoveryService(d,p,new PaymentProcessingService(d,p,new PaymentAttemptLifecycleService(d))); await s.ProcessOneAsync(x.Tenant,x.AttemptId); var after=await FinancialAsync(x); await using(var post=fixture.CreateDbContext()){var e=await post.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(PaymentProviderExecutionStatus.Completed,e.Status); Assert.Null(e.ClaimedBy); Assert.Null(e.ClaimedUntil);} Assert.Equal(0,p.StartPaymentInvocationCount); Assert.Equal(0,p.LookupPaymentInvocationCount); Assert.Equal(PaymentAttemptStatus.Approved,after.Status); Assert.Equal(before.Reserved-x.Amount,after.Reserved); Assert.Equal(before.Paid+x.Amount,after.Paid); Assert.Equal(auditBefore+1,await SemanticAuditCountAsync(x.AttemptId)); Assert.Equal(outboxBefore+1,await SemanticOutboxCountAsync(x.Tenant,x.AttemptId)); await using var d2=fixture.CreateDbContext(); var s2=new PaymentProviderRecoveryService(d2,p,new PaymentProcessingService(d2,p,new PaymentAttemptLifecycleService(d2))); await s2.ProcessOneAsync(x.Tenant,x.AttemptId); var replay=await FinancialAsync(x); await using(var post2=fixture.CreateDbContext()){var e=await post2.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(PaymentProviderExecutionStatus.Completed,e.Status);} Assert.Equal(after,replay); Assert.Equal(0,p.StartPaymentInvocationCount); Assert.Equal(0,p.LookupPaymentInvocationCount); Assert.Equal(auditBefore+1,await SemanticAuditCountAsync(x.AttemptId)); Assert.Equal(outboxBefore+1,await SemanticOutboxCountAsync(x.Tenant,x.AttemptId)); }
    [Fact] public async Task LifecycleAppliedBeforeExecutionCompletionReplaysWithoutDuplicateEffects() { var x=await CreateAttemptAsync(); var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.OutcomeObserved,$"attempt:{x.AttemptId:N}"); await using(var seed=fixture.CreateDbContext()){var e=await seed.Set<PaymentProviderExecution>().SingleAsync(e=>e.Id==id); e.NormalizedOutcome="approved"; await seed.SaveChangesAsync();} var p=new ControllablePaymentProvider("unknown"); await using var d=fixture.CreateDbContext(); var lifecycle=new PaymentAttemptLifecycleService(d); await lifecycle.SucceedAsync(x.Tenant,x.AttemptId); var before=await FinancialAsync(x); var audit=await SemanticAuditCountAsync(x.AttemptId); var outbox=await SemanticOutboxCountAsync(x.Tenant,x.AttemptId); await using(var pre=fixture.CreateDbContext()){var a=await pre.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a=>a.Id==x.AttemptId); var e=await pre.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(PaymentAttemptStatus.Approved,a.Status); Assert.Equal(PaymentProviderExecutionStatus.OutcomeObserved,e.Status); Assert.Equal("approved",e.NormalizedOutcome);} var service=new PaymentProviderRecoveryService(d,p,new PaymentProcessingService(d,p,lifecycle)); await service.ProcessOneAsync(x.Tenant,x.AttemptId); var after=await FinancialAsync(x); await using(var post=fixture.CreateDbContext()){var e=await post.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(PaymentProviderExecutionStatus.Completed,e.Status); Assert.Null(e.ClaimedBy); Assert.Null(e.ClaimedUntil);} Assert.Equal(0,p.StartPaymentInvocationCount); Assert.Equal(0,p.LookupPaymentInvocationCount); Assert.Equal(before,after); Assert.Equal(audit,await SemanticAuditCountAsync(x.AttemptId)); Assert.Equal(outbox,await SemanticOutboxCountAsync(x.Tenant,x.AttemptId)); await using var d2=fixture.CreateDbContext(); var s2=new PaymentProviderRecoveryService(d2,p,new PaymentProcessingService(d2,p,new PaymentAttemptLifecycleService(d2))); await s2.ProcessOneAsync(x.Tenant,x.AttemptId); var replay=await FinancialAsync(x); await using(var post2=fixture.CreateDbContext()){var e=await post2.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(PaymentProviderExecutionStatus.Completed,e.Status);} Assert.Equal(before,replay); Assert.Equal(0,p.StartPaymentInvocationCount); Assert.Equal(0,p.LookupPaymentInvocationCount); Assert.Equal(audit,await SemanticAuditCountAsync(x.AttemptId)); Assert.Equal(outbox,await SemanticOutboxCountAsync(x.Tenant,x.AttemptId)); }
    [Fact] public async Task ConcurrentWorkersClaimSameExecutionOnlyOneCallsProvider() { var x=await CreateAttemptAsync(); var key=$"attempt:{x.AttemptId:N}"; var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.Pending,key); var p=new ControllablePaymentProvider("processing"); var gate=p.BlockStart(x.AttemptId); await using var d1=fixture.CreateDbContext(); await using var d2=fixture.CreateDbContext(); var a=new PaymentProviderRecoveryService(d1,p,new PaymentProcessingService(d1,p,new PaymentAttemptLifecycleService(d1))); var b=new PaymentProviderRecoveryService(d2,p,new PaymentProcessingService(d2,p,new PaymentAttemptLifecycleService(d2))); var ta=a.ProcessOneAsync(x.Tenant,x.AttemptId); await gate.Entered; await using(var held=fixture.CreateDbContext()){var e=await held.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.NotNull(e.ClaimedBy); Assert.NotNull(e.ClaimedUntil); Assert.Equal(key,e.ProviderIdempotencyKey); Assert.Equal(1,await held.Set<PaymentProviderExecution>().CountAsync(e=>e.PaymentAttemptId==x.AttemptId));} var tb=b.ProcessOneAsync(x.Tenant,x.AttemptId); await tb; Assert.Equal(1,p.StartCount(x.AttemptId)); gate.Release(); await ta; await using var final=fixture.CreateDbContext(); Assert.Equal(1,await final.Set<PaymentProviderExecution>().CountAsync(e=>e.PaymentAttemptId==x.AttemptId)); Assert.Equal(1,p.StartCount(x.AttemptId)); }
    [Fact] public async Task ExpiredClaimIsRecoveredUsingSameExecutionAndProviderKey() { var x=await CreateAttemptAsync(); var k=$"attempt:{x.AttemptId:N}"; var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.Pending,k); var old=Guid.NewGuid(); await using(var d=fixture.CreateDbContext()){var e=await d.Set<PaymentProviderExecution>().SingleAsync(e=>e.Id==id); e.ClaimedBy=old; e.ClaimedUntil=DateTimeOffset.UtcNow.AddMinutes(-1); await d.SaveChangesAsync(); Assert.Equal(1,await d.Set<PaymentProviderExecution>().CountAsync(e=>e.PaymentAttemptId==x.AttemptId));} var p=new ControllablePaymentProvider("processing"); await using var r=fixture.CreateDbContext(); var s=new PaymentProviderRecoveryService(r,p,new PaymentProcessingService(r,p,new PaymentAttemptLifecycleService(r))); await s.ProcessOneAsync(x.Tenant,x.AttemptId); await using var read=fixture.CreateDbContext(); var e2=await read.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.PaymentAttemptId==x.AttemptId); Assert.Equal(id,e2.Id); Assert.Equal(x.AttemptId,e2.PaymentAttemptId); Assert.Equal(k,e2.ProviderIdempotencyKey); Assert.Null(e2.ClaimedBy); Assert.Null(e2.ClaimedUntil); Assert.Equal(1,await read.Set<PaymentProviderExecution>().CountAsync(e=>e.PaymentAttemptId==x.AttemptId)); Assert.Equal(1,p.StartCount(x.AttemptId)); Assert.Equal(0,p.LookupCount(x.AttemptId)); }
    [Fact] public async Task StaleClaimantCannotOverwriteOutcomeFromNewerOwner() { var x=await CreateAttemptAsync(); var k=$"attempt:{x.AttemptId:N}"; var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.Pending,k); var before=await FinancialAsync(x); var auditBefore=await SemanticAuditCountAsync(x.AttemptId); var outboxBefore=await SemanticOutboxCountAsync(x.Tenant,x.AttemptId); var p=new ControllablePaymentProvider("processing"); p.ConfigureOrderedStartResults(x.AttemptId,"declined","approved"); var gateA=p.BlockStart(x.AttemptId); var gateB=p.BlockStart(x.AttemptId); await using var dbA=fixture.CreateDbContext(); var a=new PaymentProviderRecoveryService(dbA,p,new PaymentProcessingService(dbA,p,new PaymentAttemptLifecycleService(dbA))); var taskA=a.ProcessOneAsync(x.Tenant,x.AttemptId); await gateA.Entered; Guid claimantA; await using(var held=fixture.CreateDbContext()){var e=await held.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); claimantA=e.ClaimedBy!.Value; var expiry=DateTimeOffset.UtcNow.AddMinutes(-10); var expiryRows=await held.Database.ExecuteSqlInterpolatedAsync($"update payments.payment_provider_execution set claimed_until = {expiry} where id = {id} and establishment_id = {x.Tenant} and claimed_by = {claimantA}"); Assert.Equal(1,expiryRows); held.ChangeTracker.Clear(); e=await held.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); var postgresNow=await held.Database.SqlQuery<DateTimeOffset>($"select now() as \"Value\"").SingleAsync(); Assert.Equal(PaymentProviderExecutionStatus.Processing,e.Status); Assert.True(e.ClaimedUntil <= postgresNow); } await using var dbB=fixture.CreateDbContext(); var b=new PaymentProviderRecoveryService(dbB,p,new PaymentProcessingService(dbB,p,new PaymentAttemptLifecycleService(dbB))); var taskB=b.ProcessOneAsync(x.Tenant,x.AttemptId); var first=await Task.WhenAny(gateB.Entered,taskB,Task.Delay(TimeSpan.FromSeconds(10))); if(first==taskB) { string taskState; string taskError = ""; bool? taskResult = null; try { taskResult = await taskB; taskState = "RanToCompletion"; } catch(Exception ex) { taskState = "Faulted"; taskError = $"{ex.GetBaseException().GetType().FullName}: {ex.GetBaseException().Message}"; } await using var bDiag=fixture.CreateDbContext(); var bExec=await bDiag.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); var bNow=await bDiag.Database.SqlQuery<DateTimeOffset>($"select now() as \"Value\"").SingleAsync(); var bRows=await bDiag.Set<PaymentProviderExecution>().CountAsync(e=>e.Id==id); var bAttempt=await bDiag.Set<PaymentAttempt>().AsNoTracking().SingleOrDefaultAsync(a=>a.Id==x.AttemptId); Assert.Fail($"B completed before Start invocation #2 entered; taskB={taskState}; taskBResult={taskResult}; error={taskError}; executionStatus={bExec.Status}; claimedBy={bExec.ClaimedBy}; claimedUntil={bExec.ClaimedUntil}; dbNow={bNow}; providerKey={bExec.ProviderIdempotencyKey}; attemptExists={bAttempt is not null}; attemptId={bAttempt?.Id}; attemptStatus={bAttempt?.Status}; attemptProvider={bAttempt?.Provider}; attemptProviderKey={bExec.ProviderIdempotencyKey}; rows={bRows}; starts={p.StartCount(x.AttemptId)}; lookups={p.LookupCount(x.AttemptId)}; trace={string.Join(" || ",p.CallTrace)}"); } Assert.Same(gateB.Entered,first); await using(var claimed=fixture.CreateDbContext()){var e=await claimed.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.NotEqual(claimantA,e.ClaimedBy); Assert.Equal(PaymentProviderExecutionStatus.Processing,e.Status); Assert.Equal(k,e.ProviderIdempotencyKey);} gateB.Release(); await taskB; var bFinancial=await FinancialAsync(x); var bAudit=await SemanticAuditCountAsync(x.AttemptId); var bOutbox=await SemanticOutboxCountAsync(x.Tenant,x.AttemptId); await using(var beforeA=fixture.CreateDbContext()){var e=await beforeA.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); var at=await beforeA.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a=>a.Id==x.AttemptId); Assert.Equal(PaymentProviderExecutionStatus.Completed,e.Status); Assert.Equal("approved",e.NormalizedOutcome); Assert.Equal(PaymentAttemptStatus.Approved,at.Status); Assert.Equal(before.Reserved-x.Amount,bFinancial.Reserved); Assert.Equal(before.Paid+x.Amount,bFinancial.Paid); Assert.Equal(auditBefore+1,bAudit); Assert.Equal(outboxBefore+1,bOutbox); Assert.Null(e.ClaimedBy); Assert.Null(e.ClaimedUntil); Assert.Equal(k,e.ProviderIdempotencyKey);} gateA.Release(); await Assert.ThrowsAnyAsync<Exception>(()=>taskA); var finalFinancial=await FinancialAsync(x); await using var final=fixture.CreateDbContext(); var f=await final.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(PaymentProviderExecutionStatus.Completed,f.Status); Assert.Equal("approved",f.NormalizedOutcome); Assert.Equal(PaymentAttemptStatus.Approved,finalFinancial.Status); Assert.Equal(bFinancial,finalFinancial); Assert.Equal(bAudit,await SemanticAuditCountAsync(x.AttemptId)); Assert.Equal(bOutbox,await SemanticOutboxCountAsync(x.Tenant,x.AttemptId)); Assert.Equal(k,f.ProviderIdempotencyKey); Assert.Null(f.ClaimedBy); Assert.Null(f.ClaimedUntil); Assert.Equal(1,await final.Set<PaymentProviderExecution>().CountAsync(e=>e.PaymentAttemptId==x.AttemptId)); Assert.Equal(2,p.StartCount(x.AttemptId)); }
    [Fact] public async Task DifferentExecutionsCanBeProcessedConcurrently() { var x=await CreateAttemptAsync(); var y=await CreateAttemptAsync(); var kx=$"attempt:{x.AttemptId:N}"; var ky=$"attempt:{y.AttemptId:N}"; var ix=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.Pending,kx); var iy=await CreateExecutionAsync(y,PaymentProviderExecutionStatus.Pending,ky); var p=new ControllablePaymentProvider("processing"); var gx=p.BlockStart(x.AttemptId); var gy=p.BlockStart(y.AttemptId); await using var d1=fixture.CreateDbContext(); await using var d2=fixture.CreateDbContext(); var s1=new PaymentProviderRecoveryService(d1,p,new PaymentProcessingService(d1,p,new PaymentAttemptLifecycleService(d1))); var s2=new PaymentProviderRecoveryService(d2,p,new PaymentProcessingService(d2,p,new PaymentAttemptLifecycleService(d2))); var a=s1.ProcessOneAsync(x.Tenant,x.AttemptId); await gx.Entered; var b=s2.ProcessOneAsync(y.Tenant,y.AttemptId); await gy.Entered; await using(var mid=fixture.CreateDbContext()){var ex=await mid.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==ix); var ey=await mid.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==iy); Assert.Equal(ix,ex.Id); Assert.Equal(iy,ey.Id); Assert.Equal(kx,ex.ProviderIdempotencyKey); Assert.Equal(ky,ey.ProviderIdempotencyKey);} Assert.Equal(1,p.StartCount(x.AttemptId)); Assert.Equal(1,p.StartCount(y.AttemptId)); gx.Release(); gy.Release(); await Task.WhenAll(a,b); }
    [Fact] public async Task CompletedExecutionIsNotReprocessed() { var x=await CreateAttemptAsync(); var key=$"attempt:{x.AttemptId:N}"; var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.Pending,key); var provider=new ControllablePaymentProvider("processing"); await using(var db=fixture.CreateDbContext()){var lifecycle=new PaymentAttemptLifecycleService(db); await lifecycle.SucceedAsync(x.Tenant,x.AttemptId); var e=await db.Set<PaymentProviderExecution>().SingleAsync(e=>e.Id==id); e.Status=PaymentProviderExecutionStatus.Completed; e.NormalizedOutcome="approved"; e.ProviderReference="provider-ref"; e.ClaimedBy=null; e.ClaimedUntil=null; await db.SaveChangesAsync();} var before=await FinancialAsync(x); var audit=await SemanticAuditCountAsync(x.AttemptId); var outbox=await SemanticOutboxCountAsync(x.Tenant,x.AttemptId); await using(var db=fixture.CreateDbContext()){var e=await db.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); var a=await db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a=>a.Id==x.AttemptId); Assert.Equal(PaymentProviderExecutionStatus.Completed,e.Status); Assert.Equal(PaymentAttemptStatus.Approved,a.Status); Assert.Equal(x.Tenant,e.EstablishmentId);} await using(var db=fixture.CreateDbContext()){var recovery=new PaymentProviderRecoveryService(db,provider,new PaymentProcessingService(db,provider,new PaymentAttemptLifecycleService(db))); Assert.False(await recovery.ProcessOneAsync(x.Tenant,x.AttemptId));} await using(var db=fixture.CreateDbContext()){var e=await db.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); var a=await db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a=>a.Id==x.AttemptId); Assert.Equal(PaymentProviderExecutionStatus.Completed,e.Status); Assert.Equal("approved",e.NormalizedOutcome); Assert.Equal(PaymentAttemptStatus.Approved,a.Status); Assert.Equal(key,e.ProviderIdempotencyKey); Assert.Equal(before,await FinancialAsync(x)); Assert.Equal(audit,await SemanticAuditCountAsync(x.AttemptId)); Assert.Equal(outbox,await SemanticOutboxCountAsync(x.Tenant,x.AttemptId));} Assert.Equal(0,provider.StartCount(x.AttemptId)); Assert.Equal(0,provider.LookupCount(x.AttemptId)); }
    [Fact] public async Task CashIsNeverSelectedByProviderWorker() { var x=await CreateAttemptAsync(); await using(var seed=fixture.CreateDbContext()){var attempt=await seed.Set<PaymentAttempt>().SingleAsync(a=>a.Id==x.AttemptId); attempt.Method=PaymentMethod.Cash; await seed.SaveChangesAsync();} await using var db=fixture.CreateDbContext(); var attemptBefore=await db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a=>a.Id==x.AttemptId); Assert.Equal(PaymentMethod.Cash,attemptBefore.Method); Assert.Equal(x.Tenant,attemptBefore.EstablishmentId); var executionBefore=await db.Set<PaymentProviderExecution>().AsNoTracking().Where(e=>e.PaymentAttemptId==x.AttemptId).ToListAsync(); Assert.Empty(executionBefore); var provider=new ControllablePaymentProvider("processing"); var recovery=new PaymentProviderRecoveryService(db,provider,new PaymentProcessingService(db,provider,new PaymentAttemptLifecycleService(db))); var processed=await recovery.ProcessBatchAsync(25); Assert.True(processed >= 0); await using var verify=fixture.CreateDbContext(); var attemptAfter=await verify.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a=>a.Id==x.AttemptId); Assert.Equal(PaymentMethod.Cash,attemptAfter.Method); Assert.Empty(await verify.Set<PaymentProviderExecution>().AsNoTracking().Where(e=>e.PaymentAttemptId==x.AttemptId).ToListAsync()); Assert.Equal(0,provider.StartCount(x.AttemptId)); Assert.Equal(0,provider.LookupCount(x.AttemptId)); }
    [Fact] public async Task ForeignTenantExecutionCannotBeReconciledAcrossTenantBoundary() { var a=await CreateAttemptAsync(); var b=await CreateAttemptAsync(); Assert.NotEqual(a.Tenant,b.Tenant); var key=$"attempt:{b.AttemptId:N}"; var id=await CreateExecutionAsync(b,PaymentProviderExecutionStatus.Pending,key); var before=await FinancialAsync(b); var auditBefore=await SemanticAuditCountAsync(b.AttemptId); var outboxBefore=await SemanticOutboxCountAsync(b.Tenant,b.AttemptId); await using(var pre=fixture.CreateDbContext()){var e=await pre.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); var at=await pre.Set<PaymentAttempt>().AsNoTracking().SingleAsync(x=>x.Id==b.AttemptId); Assert.Equal(b.Tenant,e.EstablishmentId); Assert.Equal(b.AttemptId,e.PaymentAttemptId); Assert.Equal(PaymentProviderExecutionStatus.Pending,e.Status); Assert.Null(e.ClaimedBy); Assert.Null(e.ClaimedUntil); Assert.Equal(PaymentAttemptStatus.Created,at.Status);} var provider=new ControllablePaymentProvider("processing"); await using(var wrongDb=fixture.CreateDbContext()){var wrong=new PaymentProviderRecoveryService(wrongDb,provider,new PaymentProcessingService(wrongDb,provider,new PaymentAttemptLifecycleService(wrongDb))); Assert.False(await wrong.ProcessOneAsync(a.Tenant,b.AttemptId));} await using(var unchanged=fixture.CreateDbContext()){var e=await unchanged.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); var at=await unchanged.Set<PaymentAttempt>().AsNoTracking().SingleAsync(x=>x.Id==b.AttemptId); Assert.Equal(PaymentProviderExecutionStatus.Pending,e.Status); Assert.Null(e.ClaimedBy); Assert.Null(e.ClaimedUntil); Assert.Equal(key,e.ProviderIdempotencyKey); Assert.Equal(PaymentAttemptStatus.Created,at.Status); Assert.Equal(before,await FinancialAsync(b)); Assert.Equal(auditBefore,await SemanticAuditCountAsync(b.AttemptId)); Assert.Equal(outboxBefore,await SemanticOutboxCountAsync(b.Tenant,b.AttemptId));} Assert.Equal(0,provider.StartCount(b.AttemptId)); Assert.Equal(0,provider.LookupCount(b.AttemptId)); await using(var rightDb=fixture.CreateDbContext()){var right=new PaymentProviderRecoveryService(rightDb,provider,new PaymentProcessingService(rightDb,provider,new PaymentAttemptLifecycleService(rightDb))); Assert.True(await right.ProcessOneAsync(b.Tenant,b.AttemptId));} Assert.Equal(1,provider.StartCount(b.AttemptId)); Assert.Equal(0,provider.LookupCount(b.AttemptId)); await using var post=fixture.CreateDbContext(); var final=await post.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(b.Tenant,final.EstablishmentId); Assert.Equal(b.AttemptId,final.PaymentAttemptId); Assert.Equal(key,final.ProviderIdempotencyKey); }
    [Fact] public async Task CancellationStopsWorkerWithoutLeavingPermanentClaim() { var x=await CreateAttemptAsync(); var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.Pending,$"attempt:{x.AttemptId:N}"); var provider=new ControllablePaymentProvider("processing"); var gate=provider.BlockStart(x.AttemptId); await using var db=fixture.CreateDbContext(); var service=new PaymentProviderRecoveryService(db,provider,new PaymentProcessingService(db,provider,new PaymentAttemptLifecycleService(db))); using var cts=new CancellationTokenSource(); var task=service.ProcessOneAsync(x.Tenant,x.AttemptId,cts.Token); await gate.Entered; await using(var held=fixture.CreateDbContext()){var e=await held.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.NotNull(e.ClaimedBy); Assert.NotNull(e.ClaimedUntil); Assert.Equal(PaymentProviderExecutionStatus.Processing,e.Status);} cts.Cancel(); gate.Release(); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task); await using var final=fixture.CreateDbContext(); var persisted=await final.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); var now=DateTimeOffset.UtcNow; Assert.True(persisted.ClaimedUntil is null || persisted.ClaimedUntil > now); Assert.True(persisted.ClaimedUntil is null || persisted.ClaimedUntil <= now.AddMinutes(2).AddSeconds(5)); Assert.Equal(1,provider.StartCount(x.AttemptId)); Assert.Equal(0,provider.LookupCount(x.AttemptId)); }
    [Fact] public async Task AwaitingCustomerActionRecoveryDoesNotCreateNewCharge() { var x=await CreateAttemptAsync(); var key=$"attempt:{x.AttemptId:N}"; var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.AwaitingCustomerAction,key); await using(var seed=fixture.CreateDbContext()){var e=await seed.Set<PaymentProviderExecution>().SingleAsync(e=>e.Id==id); e.ClaimedUntil=DateTimeOffset.UtcNow.AddMinutes(-1); await seed.SaveChangesAsync();} var provider=new ControllablePaymentProvider("unknown",lookupStatus:"unknown"); await using var db=fixture.CreateDbContext(); var service=new PaymentProviderRecoveryService(db,provider,new PaymentProcessingService(db,provider,new PaymentAttemptLifecycleService(db))); await service.ProcessOneAsync(x.Tenant,x.AttemptId); await using var final=fixture.CreateDbContext(); var e2=await final.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(id,e2.Id); Assert.Equal(key,e2.ProviderIdempotencyKey); Assert.Equal(1,provider.LookupCount(x.AttemptId)); Assert.Equal(0,provider.StartCount(x.AttemptId)); Assert.Equal(1,await final.Set<PaymentProviderExecution>().CountAsync(e=>e.PaymentAttemptId==x.AttemptId)); }
    [Fact] public async Task ProcessingRecoveryUsesLookupOrSafeClassificationWithoutDuplicateIntent() { var x=await CreateAttemptAsync(); var key=$"attempt:{x.AttemptId:N}"; var id=await CreateExecutionAsync(x,PaymentProviderExecutionStatus.Processing,key); await using(var seed=fixture.CreateDbContext()){var e=await seed.Set<PaymentProviderExecution>().SingleAsync(e=>e.Id==id); e.ClaimedUntil=DateTimeOffset.UtcNow.AddMinutes(-1); await seed.SaveChangesAsync();} var provider=new ControllablePaymentProvider("processing"){StartFailureClassification=PaymentRecoveryClassification.SafeToRetry,FailOnlyFirstStart=true}; await using var db=fixture.CreateDbContext(); var service=new PaymentProviderRecoveryService(db,provider,new PaymentProcessingService(db,provider,new PaymentAttemptLifecycleService(db))); await service.ProcessOneAsync(x.Tenant,x.AttemptId); await using var final=fixture.CreateDbContext(); var e2=await final.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e=>e.Id==id); Assert.Equal(id,e2.Id); Assert.Equal(key,e2.ProviderIdempotencyKey); Assert.Equal(1,provider.StartCount(x.AttemptId)); Assert.Equal(0,provider.LookupCount(x.AttemptId)); Assert.Equal(1,await final.Set<PaymentProviderExecution>().CountAsync(e=>e.PaymentAttemptId==x.AttemptId)); Assert.Equal(PaymentProviderExecutionStatus.Pending,e2.Status); Assert.Null(e2.ClaimedBy); Assert.Null(e2.ClaimedUntil); }

    private async Task<(Guid Tenant, Guid AttemptId, decimal Amount)> CreateAttemptAsync()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext()) { var session = await db.Set<TableSession>().SingleAsync(s => s.Id == context.SessionId); session.TotalAmount = 10m; await db.SaveChangesAsync(); }
        var plan = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, Guid.NewGuid()); plan.EnsureSuccessStatusCode();
        using var planJson = JsonDocument.Parse(await plan.Content.ReadAsStringAsync());
        var planId = planJson.RootElement.GetProperty("planId").GetGuid();
        var allocationId = planJson.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid();
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = new[] { allocationId }, paymentMethod = "pix" }, context.Device.AccessToken, Guid.NewGuid()); response.EnsureSuccessStatusCode();
        using var attemptJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var attemptId = attemptJson.RootElement.GetProperty("attemptId").GetGuid();
        await using var dbRead = fixture.CreateDbContext();
        var tenant = await dbRead.Set<TableSession>().Where(s => s.Id == context.SessionId).Select(s => s.EstablishmentId).SingleAsync();
        return (tenant, attemptId, 10m);
    }

    private async Task<Guid> CreateExecutionAsync((Guid Tenant, Guid AttemptId, decimal Amount) x, PaymentProviderExecutionStatus status, string key)
    {
        await using var db = fixture.CreateDbContext();
        var id = Guid.NewGuid();
        db.Set<PaymentProviderExecution>().Add(new PaymentProviderExecution { Id = id, EstablishmentId = x.Tenant, PaymentAttemptId = x.AttemptId, Provider = "ControllablePaymentProvider", ProviderIdempotencyKey = key, Status = status, ReconciliationRequired = status == PaymentProviderExecutionStatus.Unknown, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<PaymentProviderExecution> ReloadExecutionAsync((Guid Tenant, Guid AttemptId, decimal Amount) x)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<PaymentProviderExecution>().AsNoTracking().SingleAsync(e => e.EstablishmentId == x.Tenant && e.PaymentAttemptId == x.AttemptId);
    }

    private async Task<(PaymentAttemptStatus Status, decimal Reserved, decimal Paid)> FinancialAsync((Guid Tenant, Guid AttemptId, decimal Amount) x)
    {
        await using var db = fixture.CreateDbContext();
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a => a.Id == x.AttemptId);
        var session = await db.Set<TableSession>().AsNoTracking().SingleAsync(s => s.Id == attempt.TableSessionId);
        return (attempt.Status, session.ReservedAmount, session.PaidAmount);
    }

    private async Task<int> SemanticAuditCountAsync(Guid attemptId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Appizza.Modules.Auditing.AuditEntry>().CountAsync(a => a.AggregateId == attemptId && a.Action == "payment_attempt.approved");
    }

    private async Task<int> SemanticOutboxCountAsync(Guid tenant, Guid attemptId)
    {
        await using var db = fixture.CreateDbContext();
        var candidates = await db.OutboxMessages.AsNoTracking()
            .Where(e => e.EstablishmentId == tenant && e.EventType == "payment-attempt-approved.v1")
            .Select(e => e.Payload)
            .ToListAsync();
        return candidates.Count(payload =>
        {
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.TryGetProperty("paymentAttemptId", out var value) && value.GetGuid() == attemptId;
        });
    }
}
