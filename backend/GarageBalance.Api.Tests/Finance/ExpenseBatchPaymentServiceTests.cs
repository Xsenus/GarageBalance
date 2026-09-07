using System.Reflection;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace GarageBalance.Api.Tests.Finance;

public sealed class ExpenseBatchPaymentServiceTests
{
    private static readonly DateOnly Month = new(2026, 8, 1);

    [PostgreSqlFact]
    public async Task Http_EnforcesPermissionsAndValidationAndReplaysAfterCommentPolicyChanges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database);
        var settings = DispatchProxy.Create<IApplicationSettingsService, SettingsProxy>();
        var settingsState = (SettingsProxy)(object)settings;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('t', 64)));
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "expense-batch-test",
                ValidateAudience = true,
                ValidAudience = "expense-batch-test",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var permission in new[] { SystemPermissions.PaymentsRead, SystemPermissions.PaymentsWrite })
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
        });
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton(settings);
        builder.Services.AddScoped<ActionCommentRequirementFilter>();
        builder.Services.AddScoped(_ => database.CreateContext());
        builder.Services.AddScoped<IExpenseBatchPreviewService>(provider => Services(provider.GetRequiredService<GarageBalanceDbContext>()).Item2);
        builder.Services.AddScoped<IExpenseBatchPaymentService>(provider => Services(provider.GetRequiredService<GarageBalanceDbContext>()).Item1);
        builder.Services.AddControllers(options => options.Filters.AddService<ActionCommentRequirementFilter>())
            .AddApplicationPart(typeof(ExpenseBatchesController).Assembly)
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiProblemDetails.CreateInvalidModelStateResponse);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            const string route = "/api/finance/expense-batches";
            var previewRequest = new ExpenseBatchPreviewRequest(Month, Month);
            using (var anonymous = await client.PostAsJsonAsync(route + "/preview", previewRequest))
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            foreach (var permissions in new[] { Array.Empty<string>(), new[] { SystemPermissions.PaymentsRead }, new[] { SystemPermissions.PaymentsWrite } })
            {
                Authorize(permissions);
                using var denied = await client.PostAsJsonAsync(route + "/preview", previewRequest);
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                using var deniedPay = await client.PostAsJsonAsync(route, new { fingerprint = "invalid" });
                Assert.Equal(HttpStatusCode.Forbidden, deniedPay.StatusCode);
            }
            Authorize([SystemPermissions.PaymentsRead, SystemPermissions.PaymentsWrite]);
            using (var invalid = await client.PostAsJsonAsync(route, new { fingerprint = "invalid" }))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var previewResponse = await client.PostAsJsonAsync(route + "/preview", previewRequest);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = (await previewResponse.Content.ReadFromJsonAsync<ExpenseBatchPreviewDto>())!;
            var request = new ExpenseBatchPaymentRequest(Guid.NewGuid(), Month, Month, preview.Fingerprint, false, null);
            settingsState.CommentsRequired = true;
            using (var required = await client.PostAsJsonAsync(route, request))
            {
                Assert.Equal(HttpStatusCode.BadRequest, required.StatusCode);
                Assert.Contains("expense_batch_comment_required", await required.Content.ReadAsStringAsync());
            }
            settingsState.CommentsRequired = false;
            using var paidResponse = await client.PostAsJsonAsync(route, request);
            Assert.Equal(HttpStatusCode.OK, paidResponse.StatusCode);
            var paid = (await paidResponse.Content.ReadFromJsonAsync<ExpenseBatchPaymentResult>())!;
            Assert.Equal(2, paid.OperationIds.Count);
            settingsState.CommentsRequired = true;
            using var repeatedResponse = await client.PostAsJsonAsync(route, request);
            Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
            var repeated = (await repeatedResponse.Content.ReadFromJsonAsync<ExpenseBatchPaymentResult>())!;
            Assert.Equal(paid.OperationIds, repeated.OperationIds);
            using var conflict = await client.PostAsJsonAsync(route, request with { Comment = "Другое содержимое" });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            await using var check = database.CreateContext();
            Assert.Equal(2, await check.FinancialOperations.CountAsync());
            Assert.Equal(1, await check.ExpensePaymentBatches.CountAsync());
            Assert.Equal(1, await check.AuditEvents.CountAsync(item => item.Action == "finance.expense_batch_created"));

            void Authorize(string[] permissions)
            {
                var claims = permissions.Select(permission => new Claim("permission", permission))
                    .Append(new Claim(ClaimTypes.NameIdentifier, seed.Actor.ToString()));
                var token = new JwtSecurityToken("expense-batch-test", "expense-batch-test", claims,
                    expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [PostgreSqlFact]
    public async Task PayAsync_PaysRegisteredSalaryMonthWithoutInventingEarlierEmploymentDebt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database);
        await using var context = database.CreateContext();
        var staff = await context.StaffMembers.SingleAsync();
        context.StaffEmploymentPeriods.Add(new StaffEmploymentPeriod
        {
            StaffMemberId = staff.Id,
            EffectiveFrom = Month.AddMonths(-6)
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var (service, preview) = Services(context);
        var calculation = await preview.PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.True(calculation.Succeeded, calculation.ErrorMessage);
        var salary = Assert.Single(calculation.Value!.Items, item => item.Payment.RecipientKind == "staff");
        Assert.Equal(Month, salary.Payment.AccountingMonth);
        Assert.Equal(70m, salary.Payment.Amount);
        var result = await service.PayAsync(new(Guid.NewGuid(), Month, Month, calculation.Value.Fingerprint, false, "Контроль периода"), seed.Actor, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var payment = await context.FinancialOperations.AsNoTracking().SingleAsync(item => item.StaffMemberId == staff.Id);
        Assert.Equal(Month, payment.AccountingMonth);
        Assert.Equal(70m, payment.Amount);
        var after = await preview.PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.Empty(after.Value!.Items);
    }

    [PostgreSqlFact]
    public async Task PayAsync_CommitsMixedPaymentsAuditAndIdempotentResult()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database);
        await using var context = database.CreateContext();
        var (service, preview) = Services(context);
        var request = (await Request(preview)) with { Comment = null };
        FinanceResult<ExpenseBatchPaymentResult> result;
        using (ActionCommentRequirementContext.Push(false))
        {
            result = await service.PayAsync(request, seed.Actor, CancellationToken.None);
        }
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, result.Value!.OperationIds.Count);
        var repeat = await service.PayAsync(request, seed.Actor, CancellationToken.None);
        Assert.Equal(result.Value.OperationIds, repeat.Value!.OperationIds);
        Assert.Equal("expense_batch_request_conflict", (await service.PayAsync(request with { Comment = "Другой комментарий" }, seed.Actor, CancellationToken.None)).ErrorCode);
        Assert.Equal(2, await context.FinancialOperations.CountAsync());
        Assert.Equal(1, await context.ExpensePaymentBatches.CountAsync());
        Assert.Equal(20m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Fund)).Balance);
        Assert.Equal(1, await context.AuditEvents.CountAsync(item => item.Action == "finance.expense_batch_created"));
        Assert.Equal(1, await context.SupplierAccruals.CountAsync());
    }

    [PostgreSqlFact]
    public async Task PayAsync_ConcurrentIdenticalRequestsReturnSameOperations()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var (one, preview) = Services(first);
        var (two, _) = Services(second);
        var request = await Request(preview);
        var results = await Task.WhenAll(one.PayAsync(request, seed.Actor, CancellationToken.None), two.PayAsync(request, seed.Actor, CancellationToken.None));
        Assert.All(results, result => Assert.True(result.Succeeded, result.ErrorMessage));
        Assert.Equal(results[0].Value!.OperationIds, results[1].Value!.OperationIds);
        await using var check = database.CreateContext();
        Assert.Equal(2, await check.FinancialOperations.CountAsync());
    }

    [PostgreSqlFact]
    public async Task PayAsync_RollsBackSupplierPaymentWhenSalaryFails()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database);
        await using var context = database.CreateContext();
        var (service, preview) = Services(context, failSalary: true);
        var auditBefore = await context.AuditEvents.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync();
        var request = await Request(preview);
        var result = await service.PayAsync(request, seed.Actor, CancellationToken.None);
        Assert.Equal("staff_payment_amount_exceeds_available", result.ErrorCode);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await context.FinancialOperations.ToListAsync());
        Assert.Empty(await context.ExpensePaymentBatches.ToListAsync());
        Assert.Equal(auditBefore, await context.AuditEvents.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync());
        Assert.Equal(50m, (await context.Funds.SingleAsync(item => item.Id == seed.Fund)).Balance);
    }

    [PostgreSqlFact]
    public async Task PayAsync_RequiresNegativeFundConfirmationAndRevalidatesSnapshot()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database, 20m);
        await using var context = database.CreateContext();
        var (service, preview) = Services(context);
        var request = await Request(preview);
        Assert.Equal("expense_batch_negative_fund_confirmation_required", (await service.PayAsync(request, seed.Actor, CancellationToken.None)).ErrorCode);
        var changed = request with { Fingerprint = new string('b', 64), ConfirmNegativeFundBalance = true };
        Assert.Equal("expense_batch_preview_changed", (await service.PayAsync(changed, seed.Actor, CancellationToken.None)).ErrorCode);
        var result = await service.PayAsync(request with { ConfirmNegativeFundBalance = true }, seed.Actor, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(-10m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Fund)).Balance);
        Assert.True(await context.FinancialOperations.AnyAsync(item => item.NegativeFundBalanceConfirmed));
    }

    [PostgreSqlFact]
    public async Task PayAsync_DetectsChangeAfterLocksAndRejectsInvalidRequestsWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database);
        await using var context = database.CreateContext();
        var (_, original) = Services(context);
        var changing = new ChangedPreview(original);
        var (service, _) = Services(context, previewOverride: changing);
        var request = await Request(original);
        Assert.Equal("expense_batch_preview_changed", (await service.PayAsync(request, seed.Actor, CancellationToken.None)).ErrorCode);
        Assert.Empty(await context.FinancialOperations.ToListAsync());
        foreach (var invalid in new[] { request with { RequestId = Guid.Empty }, request with { Fingerprint = "invalid" }, request with { Comment = "x" } })
            Assert.Equal("expense_batch_request_invalid", (await service.PayAsync(invalid, seed.Actor, CancellationToken.None)).ErrorCode);
        Assert.Equal("expense_batch_request_invalid", (await service.PayAsync(request, Guid.Empty, CancellationToken.None)).ErrorCode);
        Assert.Equal("expense_batch_comment_required", (await service.PayAsync(request with { Comment = null }, seed.Actor, CancellationToken.None)).ErrorCode);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PayAsync(request, seed.Actor, new CancellationToken(true)));
    }

    [PostgreSqlFact]
    public async Task PayAsync_ConflictingKeyFromAnotherAuthorRollsBackAttemptWithoutDisclosingResult()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database);
        await using var context = database.CreateContext();
        var (service, preview) = Services(context);
        var original = await Request(preview);
        Assert.True((await service.PayAsync(original, seed.Actor, CancellationToken.None)).Succeeded);
        var other = new AppUser { Email = "other@example.test", NormalizedEmail = "OTHER@EXAMPLE.TEST", DisplayName = "Другой", PasswordHash = "test-only" };
        var supplier = await context.Suppliers.SingleAsync();
        context.Add(other);
        context.Add(new SupplierAccrual
        {
            Supplier = supplier,
            ExpenseTypeId = supplier.ExpenseTypeId!.Value,
            ExpenseFundId = supplier.ExpenseFundId,
            AccountingMonth = Month,
            Amount = 30m,
            Source = "manual"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var fresh = await Request(preview);
        var result = await service.PayAsync(fresh with { RequestId = original.RequestId, ConfirmNegativeFundBalance = true }, other.Id, CancellationToken.None);
        Assert.Equal("expense_batch_request_conflict", result.ErrorCode);
        Assert.Null(result.Value);
        Assert.Equal(2, await context.FinancialOperations.CountAsync());
        Assert.Equal(seed.Actor, (await context.ExpensePaymentBatches.AsNoTracking().SingleAsync()).ActorUserId);
        Assert.Equal(20m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Fund)).Balance);
    }

    [PostgreSqlFact]
    public async Task PayAsync_RejectsUnavailableEmptyInvalidAndFailedPreviewsWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var seed = await Seed(database);
        await using var context = database.CreateContext();
        var (_, preview) = Services(context);
        var value = (await preview.PreviewAsync(new(Month, Month), CancellationToken.None)).Value!;
        foreach (var scenario in new[] { "empty", "money", "amount", "error" })
        {
            var changed = scenario switch
            {
                "empty" => value with { Items = [] },
                "money" => value with { Issues = ["Недостаточно средств"] },
                "amount" => value with { Items = [value.Items[0] with { Payment = value.Items[0].Payment with { Amount = 0 } }] },
                _ => value
            };
            var fixedPreview = new FixedPreview(scenario == "error" ? FinanceResult<ExpenseBatchPreviewDto>.Failure("preview_failed", "Ошибка")
                : FinanceResult<ExpenseBatchPreviewDto>.Success(changed));
            var (service, _) = Services(context, previewOverride: fixedPreview);
            var request = new ExpenseBatchPaymentRequest(Guid.NewGuid(), Month, Month, value.Fingerprint, false, "Проверка");
            var result = await service.PayAsync(request, seed.Actor, CancellationToken.None);
            Assert.Equal(scenario == "error" ? "preview_failed" : scenario == "amount" ? "expense_batch_amount_invalid" : "expense_batch_not_payable", result.ErrorCode);
        }
        Assert.Empty(await context.FinancialOperations.ToListAsync());
    }

    private sealed class FixedPreview(FinanceResult<ExpenseBatchPreviewDto> result) : IExpenseBatchPreviewService
    {
        public Task<FinanceResult<ExpenseBatchPreviewDto>> PreviewAsync(ExpenseBatchPreviewRequest request, CancellationToken token) => Task.FromResult(result);
    }

    private static async Task<ExpenseBatchPaymentRequest> Request(IExpenseBatchPreviewService preview)
    {
        var result = await preview.PreviewAsync(new(Month, Month.AddDays(29)), CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        return new(Guid.NewGuid(), Month, Month.AddDays(29), result.Value!.Fingerprint, false, "Проверка пакета");
    }

    private static (ExpenseBatchPaymentService, IExpenseBatchPreviewService) Services(GarageBalanceDbContext context,
        bool failSalary = false, IExpenseBatchPreviewService? previewOverride = null)
    {
        var time = new FixedTime();
        var finance = FinanceServiceTestFactory.Create(context, time);
        var settings = DispatchProxy.Create<IApplicationSettingsService, SettingsProxy>();
        var preview = previewOverride ?? new ExpenseBatchPreviewService(finance,
            new EfExpenseBatchStaffDebtQuery(context, TestBusinessDateProvider.From(time)), settings, TestBusinessDateProvider.From(time));
        IFinanceService execution = finance;
        if (failSalary)
        {
            execution = DispatchProxy.Create<IFinanceService, FailedSalaryProxy>();
            ((FailedSalaryProxy)(object)execution).Inner = finance;
        }
        return (new(preview, execution, new EfExpensePaymentBatchRepository(context), new EfExpenseBatchTransactionRunner(context),
            new ExpenseFundDisbursementService(new EfFundRepository(context), new AuditEventWriter(context)),
            new EfFinanceAvailableBalanceQuery(context), new EfStaffSalaryAdjustmentRepository(context),
            new EfApplicationUnitOfWork(context), new AuditEventWriter(context), time), preview);
    }

    private static async Task<(Guid Actor, Guid Fund)> Seed(PostgreSqlTestDatabase database, decimal fundBalance = 50m)
    {
        await using var context = database.CreateContext();
        var user = new AppUser { Email = "batch@example.test", NormalizedEmail = "BATCH@EXAMPLE.TEST", DisplayName = "Проверка", PasswordHash = "test-only" };
        var fund = new Fund { Name = "Проверка пакета", NormalizedName = "ПРОВЕРКА ПАКЕТА", Balance = fundBalance, AllowOperations = true };
        var type = new ExpenseType { Name = "Проверочная услуга" };
        var supplier = new Supplier
        {
            Name = "Проверочный поставщик",
            Group = new SupplierGroup { Name = "Проверка" },
            SupplierService = new SupplierService { Name = type.Name },
            ExpenseType = type,
            ExpenseFund = fund
        };
        var staff = new StaffMember
        {
            FullName = "Проверочный сотрудник",
            Rate = 70m,
            Department = new StaffDepartment { Name = "Проверка" },
            CreatedAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)
        };
        context.AddRange(user, supplier, staff, new SupplierAccrual
        { Supplier = supplier, ExpenseType = type, ExpenseFund = fund, AccountingMonth = Month, Amount = 30m, Source = "manual" });
        foreach (var account in new[] { "bank", "cash" })
            context.Add(new CashBankBalanceOperation { Account = account, Direction = "increase", OperationKind = "opening_balance", OperationDate = Month, Amount = 100m, Reason = "Контроль" });
        await context.SaveChangesAsync();
        return (user.Id, fund.Id);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
    }
    public class SettingsProxy : DispatchProxy
    {
        public bool CommentsRequired { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(IApplicationSettingsService.GetSalaryAccrualSettingsAsync) => Task.FromResult(new SalaryAccrualSettingsDto(1)),
            nameof(IApplicationSettingsService.GetActionCommentSettingsAsync) => Task.FromResult(new ActionCommentSettingsDto(CommentsRequired)),
            _ => throw new NotSupportedException()
        };
    }
    public class FailedSalaryProxy : DispatchProxy
    {
        public IFinanceService Inner { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(IFinanceService.CreateStaffPaymentAsync)
                ? Task.FromResult(FinanceResult<FinancialOperationDto>.Failure("staff_payment_amount_exceeds_available", "Контроль отказа"))
                : method.Invoke(Inner, args);
    }
    private sealed class ChangedPreview(IExpenseBatchPreviewService inner) : IExpenseBatchPreviewService
    {
        private int calls;
        public async Task<FinanceResult<ExpenseBatchPreviewDto>> PreviewAsync(ExpenseBatchPreviewRequest request, CancellationToken token)
        {
            var result = await inner.PreviewAsync(request, token);
            return ++calls == 1 ? result : FinanceResult<ExpenseBatchPreviewDto>.Success(result.Value! with { Fingerprint = new string('b', 64) });
        }
    }
}
