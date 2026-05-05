using Hangfire;
using Hangfire.MySql;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Hubs;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using MySql.Data.MySqlClient;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register Data Access Layer
builder.Services.AddScoped<IDbConnectionFactory, MySqlConnectionFactory>();

// Register Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IUserRoleService, UserRoleService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IUserLocationService, UserLocationService>();
builder.Services.AddScoped<IUserPrivilegeService, UserPrivilegeService>();
builder.Services.AddScoped<ISetupService, SetupService>();
builder.Services.AddScoped<ITaxSetupService, TaxSetupService>();
builder.Services.AddScoped<IRouteSetupService, RouteSetupService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<ILeavesService, LeavesService>();
builder.Services.AddScoped<ILoansService, LoansService>();
builder.Services.AddScoped<IExtrasService, ExtrasService>();
builder.Services.AddScoped<IOvertimeService, OvertimeService>();
builder.Services.AddScoped<ICommissionService, CommissionService>();
builder.Services.AddScoped<IPayrollService, PayrollService>();
builder.Services.AddScoped<ICommissionExecutionHistoryService, CommissionExecutionHistoryService>();
builder.Services.AddScoped<ICommissionVerificationService, CommissionVerificationService>();
builder.Services.AddScoped<IPenaltyService, PenaltyService>();
builder.Services.AddScoped<IClosingService, ClosingService>();
builder.Services.AddScoped<ISettlementService, SettlementService>();
builder.Services.AddScoped<IAdvanceSalaryService, AdvanceSalaryService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ISupportService, SupportService>();
builder.Services.AddScoped<ICommissionInvestigationService, CommissionInvestigationService>();
builder.Services.AddScoped<IAttendanceManagementService, AttendanceManagementService>();

// ── Bootstrap: sync app-required tables from live if missing locally ──────────
{
    var localConn = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";
    var liveConn  = builder.Configuration.GetSection("LiveConnections")["DefaultConnection"] ?? "";

    // Use a short probe timeout so a dead/unreachable server never stalls startup.
    // The global Connection Timeout (60 s) is only for real app queries.
    static string WithProbeTimeout(string cs) =>
        System.Text.RegularExpressions.Regex.Replace(
            cs, @"(?i)Connection Timeout\s*=\s*\d+", "Connection Timeout=5");

    var localProbe = WithProbeTimeout(localConn);
    var liveProbe  = WithProbeTimeout(liveConn);

    // All lcs_hr tables required for the app to function.
    // Add more here if a new "table doesn't exist" error appears.
    var requiredTables = new[]
    {
        // Auth & users
        "lcs_users",
        "lcs_users_roles",
        "lcs_user_location",
        // Navigation menu
        "lcs_menu",
        "lcs_submenu",
        "lcs_submenu_det",
        "lcs_roles_privileges",
        // Setup master data
        "hr_regionalzones",
        "hr_city",
    };

    // Sync each required table (skip entirely if it already has data locally)
    if (string.IsNullOrEmpty(liveProbe))
    {
        Console.WriteLine("[Bootstrap] No LiveConnections:DefaultConnection configured — skipping table sync.");
    }
    else
    {
        foreach (var tableName in requiredTables)
        {
            try
            {
                // Check local (with short probe timeout)
                long localCount = 0;
                bool exists = false;
                using (var local = new MySqlConnection(localProbe))
                {
                    local.Open();
                    using var chk = local.CreateCommand();
                    chk.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                                      $"WHERE TABLE_SCHEMA='lcs_hr' AND TABLE_NAME='{tableName}'";
                    exists = Convert.ToInt64(chk.ExecuteScalar()) > 0;
                    if (exists)
                    {
                        using var cnt = local.CreateCommand();
                        cnt.CommandText = $"SELECT COUNT(*) FROM `lcs_hr`.`{tableName}`";
                        localCount = Convert.ToInt64(cnt.ExecuteScalar());
                    }
                }

                // If table already has data locally, skip without touching the live server
                if (exists && localCount > 0)
                {
                    Console.WriteLine($"[Bootstrap] {tableName,-30} local:{localCount,5}  → already populated, skipped.");
                    continue;
                }

                // Check live (only needed when local table is missing or empty)
                long liveCount = 0;
                using (var live = new MySqlConnection(liveProbe))
                {
                    live.Open();
                    using var cnt = live.CreateCommand();
                    cnt.CommandText = $"SELECT COUNT(*) FROM `lcs_hr`.`{tableName}`";
                    liveCount = Convert.ToInt64(cnt.ExecuteScalar());
                }

                Console.Write($"[Bootstrap] {tableName,-30} local:{localCount,5}  live:{liveCount,5}  → ");

                // Create table from live DDL if missing locally
                if (!exists || localCount == 0)
                {
                    string ddl;
                    using (var live = new MySqlConnection(liveProbe))
                    {
                        live.Open();
                        using var ddlCmd = live.CreateCommand();
                        ddlCmd.CommandText = $"SHOW CREATE TABLE `lcs_hr`.`{tableName}`";
                        using var reader = ddlCmd.ExecuteReader();
                        reader.Read();
                        ddl = reader.GetString(1);
                    }
                    ddl = System.Text.RegularExpressions.Regex.Replace(
                        ddl, @"CREATE TABLE `([^`]+)`",
                        $"CREATE TABLE IF NOT EXISTS `lcs_hr`.`{tableName}`");
                    ddl = System.Text.RegularExpressions.Regex.Replace(
                        ddl, @"\s*AUTO_INCREMENT=\d+", "");

                    using var local = new MySqlConnection(localProbe);
                    local.Open();
                    using var createCmd = local.CreateCommand();
                    createCmd.CommandText = ddl;
                    createCmd.ExecuteNonQuery();
                }

                // INSERT IGNORE all live rows (duplicates auto-skipped, only new rows inserted)
                var inserted = SyncTableFromLive(liveConn, localConn, "lcs_hr", tableName);
                Console.WriteLine($"synced ({inserted} row(s) inserted).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bootstrap] WARN {tableName}: {ex.Message}");
            }
        }
    }
}
// ─────────────────────────────────────────────────────────────────────────────

// Commission Automation (Hangfire + SignalR)
// MySqlConnector (used by Hangfire.MySqlStorage) uses SslMode=None instead of SslMode=Disabled
// MySqlConnector (Hangfire) rejects "Default Command Timeout" (with spaces) — strip it.
// MySql.Data uses that keyword; MySqlConnector uses "DefaultCommandTimeout" (no spaces).
var hangfireConnStr = System.Text.RegularExpressions.Regex.Replace(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? "",
        @"(?i)Default\s+Command\s+Timeout\s*=\s*\d+\s*;?", "")
    .Replace("SslMode=Disabled", "SslMode=None", StringComparison.OrdinalIgnoreCase)
    .Replace("ConnectionReset=True;", "", StringComparison.OrdinalIgnoreCase)
    .Replace("ConnectionReset=True", "", StringComparison.OrdinalIgnoreCase);
builder.Services.AddSignalR();
builder.Services.AddHangfire(config =>
    config.UseStorage(new MySqlStorage(hangfireConnStr, new MySqlStorageOptions
    {
        TablesPrefix = "Hangfire_"
    })));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<ICommissionAutomationService, CommissionAutomationService>();
// Singleton: service uses a static ConcurrentDictionary for in-memory migration state.
// Singleton lifetime keeps the service instance alive for the app's lifetime, which
// matches the static field — preventing state loss on per-request scoped teardown
// and ensuring Hangfire workers share the same in-memory state as web requests.
builder.Services.AddSingleton<ITestDataMigrationService, TestDataMigrationService>();
builder.Services.AddScoped<IDataSyncService, DataSyncService>();
builder.Services.AddScoped<AcTestComparisonService>();

// Configure Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(40); // Matching the old 40 min timeout
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Configure Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

// Configure HttpContextAccessor for accessing Session/User in services if needed
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Order is important: UseSession, UseAuthentication, UseAuthorization
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Custom Routes for Transaction HR replacing 404s
app.MapControllerRoute(
    name: "TransactionHR_Termination",
    pattern: "Transaction/HR/EmployeeTermination",
    defaults: new { controller = "Settlement", action = "EmployeeTermination" });
app.MapControllerRoute(
    name: "TransactionHR",
    pattern: "Transaction/HR/{action}",
    defaults: new { controller = "Transaction" });

// Custom Routes for Transaction/Employee Details
app.MapControllerRoute(
    name: "TransactionED_EmpADDetails",
    pattern: "Transaction/EmployeeDetails/EmpADDetails",
    defaults: new { controller = "Extras", action = "EmpADDetails" });
app.MapControllerRoute(
    name: "TransactionED_Shifts",
    pattern: "Transaction/EmployeeDetails/Shifts",
    defaults: new { controller = "Setup", action = "Shifts" });
app.MapControllerRoute(
    name: "TransactionED_EmpCommDetails",
    pattern: "Transaction/EmployeeDetails/EmployeeCommDetails",
    defaults: new { controller = "Commission", action = "EmployeeCommDetails" });
app.MapControllerRoute(
    name: "TransactionED_EmpExtraFixed",
    pattern: "Transaction/EmployeeDetails/EmployeeExtraFixed",
    defaults: new { controller = "Extras", action = "EmployeeExtraFixed" });
app.MapControllerRoute(
    name: "TransactionED_Default",
    pattern: "Transaction/EmployeeDetails/{action}",
    defaults: new { controller = "Transaction" });

// Custom Routes for Transaction/EmployeeAdjustments
app.MapControllerRoute(
    name: "TransactionEA_EmpExtra",
    pattern: "Transaction/EmployeeAdjustments/EmployeeExtra",
    defaults: new { controller = "Extras", action = "EmployeeExtra" });
app.MapControllerRoute(
    name: "TransactionEA_EmpOvertime",
    pattern: "Transaction/EmployeeAdjustments/EmployeeOvertime",
    defaults: new { controller = "Overtime", action = "EmployeeOvertime" });
app.MapControllerRoute(
    name: "TransactionEA_BulkAttAdj",
    pattern: "Transaction/EmployeeAdjustments/BulkAttendanceAdjustment",
    defaults: new { controller = "Payroll", action = "BulkAttendanceAdjustment" });
app.MapControllerRoute(
    name: "TransactionEA_ExtraHoursAppr",
    pattern: "Transaction/EmployeeAdjustments/ExtraHoursApproval",
    defaults: new { controller = "Extras", action = "ExtraHoursApproval" });
app.MapControllerRoute(
    name: "TransactionEA_EmpAdvSal",
    pattern: "Transaction/EmployeeAdjustments/EmpAdvanceSalary",
    defaults: new { controller = "AdvanceSalary", action = "EmpAdvanceSalary" });
app.MapControllerRoute(
    name: "TransactionEA_PenaltyFine",
    pattern: "Transaction/EmployeeAdjustments/PenaltyFine",
    defaults: new { controller = "Penalty", action = "PenaltyFine" });
app.MapControllerRoute(
    name: "TransactionEA_AdvSalAppr",
    pattern: "Transaction/EmployeeAdjustments/AdvanceSalaryApprove",
    defaults: new { controller = "AdvanceSalary", action = "AdvanceSalaryApprove" });

// Custom Routes for Transaction/EmployeeLoan
app.MapControllerRoute(
    name: "TransactionEL_LoanReq",
    pattern: "Transaction/EmployeeLoan/EmpLoanRequest",
    defaults: new { controller = "Loans", action = "EmpLoanRequest" });
app.MapControllerRoute(
    name: "TransactionEL_LoanAppr",
    pattern: "Transaction/EmployeeLoan/EmployeeLoanApprove",
    defaults: new { controller = "Loans", action = "EmployeeLoanApprove" });
app.MapControllerRoute(
    name: "TransactionEL_LoanDisb",
    pattern: "Transaction/EmployeeLoan/LoanDisbursed",
    defaults: new { controller = "Loans", action = "LoanDisbursed" });
app.MapControllerRoute(
    name: "TransactionEL_LoanDed",
    pattern: "Transaction/EmployeeLoan/LoanDeduction",
    defaults: new { controller = "Loans", action = "LoanDeduction" });

// Custom Routes for Transaction/EmployeeRequest
app.MapControllerRoute(
    name: "TransactionER_LeaveReq",
    pattern: "Transaction/EmployeeRequest/EmployeeLeaveRequest",
    defaults: new { controller = "Leaves", action = "EmployeeLeaveRequest" });
app.MapControllerRoute(
    name: "TransactionER_LeaveReqAppr",
    pattern: "Transaction/EmployeeRequest/EmployeeLeaveRequestApproval",
    defaults: new { controller = "Leaves", action = "EmployeeLeaveRequestApproval" });

// Custom Routes for Transaction/SalaryProcess
var spPayrollActions = new[] { "FuelPrices", "EmployeeAttendanceProccess", "CodCommission", "ReturnCodCommission", "CashCommission", "OverLandCommission", "CommissionProcess", "SalariesProcess", "SalaryReprocess", "DeathCompensation", "SalaryVouchers", "LeaveProcess", "ExcludeCodCN" };
foreach(var spAction in spPayrollActions)
{
    app.MapControllerRoute(
        name: $"TransactionSP_{spAction}",
        pattern: $"Transaction/SalaryProcess/{spAction}",
        defaults: new { controller = "Payroll", action = spAction });
}

var spClosingActions = new[] { "CommissionUnlock", "UnlockSalary", "CloseProcesses" };
foreach(var spAction in spClosingActions)
{
    app.MapControllerRoute(
        name: $"TransactionSP_{spAction}",
        pattern: $"Transaction/SalaryProcess/{spAction}",
        defaults: new { controller = "Closing", action = spAction });
}

app.MapControllerRoute(
    name: "TransactionSP_TagCommission",
    pattern: "Transaction/SalaryProcess/TagCommission",
    defaults: new { controller = "Commission", action = "TagCommission" });

// Custom Routes for Setup/Locations
var setupLocationsActions = new[] { "Countries", "Provinces", "RegionalZones", "Cities" };
foreach(var action in setupLocationsActions)
{
    app.MapControllerRoute(
        name: $"SetupLoc_{action}",
        pattern: $"Setup/Locations/{action}",
        defaults: new { controller = "Setup", action = action });
}
app.MapControllerRoute(
    name: "SetupLoc_UserLocation",
    pattern: "Setup/Locations/UserLocation",
    defaults: new { controller = "Admin", action = "UserLocation" });

// Custom Routes for Setup/Hr
var setupHrActions = new[] { "Jobs", "Departments", "EmployeeTypes", "LeaveStructures", "AttendanceRules", "Divisions", "DepartmentStrength", "DefineHRHierarchy" };
foreach(var action in setupHrActions)
{
    app.MapControllerRoute(
        name: $"SetupHr_{action}",
        pattern: $"Setup/Hr/{action}",
        defaults: new { controller = "Setup", action = action });
}

// Custom Routes for Setup/Payroll
var setupPayrollActions = new[] { "LoanTypes", "CommissionRates", "Shifts", "Taxes", "Routes", "GazettedHolidays", "SalaryBanks", "CommissionEligibility" };
foreach(var action in setupPayrollActions)
{
    app.MapControllerRoute(
        name: $"SetupPayroll_{action}",
        pattern: $"Setup/Payroll/{action}",
        defaults: new { controller = "Setup", action = action });
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Hangfire dashboard (requires authentication — admin only)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

// SignalR hub
app.MapHub<CommissionProgressHub>("/hubs/commission-progress");
app.MapHub<LCS_HR_MVC.Hubs.DataMigrationHub>("/hubs/data-migration");

// Monthly recurring job: 1 AM on the 26th of every month
// Wrapped in try-catch — Hangfire storage write can timeout if MySQL is under load at startup.
try
{
    RecurringJob.AddOrUpdate<ICommissionAutomationService>(
        "monthly-commission-automation",
        svc => svc.TriggerScheduledAsync(),
        "0 1 26 * *");
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "RecurringJob.AddOrUpdate failed at startup — job will be registered on next restart.");
}

// Every-6-hours config validation: verifies all commission/salary tables and restores if deleted.
try
{
    RecurringJob.AddOrUpdate<IDataSyncService>(
        "config-validation-job",
        svc => svc.RunSyncAsync(CancellationToken.None),
        "0 */6 * * *");
}
catch (Exception ex)
{
    app.Services.GetRequiredService<ILogger<Program>>()
        .LogWarning(ex, "[Hangfire] config-validation-job registration failed.");
}

// ── ORDER 1: Initialize AC_Test table name resolver ──────────────────────────
// Must be FIRST — before DbInitializer, CommissionTablesBootstrapper, and
// AcTestTablesBootstrapper so that IsTestMode is set for all subsequent checks.
{
    var useTestTables = app.Configuration
        .GetValue<bool>("CommissionSettings:UseTestTables");
    AcTestTableNames.Initialize(useTestTables);
}

// Ensure required tables exist (creates TakenLeaves and TotalLeaves if missing).
// Hard 30-second timeout so a MySQL metadata lock can never hang the entire startup.
try
{
    var connStr = app.Configuration.GetConnectionString("DefaultConnection")!;
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await DbInitializer.EnsureTablesExistAsync(connStr).WaitAsync(cts.Token);
}
catch (OperationCanceledException)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning("DbInitializer timed out after 30 s — MySQL may have a metadata lock. App will continue; some tables may be missing.");
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "DbInitializer: failed to ensure tables exist. Some features may be unavailable.");
}

// Ensure commission/salary config tables exist and contain baseline seed data.
// INSERT IGNORE semantics: existing HR-modified values are never overwritten.
// Hard 60-second timeout — ALTER TABLE on large log tables can take a few seconds.
try
{
    var connStr = app.Configuration.GetConnectionString("DefaultConnection")!;
    var logger  = app.Services.GetRequiredService<ILogger<Program>>();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await CommissionTablesBootstrapper.EnsureAllTablesAsync(connStr, logger).WaitAsync(cts.Token);
}
catch (OperationCanceledException)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning("[Bootstrap] CommissionTablesBootstrapper timed out after 60 s — some config tables may be missing.");
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "[Bootstrap] CommissionTablesBootstrapper failed — some config tables may be missing.");
}

// ── ORDER 4: AC_Test Tables Startup Check ────────────────────────────────────
// Runs only when UseTestTables = true.
// Checks each test table individually — skips existing ones.
// Creates missing ones using CREATE TABLE ... LIKE.
// (AcTestTableNames.Initialize already called at ORDER 1 above)
if (AcTestTableNames.IsTestMode)
{
    app.Services.GetRequiredService<ILogger<Program>>()
        .LogWarning(
            "[ACTest] TEST MODE ACTIVE — Commission inserts will go to _AC_Test tables. " +
            "Set CommissionSettings:UseTestTables=false for production.");

    try
    {
        var connStr = app.Configuration
            .GetConnectionString("DefaultConnection")!;
        var acLogger = app.Services
            .GetRequiredService<ILogger<Program>>();

        using var ctsAc = new CancellationTokenSource(
            TimeSpan.FromSeconds(60));

        await AcTestTablesBootstrapper
            .EnsureTestTablesAsync(connStr, acLogger)
            .WaitAsync(ctsAc.Token);
    }
    catch (OperationCanceledException)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(
            "[ACTest] Startup check timed out after 60s.");
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogError(ex,
            "[ACTest] Startup check failed. " +
            "Visit /ac-test to diagnose.");
    }
}
else
{
    app.Services.GetRequiredService<ILogger<Program>>()
        .LogInformation(
            "[ACTest] PRODUCTION MODE — Commission inserts will go to real tables.");
}

app.Run();

// ── Bootstrap helper: INSERT IGNORE all rows from a live table into local ─────
// Duplicate PKs are silently ignored — only genuinely new rows get inserted.
static long SyncTableFromLive(string liveConnStr, string localConnStr, string schema, string tableName)
{
    const int batchSize = 500;
    long totalInserted = 0;
    int offset = 0;

    using var liveConn = new MySqlConnection(liveConnStr);
    liveConn.Open();

    List<string>? colNames = null;

    while (true)
    {
        var batch = new System.Data.DataTable();
        using (var selectCmd = liveConn.CreateCommand())
        {
            selectCmd.CommandText = $"SELECT * FROM `{schema}`.`{tableName}` LIMIT {batchSize} OFFSET {offset}";
            using var adapter = new MySqlDataAdapter(selectCmd);
            adapter.Fill(batch);
        }

        if (batch.Rows.Count == 0) break;

        if (colNames == null)
            colNames = batch.Columns.Cast<System.Data.DataColumn>()
                            .Select(c => c.ColumnName).ToList();

        var colList   = string.Join(", ", colNames.Select(c => $"`{c}`"));
        var paramList = string.Join(", ", colNames.Select((_, i) => $"@p{i}"));
        var sql       = $"INSERT IGNORE INTO `{schema}`.`{tableName}` ({colList}) VALUES ({paramList})";

        using var localConn = new MySqlConnection(localConnStr);
        localConn.Open();

        foreach (System.Data.DataRow row in batch.Rows)
        {
            using var cmd = localConn.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < colNames.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", row[i] == DBNull.Value ? null : row[i]);
            totalInserted += cmd.ExecuteNonQuery();
        }

        offset += batch.Rows.Count;
        if (batch.Rows.Count < batchSize) break;
    }

    return totalInserted;
}
