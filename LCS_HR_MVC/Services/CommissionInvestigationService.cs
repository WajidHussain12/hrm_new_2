using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    /// <summary>
    /// Commission Investigation Center — full read-only analysis across 3 servers.
    ///
    /// KEY SCHEMA FACTS (verified on live DB 2026-04-02):
    ///   hr_city.Code           = '001'   (3-char city code)
    ///   hr_city.station_id     = '00592' (5-char, NOT numerically related to Code)
    ///   hr_city.FullName       = city name (NOT city_name)
    ///   hr_cash_consignments.Station_id → JOIN hr_city ON station_id, then use hc.Code
    ///   hr_cash_consignments.cour_id    = RouteCode (varchar 5)  NOT emp_no
    ///   hr_cod_consignments.Cour_id     = RouteCode (varchar 5)  NOT emp_no
    ///   hr_cod_consignments.Arivl_Dest  = hr_city.station_id
    ///   hr_commissionprocess grand total includes CASH_Economy_Booking + 80+ more columns
    /// </summary>
    public class CommissionInvestigationService : ICommissionInvestigationService
    {
        private readonly IDbConnectionFactory _factory;
        private readonly IConfiguration?      _config;
        private readonly ILogger<CommissionInvestigationService> _log;

        private const int PeriodStartDay = 21;
        private const int PeriodEndDay   = 20;

        // Grand total SQL (exact match to CommissionVerificationService — verified 2026-04-02)
        private const string GtSql = @"
(IFNULL(DOM_CREDIT,0)+IFNULL(LOCAL_CREDIT,0)+IFNULL(LOCAL_DLD,0)+IFNULL(PMCL,0)+
 IFNULL(DomesticDelivery,0)+IFNULL(INTL_CREDIT,0)+IFNULL(Porter,0)+IFNULL(COD,0)+
 IFNULL(OVERNIGHT,0)+IFNULL(YB1KG,0)+IFNULL(YB2KG,0)+IFNULL(YB5KG,0)+
 IFNULL(YB10KG,0)+IFNULL(YB15KG,0)+IFNULL(YB25KG,0)+IFNULL(FLAYER,0)+
 IFNULL(DETAIN,0)+IFNULL(OVERLAND,0)+IFNULL(PREPAID,0)+IFNULL(LOVELINE,0)+
 IFNULL(INTL_CASH,0)+IFNULL(OLE_Credit_Booking,0)+IFNULL(OLE_Dispatch_Proper,0)+
 IFNULL(OLE_Transit_Dispatch,0)+IFNULL(OLE_Delivery_OPS,0)+IFNULL(OLE_Delivery,0)+
 IFNULL(MOFA_OTO,0)+IFNULL(MOFA_OTD,0)+IFNULL(Rms_Cod_Booking,0)+
 IFNULL(AllInOne,0)+IFNULL(DocumnetCare,0)+IFNULL(MTD,0)+IFNULL(VAS,0)+
 IFNULL(IntlDox,0)+IFNULL(IntlEconomy,0)+IFNULL(IntlParcel,0)+
 IFNULL(ONUpto1kg,0)+IFNULL(ONAbove1kg,0)+IFNULL(ONUpto1kgRetailCOD,0)+
 IFNULL(ONAbove1kgRetailCOD,0)+IFNULL(EconomyRetail,0)+
 IFNULL(YB1KGRetail,0)+IFNULL(YB2KGRetail,0)+IFNULL(YB5KGRetail,0)+
 IFNULL(YB10KGRetail,0)+IFNULL(YB15KGRetail,0)+IFNULL(YB25KGRetail,0)+
 IFNULL(MyCollect,0)+IFNULL(Attestation,0)+
 IFNULL(CEB_UpTo_2Kg,0)+IFNULL(CEB_Above_2Kg,0)+
 IFNULL(Cor_Economy_Booking,0)+IFNULL(Cor_Ole_Booking,0)+
 IFNULL(CEB_Upto_2KG_Exis,0)+IFNULL(CEB_Upto_2KG_New,0)+
 IFNULL(CEB_Above_2Kg_Exis,0)+IFNULL(CEB_Above_2Kg_New,0)+
 IFNULL(ECON_Credit_Booking_Exis,0)+IFNULL(ECON_Credit_Booking_New,0)+
 IFNULL(OLE_CORP_Booking_Exis,0)+IFNULL(OLE_CORP_Booking_New,0)+
 IFNULL(Project_Local_Exis,0)+IFNULL(Project_Local_New,0)+
 IFNULL(Project_Domestic_Exis,0)+IFNULL(Project_Domestic_New,0)+
 IFNULL(CASH_EXP_BKG_UpTo_2Kg,0)+IFNULL(CASH_EXP_BKG_Above_2Kg,0)+
 IFNULL(CASH_Leop_BOX_Above_2Kg,0)+IFNULL(CASH_Economy_Booking,0)+
 IFNULL(CASH_OLE_Booking,0)+IFNULL(Insurance_Com,0)+
 IFNULL(Credit_Debit_Card,0)+IFNULL(ECommerce_Zero_COD,0)+
 IFNULL(Passport,0)+IFNULL(CNIC_Card,0)+IFNULL(Return_E_Com,0)+
 IFNULL(Pickup_Leopard,0)+IFNULL(COD_Bonus,0)+
 IFNULL(SOA,0)+IFNULL(Utility_Bill,0)+
 IFNULL(General_Light_Delivery,0)+IFNULL(General_Heavy_Delivery,0)+
 IFNULL(MTD_Delivery,0)+IFNULL(Giftwifts_Delivery,0)+
 IFNULL(Ecom_overall_SR_Bonus,0)
 -IFNULL(Retail_Deduction,0)-IFNULL(COD_Deduction,0))";

        public CommissionInvestigationService(
            IDbConnectionFactory factory,
            ILogger<CommissionInvestigationService> log,
            IConfiguration? config = null)
        {
            _factory = factory;
            _log     = log;
            _config  = config;
            _ = EnsureNotesTableAsync(); // fire-and-forget table bootstrap
        }

        // ── Connection helpers ────────────────────────────────────────────────

        /// <summary>MySQL bit(1) comes back as ulong — convert to bool? safely.</summary>
        private static bool? ToBool(dynamic v)
            => v is null ? (bool?)null : v is bool b ? b : ((ulong)v) != 0;

        private MySqlConnection OpenMain()
        {
            var conn = _factory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot open main DB connection.");
            conn.Open();
            return conn;
        }

        private async Task<(MySqlConnection? conn, string? error)> TryOpenExternalAsync(string key, string serverLabel)
        {
            try
            {
                var cs = _config?.GetConnectionString(key);
                if (string.IsNullOrWhiteSpace(cs))
                    return (null, $"{serverLabel} connection string '{key}' not configured.");
                var conn = new MySqlConnection(cs);
                await conn.OpenAsync();
                return (conn, null);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Cannot connect to {Server} ({Key})", serverLabel, key);
                return (null, ex.Message);
            }
        }

        private static (DateTime From, DateTime To) GetPeriod(int year, int month)
        {
            var from = new DateTime(year, month, PeriodStartDay).AddMonths(-1);
            var to   = new DateTime(year, month, PeriodEndDay);
            return (from, to);
        }

        /// <summary>
        /// Runs a single loader; on failure logs the error, appends to ErrorMessage, but does NOT
        /// flip IsConnected so that successfully-loaded data is still shown.
        /// </summary>
        private async Task RunLoader(Func<Task> loader, string loaderName,
            EmployeeCommissionInvestigationVm vm, string empNo)
        {
            try { await loader(); }
            catch (Exception ex)
            {
                _log.LogError(ex, "Server10 {Loader} failed for EmpNo={EmpNo}: {Msg}",
                    loaderName, empNo, ex.Message);
                // Append to error message (don't set IsConnected=false for partial failures)
                var note = $"[{loaderName}: {ex.Message}]";
                vm.S10Health.ErrorMessage = string.IsNullOrEmpty(vm.S10Health.ErrorMessage)
                    ? note
                    : vm.S10Health.ErrorMessage + " " + note;
            }
        }

        // ── Notes table bootstrap ─────────────────────────────────────────────

        private async Task EnsureNotesTableAsync()
        {
            try
            {
                // Use async open — synchronous conn.Open() inside fire-and-forget
                // can starve the thread pool if the connection pool is busy at startup.
                var conn = _factory.CreateConnection() as MySqlConnection
                    ?? throw new InvalidOperationException("Cannot open main DB connection.");
                await conn.OpenAsync();
                using (conn)
                {
                    await conn.ExecuteAsync(@"
                        CREATE TABLE IF NOT EXISTS `hr_commission_investigation_notes` (
                            `Id`           INT NOT NULL AUTO_INCREMENT,
                            `EmpNo`        VARCHAR(14) NOT NULL,
                            `Year`         INT NOT NULL,
                            `Month`        INT NOT NULL,
                            `CityCode`     VARCHAR(3) DEFAULT NULL,
                            `ActionType`   VARCHAR(30) NOT NULL DEFAULT 'Note',
                            `Notes`        TEXT NOT NULL,
                            `CreatedBy`    VARCHAR(100) NOT NULL,
                            `CreatedDate`  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            `Status`       VARCHAR(20) NOT NULL DEFAULT 'Open',
                            `ResolvedBy`   VARCHAR(100) DEFAULT NULL,
                            `ResolvedDate` DATETIME DEFAULT NULL,
                            `IsDeleted`    TINYINT(1) NOT NULL DEFAULT '0',
                            PRIMARY KEY (`Id`),
                            KEY `idx_inv_emp_ym` (`EmpNo`, `Year`, `Month`)
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "EnsureNotesTableAsync failed — notes table may not exist.");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // DROPDOWNS
        // ═════════════════════════════════════════════════════════════════════

        public async Task<List<int>> GetAvailableYearsAsync()
        {
            try
            {
                using var conn = OpenMain();
                var years = await conn.QueryAsync<int>(
                    "SELECT DISTINCT Year FROM hr_commissionprocess ORDER BY Year DESC");
                var list = years.ToList();
                if (!list.Contains(DateTime.Now.Year)) list.Insert(0, DateTime.Now.Year);
                return list;
            }
            catch { return new List<int> { DateTime.Now.Year }; }
        }

        public async Task<List<(string Code, string Name)>> GetCitiesAsync()
        {
            try
            {
                using var conn = OpenMain();
                var rows = await conn.QueryAsync<(string Code, string Name)>(
                    "SELECT Code, FullName FROM hr_city WHERE Code IS NOT NULL AND Code <> '' ORDER BY FullName");
                return rows.ToList();
            }
            catch { return new List<(string, string)>(); }
        }

        // ═════════════════════════════════════════════════════════════════════
        // SEARCH
        // ═════════════════════════════════════════════════════════════════════

        public async Task<List<EmpInvestigationSearchRow>> SearchEmployeesAsync(
            CommissionInvestigationFilter filter)
        {
            try
            {
                using var conn = OpenMain();
                var no = (filter.EmpNo ?? "").Trim();

                if (string.IsNullOrEmpty(no))
                    return new List<EmpInvestigationSearchRow>();

                // DB columns (Emp_No, RouteCode, citycode) are latin1_swedish_ci.
                // C# MySql.Data sends string parameters as utf8mb4 by default which causes
                // a collation mismatch on LIKE comparisons. Fix: CONVERT(@param USING latin1).
                //
                // LIKE '%12385%' matches '00000000012385' — no leading-zero stripping needed.
                //
                // UNION: employees WITH route codes first, then employees WITHOUT route codes
                // but who have commission records (e.g. emp 45194 — citycode from hr_commissionprocess).
                var sql = @"
                    SELECT DISTINCT
                        erc.Emp_No        AS EmpNo,
                        epd.NAME          AS Name,
                        epd.EMP_STATUS    AS EmpStatus,
                        epd.EMPLOYEE_TYPE AS EmployeeType,
                        erc.RouteCode     AS RouteCode,
                        erc.citycode      AS CityCode,
                        hc.FullName       AS CityName,
                        hc.station_id     AS StationId
                    FROM hr_employeeroutecode erc
                    INNER JOIN hr_employeepersonaldetail epd ON epd.EMP_NO = erc.Emp_No
                    LEFT JOIN hr_city hc ON hc.Code = erc.citycode
                    WHERE erc.Emp_No LIKE CONVERT(@NoLike USING latin1) COLLATE latin1_swedish_ci
                    UNION
                    SELECT DISTINCT
                        epd.EMP_NO        AS EmpNo,
                        epd.NAME          AS Name,
                        epd.EMP_STATUS    AS EmpStatus,
                        epd.EMPLOYEE_TYPE AS EmployeeType,
                        NULL              AS RouteCode,
                        cp.citycode       AS CityCode,
                        hc.FullName       AS CityName,
                        hc.station_id     AS StationId
                    FROM hr_employeepersonaldetail epd
                    INNER JOIN hr_commissionprocess cp ON cp.emp_no = epd.EMP_NO
                        AND cp.Year = @Year AND cp.Month = @Month
                    LEFT JOIN hr_city hc ON hc.Code = cp.citycode
                    WHERE epd.EMP_NO LIKE CONVERT(@NoLike USING latin1) COLLATE latin1_swedish_ci
                      AND NOT EXISTS (SELECT 1 FROM hr_employeeroutecode erc2 WHERE erc2.Emp_No = epd.EMP_NO)
                    ORDER BY Name
                    LIMIT 200";

                var rawRows = (await conn.QueryAsync<dynamic>(sql, new {
                    NoLike = $"%{no}%",
                    Year   = filter.Year,
                    Month  = filter.Month
                })).ToList();

                if (!rawRows.Any()) return new List<EmpInvestigationSearchRow>();

                // Group by EmpNo (employee may have multiple route codes)
                var empNos = rawRows.Select(r => (string)r.EmpNo).Distinct().ToList();

                // Step 2: get final commission status for these employees
                var commStatus = new Dictionary<string, (bool hasRecord, decimal total, bool? eligible)>();
                if (empNos.Any())
                {
                    // CONVERT(...USING latin1) on each param — columns are latin1_swedish_ci
                    // but MySql.Data sends string params as utf8mb4 causing collation errors.
                    var convertList = string.Join(",", empNos.Select((_, i) => $"CONVERT(@p{i} USING latin1)"));
                    var paramDict = new DynamicParameters();
                    paramDict.Add("Year",  filter.Year);
                    paramDict.Add("Month", filter.Month);
                    for (int i = 0; i < empNos.Count; i++) paramDict.Add($"p{i}", empNos[i]);

                    var commRows = await conn.QueryAsync<dynamic>($@"
                        SELECT cp.emp_no AS EmpNo, {GtSql} AS GrandTotal,
                               elig.IsEligible
                        FROM hr_commissionprocess cp
                        LEFT JOIN hr_empcommissioneligibility elig
                            ON elig.Emp_No = cp.emp_no
                        WHERE cp.Year = @Year AND cp.Month = @Month
                          AND cp.emp_no IN ({convertList})", paramDict);

                    foreach (var r in commRows)
                    {
                        string en = (string)r.EmpNo;
                        commStatus[en] = (true, (decimal)r.GrandTotal, ToBool(r.IsEligible));
                    }

                    // Also check eligibility for those without commission record
                    var eligRows = await conn.QueryAsync<dynamic>($@"
                        SELECT Emp_No AS EmpNo, IsEligible
                        FROM hr_empcommissioneligibility
                        WHERE Emp_No IN ({convertList})", paramDict);
                    foreach (var r in eligRows)
                    {
                        string en = (string)r.EmpNo;
                        if (!commStatus.ContainsKey(en))
                            commStatus[en] = (false, 0, ToBool(r.IsEligible));
                    }
                }

                // Build result rows
                var grouped = rawRows
                    .GroupBy(r => (string)r.EmpNo)
                    .Select(g => {
                        var first = g.First();
                        var en    = (string)first.EmpNo;
                        commStatus.TryGetValue(en, out var cs);

                        bool?   isElig = cs.eligible;
                        bool    hasRec = cs.hasRecord;
                        decimal total  = cs.total;

                        string status = (isElig == false)
                            ? "NotEligible"
                            : hasRec
                                ? (total > 0 ? "Processed" : "Partial")
                                : "Missing";

                        return new EmpInvestigationSearchRow {
                            EmpNo            = en,
                            Name             = (string)first.Name,
                            EmpStatus        = (string?)first.EmpStatus ?? "A",
                            CityCode         = (string)first.CityCode,
                            CityName         = (string?)first.CityName,
                            StationId        = (string?)first.StationId,
                            RouteCodes       = g.Select(r => (string)r.RouteCode).Distinct().ToList(),
                            IsEligible       = isElig,
                            HasFinalRecord   = hasRec,
                            FinalCommission  = hasRec ? total : null,
                            CommissionStatus = status
                        };
                    }).ToList();

                return grouped;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "SearchEmployeesAsync failed");
                throw; // propagate so controller can show error message to user
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // FULL INVESTIGATION
        // ═════════════════════════════════════════════════════════════════════

        public async Task<EmployeeCommissionInvestigationVm> GetInvestigationAsync(
            string empNo, int year, int month, string cityCode)
        {
            var vm = new EmployeeCommissionInvestigationVm {
                EmpNo    = empNo,
                Year     = year,
                Month    = month,
                CityCode = cityCode ?? ""
            };
            var (from, to) = GetPeriod(year, month);
            vm.PeriodFrom  = from;
            vm.PeriodTo    = to;

            // ── All 3 servers run in PARALLEL ─────────────────────────────────
            // Server 10 inner queries also parallelized (each gets its own connection).
            // Dependency: RouteCodes must finish before CashCns/CodCns/ReturnCod.
            await Task.WhenAll(
                LoadServer10Async(vm, empNo, year, month, from, to),
                LoadServer6Async(vm, from, to),
                LoadServer7Async(vm, year, month)
            );

            // ── Auto-detect issues ────────────────────────────────────────────
            DetectIssues(vm);

            // ── Determine overall commission status ───────────────────────────
            vm.CommissionStatus = DetermineCommissionStatus(vm);

            // ── Build SQL query panel ─────────────────────────────────────────
            BuildSqlQueryPanel(vm, empNo, year, month, cityCode, from, to);

            return vm;
        }

        private async Task LoadServer10Async(EmployeeCommissionInvestigationVm vm,
            string empNo, int year, int month, DateTime from, DateTime to)
        {
            // Step 1 — verify connection first, then load employee master
            try
            {
                using var c0 = OpenMain();       // throws only on connection failure
                vm.S10Health.IsConnected = true; // connection succeeded
                try
                {
                    await LoadEmployeeMasterAsync(c0, vm);
                }
                catch (Exception qex)
                {
                    // Query failed (e.g. schema mismatch) — connection IS available, just log
                    _log.LogError(qex, "LoadEmployeeMasterAsync failed for EmpNo={EmpNo}", empNo);
                    vm.S10Health.HasQueryError = true;
                    vm.S10Health.ErrorMessage  = $"Employee master query failed: {qex.Message}";
                    if (string.IsNullOrEmpty(vm.Name)) vm.Name = empNo;
                }
            }
            catch (Exception ex)
            {
                vm.S10Health.IsConnected = false;
                vm.S10Health.ErrorMessage = $"Cannot connect to Server 10: {ex.Message}";
                _log.LogError(ex, "Server 10 connection failed for EmpNo={EmpNo}", empNo);
                return; // no point running other loaders
            }

            // Step 2 — RouteCodes must complete before CashCns/CodCns/ReturnCod (they need route list)
            {
                using var c1 = OpenMain();
                await RunLoader(() => LoadRouteCodesAsync(c1, vm), "LoadRouteCodes", vm, empNo);
            }

            // Step 3 — all remaining loaders run in PARALLEL (each gets its own connection)
            // They all write to different vm properties — no race conditions.
            await Task.WhenAll(
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadEligibilityAndAttendanceAsync(c, vm, year, month),
                        "LoadEligibility", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadCashCnsAsync(c, vm, from, to), "LoadCashCns", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadCodCnsAsync(c, vm, year, month), "LoadCodCns", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadReturnCodAsync(c, vm, year, month), "LoadReturnCod", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadFinalCommissionAsync(c, vm, year, month),
                        "LoadFinalCommission", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadInvestigationNotesAsync(c, vm, year, month),
                        "LoadNotes", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadAdjustmentsAsync(c, vm, empNo, year, month),
                        "LoadAdjustments", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadSrBonusAsync(c, vm, empNo, year, month),
                        "LoadSrBonus", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadLocationAndStationAsync(c, vm),
                        "LoadLocation", vm, empNo);
                })
            );

            // Step 3b — RBI/VAS/CODReturn depend on ResolvedStationId (must run after Step 3)
            await Task.WhenAll(
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadRbiDetailAsync(c, vm, year, month),
                        "LoadRbiDetail", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadVasGeneralAsync(c, vm, year, month),
                        "LoadVasGeneral", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadCodReturnDetailAsync(c, vm, year, month),
                        "LoadCodReturn", vm, empNo);
                })
            );

            // Step 4 — UsedRatePolicies reads CashCns (must run after CashCns completes)
            //        — AllRatePolicies loads all policies regardless of CNs
            await Task.WhenAll(
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadUsedRatePoliciesAsync(c, vm), "LoadUsedRatePolicies", vm, empNo);
                }),
                Task.Run(async () => {
                    using var c = OpenMain();
                    await RunLoader(() => LoadAllRatePoliciesAsync(c, vm), "LoadAllRatePolicies", vm, empNo);
                })
            );
        }

        private async Task LoadServer6Async(EmployeeCommissionInvestigationVm vm, DateTime from, DateTime to)
        {
            var (s6conn, s6err) = await TryOpenExternalAsync("LHR_Billing", "Server 6 Billing");
            vm.S6Health.IsConnected = s6conn != null;
            vm.S6Health.ErrorMessage = s6err;
            vm.S6Connected = s6conn != null;
            vm.S6Error     = s6err;
            if (s6conn != null)
            {
                using (s6conn)
                    await LoadServer6DataAsync(s6conn, vm, from, to);
            }
        }

        private async Task LoadServer7Async(EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            var (s7conn, s7err) = await TryOpenExternalAsync("MIS", "Server 7 MIS");
            vm.S7Health.IsConnected = s7conn != null;
            vm.S7Health.ErrorMessage = s7err;
            vm.S7Connected = s7conn != null;
            vm.S7Error     = s7err;
            if (s7conn != null)
            {
                using (s7conn)
                    await LoadServer7DataAsync(s7conn, vm, year, month);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SERVER 10 — individual loaders
        // ─────────────────────────────────────────────────────────────────────

        private static async Task LoadEmployeeMasterAsync(MySqlConnection conn, EmployeeCommissionInvestigationVm vm)
        {
            var sql = @"
                SELECT
                    epd.EMP_NO        AS EmpNo,
                    epd.NAME          AS Name,
                    epd.EMP_STATUS    AS EmpStatus,
                    epd.EMPLOYEE_TYPE AS EmployeeType,
                    epd.APPOINT_DATE  AS AppointDate,
                    epd.LEFT_DATE     AS LeftDate,
                    et.FullName       AS EmployeeTypeName,
                    epd.P_CITY_CODE   AS PCityCode,
                    hc.FullName       AS PCityName,
                    epd.JobTypeId     AS JobTypeId,
                    j.FullName        AS JobTitle
                FROM hr_employeepersonaldetail epd
                LEFT JOIN hr_employeetype et ON et.Code  = epd.EMPLOYEE_TYPE
                LEFT JOIN hr_city         hc ON hc.Code  = epd.P_CITY_CODE
                LEFT JOIN hr_jobs          j ON j.Code   = epd.JobTypeId
                WHERE epd.EMP_NO = @EmpNo
                LIMIT 1";

            dynamic? row;
            try
            {
                row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { EmpNo = vm.EmpNo });
            }
            catch
            {
                // Fallback: minimal query without optional JOINs (hr_jobs / hr_city might not exist)
                row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT epd.EMP_NO AS EmpNo, epd.NAME AS Name, epd.EMP_STATUS AS EmpStatus,
                           epd.EMPLOYEE_TYPE AS EmployeeType, epd.APPOINT_DATE AS AppointDate,
                           epd.LEFT_DATE AS LeftDate,
                           et.FullName AS EmployeeTypeName
                    FROM hr_employeepersonaldetail epd
                    LEFT JOIN hr_employeetype et ON et.Code = epd.EMPLOYEE_TYPE
                    WHERE epd.EMP_NO = @EmpNo LIMIT 1",
                    new { EmpNo = vm.EmpNo });
            }

            if (row != null)
            {
                vm.Name             = (string?)row.Name             ?? vm.EmpNo;
                vm.EmpStatus        = (string?)row.EmpStatus        ?? "";
                vm.EmployeeType     = (string?)row.EmployeeType;
                vm.EmployeeTypeName = (string?)row.EmployeeTypeName;
                vm.AppointDate      = (DateTime?)row.AppointDate;
                vm.LeftDate         = (DateTime?)row.LeftDate;

                // Optional fields (may not be in fallback row)
                var dict = (IDictionary<string, object>)row;
                vm.JobTypeId   = dict.TryGetValue("JobTypeId",  out var jt)  ? jt?.ToString()  : null;
                vm.Designation = dict.TryGetValue("JobTitle",   out var jd)  ? jd?.ToString()  : null;
                var pcn        = dict.TryGetValue("PCityName",  out var pcnv) ? pcnv?.ToString() : null;
                if (string.IsNullOrEmpty(vm.CityName) && !string.IsNullOrEmpty(pcn))
                    vm.CityName = pcn;
            }
        }

        private static async Task LoadRouteCodesAsync(MySqlConnection conn, EmployeeCommissionInvestigationVm vm)
        {
            var sql = @"
                SELECT
                    erc.RouteCode         AS RouteCode,
                    erc.citycode          AS CityCode,
                    hc.FullName           AS CityName,
                    hc.station_id         AS StationId,
                    erc.LocationId        AS LocationId,
                    erc.FromDate          AS FromDate,
                    erc.ToDate            AS ToDate,
                    erc.CodeType          AS CodeType,
                    ct.Name               AS CodeTypeName,
                    (erc.RBIExclude + 0)  AS RBIExclude,
                    'Main'                AS FoundOn
                FROM hr_employeeroutecode erc
                LEFT JOIN hr_city hc ON hc.Code = erc.citycode
                LEFT JOIN couriercodetype ct ON ct.Id = erc.CodeType
                WHERE erc.Emp_No = @EmpNo
                  AND (@City = '' OR erc.citycode = @City)
                ORDER BY erc.FromDate DESC";

            var rows = await conn.QueryAsync<RouteCodeInfo>(sql, new { EmpNo = vm.EmpNo, City = vm.CityCode });
            vm.RouteCodes = rows.ToList();

            // Set city info from first active route
            var active = vm.RouteCodes.FirstOrDefault(r => r.IsActive);
            if (active != null)
            {
                if (string.IsNullOrEmpty(vm.CityCode)) vm.CityCode = active.CityCode;
                vm.CityName  ??= active.CityName;
                vm.StationId ??= active.StationId;
            }
        }

        private static async Task LoadEligibilityAndAttendanceAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            // Eligibility — hr_empcommissioneligibility
            // Column is Emp_no (lowercase n) — MySQL column names case-insensitive so @EmpNo works.
            // CommissionId=2 (Ground Leopard), 3 (In House), 4 (Express Center) — from couriercodetype.
            // Delivery & Pickup (CodeType=1) employees have NO record here — eligible by default via per-shipment rates.
            var eligRows = await conn.QueryAsync<dynamic>(@"
                SELECT e.IsEligible, e.CommissionId, ct.Name AS CategoryName
                FROM hr_empcommissioneligibility e
                LEFT JOIN couriercodetype ct ON ct.Id = e.CommissionId
                WHERE e.Emp_no = @EmpNo",
                new { EmpNo = vm.EmpNo });

            var eligList = eligRows.ToList();
            if (eligList.Any())
            {
                // Overall eligibility = eligible if ANY category is eligible
                vm.IsEligible = eligList.Any(r => ToBool(r.IsEligible) == true) ? true
                              : eligList.All(r => ToBool(r.IsEligible) == false) ? false
                              : null;
                vm.EligibilityBreakdown = eligList.Select(r => new EligibilityCategory {
                    CommissionId = (int)r.CommissionId,
                    CategoryName = (string?)r.CategoryName ?? $"Category {r.CommissionId}",
                    IsEligible   = ToBool(r.IsEligible)
                }).ToList();
            }
            // If no record: check if employee has per-shipment route (CodeType=1 D&P)
            // — those employees don't need an eligibility record; commission flows via hr_cash_consignments

            // Commission categories based on CodeType
            BuildCommissionCategories(vm);

            // Attendance — safe query with fallback column names
            try
            {
                var attRow = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT *
                    FROM hr_employeeattendanceprocess
                    WHERE Emp_No = @EmpNo AND Year = @Year AND Month = @Month
                    LIMIT 1",
                    new { EmpNo = vm.EmpNo, Year = year, Month = month });

                if (attRow != null)
                {
                    var dict = (IDictionary<string, object>)attRow;
                    vm.Attendance = new AttendanceSummary {
                        IsProcessed = true,
                        TotalDays   = GetInt(dict, "TotalDays"),
                        WorkedDays  = GetInt(dict, "WorkedDays"),
                        AbsentDays  = GetInt(dict, "AbsentDays", "AbsenceDays"),
                        SundayDays  = GetInt(dict, "SundayDays", "Sundays"),
                        Adjustments = GetInt(dict, "Adjustments", "AdjustmentDays"),
                        Comments    = GetStr(dict, "Comments")
                    };
                }
            }
            catch (Exception) { /* attendance table columns may vary — skip gracefully */ }
        }

        /// <summary>
        /// Determines applicable commission categories for display in Eligibility &amp; Attendance tab,
        /// based on the employee's CodeType (couriercodetype).
        /// Database verified 2026-04-04:
        ///   CodeType=1  Delivery &amp; Pickup     → Per-shipment cash commissions (Overnight, YB, Flyer, Economy, Overland, COD…)
        ///   CodeType=2  Ground Leopard         → Per-shipment + eligibility table (CommissionId=2)
        ///   CodeType=3  In House               → Per-shipment + eligibility table (CommissionId=3)
        ///   CodeType=4  Express Center         → Per-shipment + eligibility table (CommissionId=4)
        ///   CodeType=5  Pickup Leopards        → Per-shipment cash only
        ///   CodeType=6  Delivery Leopards      → Delivery-focused (General Light/Heavy, MTD, Giftwifts)
        ///   CodeType=7  Cargo Officer          → OLE commissions (Credit Booking, Dispatch, Delivery OPS)
        ///   CodeType=10 SALES                  → Credit booking + corporate rates
        ///   CodeType=11 RECOVERY               → COD return commissions
        /// </summary>
        private static void BuildCommissionCategories(EmployeeCommissionInvestigationVm vm)
        {
            var codeType = vm.RouteCodes.FirstOrDefault(r => r.IsActive)?.CodeType
                        ?? vm.RouteCodes.FirstOrDefault()?.CodeType;

            // No route code — show probable categories based on EmployeeType
            if (codeType == null)
            {
                // Zonal/Permanent employees are typically CodeType 1 (Delivery & Pickup) or 2 (Ground Leopard)
                // Show a generic entry so the Rate Categories section appears
                vm.CommissionCategories.Add(new CommissionCategoryInfo {
                    Category    = "Commission Category — Route Code Required",
                    Description = $"Employee Type: {vm.EmployeeTypeName ?? vm.EmployeeType ?? "Unknown"}. Route Code assign hone ke baad actual CodeType se exact categories determine hongi. Typically Zonal/Permanent employees CodeType 1 (Delivery & Pickup) ya CodeType 2 (Ground Leopard) mein hote hain.",
                    RateDisplay = "N/A — Route Code assign karo",
                    Source      = "hr_employeeroutecode.CodeType → hr_commissionpolicy",
                    Icon        = "fa-question-circle"
                });
                return;
            }

            var cats = new List<CommissionCategoryInfo>();

            // Per-shipment cash commissions — applicable to CodeType 1,2,3,4,5
            if (new[] {1,2,3,4,5}.Contains(codeType.Value))
            {
                cats.Add(new CommissionCategoryInfo {
                    Category = "Cash Delivery Commissions",
                    Description = "Per-shipment commission on Overnight, Yellow Box (1-25KG), Flyer, Economy, Prepaid, Giftwift, International",
                    RateDisplay = "Rs 1.50 – 5.00 per shipment (varies by type & zone)",
                    Source = "hr_cash_consignments → hr_commissionpolicy",
                    Icon = "fa-shipping-fast"
                });
                cats.Add(new CommissionCategoryInfo {
                    Category = "Overland Commission",
                    Description = "Per-shipment for Overland deliveries (Zone A/B/C/D)",
                    RateDisplay = "Rs 5.00 per shipment",
                    Source = "hr_cash_consignments → hr_commissionprocess.OVERLAND",
                    Icon = "fa-truck"
                });
                cats.Add(new CommissionCategoryInfo {
                    Category = "COD Commission",
                    Description = "COD delivery bonus + deductions",
                    RateDisplay = "Varies — includes COD_Bonus and COD_Deduction",
                    Source = "hr_codcommission / hr_commissionprocess.COD",
                    Icon = "fa-hand-holding-usd"
                });
            }

            // Corporate / Credit booking — CodeType 4 (Express Center), 10 (SALES)
            if (new[] {4,10}.Contains(codeType.Value))
            {
                cats.Add(new CommissionCategoryInfo {
                    Category = "Corporate Credit Booking (Existing Clients — Old Rate)",
                    Description = "Commission on bookings from existing corporate clients (IsPercent=True — % of billed amount)",
                    RateDisplay = "Express ≤2KG: 5.5%, Above 2KG: 1.75%; Economy: 1.5%; Overland: 1.75%",
                    Source = "hr_commissionpolicy RateID 84,86,88,90 → hr_commissionprocess",
                    Icon = "fa-building"
                });
                cats.Add(new CommissionCategoryInfo {
                    Category = "Corporate Credit Booking (New Acquisition — New Rate)",
                    Description = "Higher % rate for newly acquired corporate clients (IsPercent=True)",
                    RateDisplay = "Express ≤2KG: 10%, Above 2KG: 2.5%; Economy: 2%; Overland: 2%",
                    Source = "hr_commissionpolicy RateID 85,87,89,91 → hr_commissionprocess",
                    Icon = "fa-user-plus"
                });
            }

            // Local/Domestic credit booking — CodeType 3 (In House), 4
            if (new[] {3,4}.Contains(codeType.Value))
            {
                cats.Add(new CommissionCategoryInfo {
                    Category = "Domestic / Local Credit Booking",
                    Description = "Commission on domestic and local credit bookings",
                    RateDisplay = "Domestic: Rs 2.00/CN, Local: Rs 1.00/CN, Intl: Rs 50/CN",
                    Source = "hr_commissionpolicy RateID 6,7,8 → hr_commissionprocess",
                    Icon = "fa-file-invoice"
                });
            }

            // OLE commissions — CodeType 7 (Cargo Officer), 2 (Ground Leopard)
            if (new[] {2,7}.Contains(codeType.Value))
            {
                cats.Add(new CommissionCategoryInfo {
                    Category = "OLE Commissions",
                    Description = "OLE Credit Booking, Dispatch Proper, Transit Dispatch, Delivery OPS, Delivery",
                    RateDisplay = "Rs 0.10 – 0.20 per KG (PerKG rate type)",
                    Source = "hr_olecommissionprocess (Server 7 MIS)",
                    Icon = "fa-boxes"
                });
            }

            // Delivery-specific — CodeType 6 (Delivery Leopards)
            if (codeType.Value == 6)
            {
                cats.Add(new CommissionCategoryInfo {
                    Category = "Delivery Commissions (Light/Heavy/MTD/Giftwifts)",
                    Description = "General Light Delivery, General Heavy Delivery, MTD Delivery, Giftwifts Delivery",
                    RateDisplay = "Light: Rs 4, Heavy: Rs 5, MTD: Rs 10, Giftwifts: Rs 25 per shipment",
                    Source = "hr_commissionprocess columns: General_Light_Delivery, General_Heavy_Delivery",
                    Icon = "fa-dolly"
                });
            }

            // COD Return — applies to CodeType 3 (In House), 5 (Pickup Leopards)
            if (new[] {3,5}.Contains(codeType.Value))
            {
                cats.Add(new CommissionCategoryInfo {
                    Category = "Return COD (Shipments Returned)",
                    Description = "Commission on returned COD shipments in slabs (up to 3K, 3K-10K, above 10K)",
                    RateDisplay = "Rs 2.50 / Rs 1.50 / Rs 0.50 per shipment (slab-based)",
                    Source = "hr_codreturncommissionprocess → hr_commissionprocess",
                    Icon = "fa-undo"
                });
            }

            // Cash booking — CodeType 1,2,3,5
            if (new[] {1,2,3,5}.Contains(codeType.Value))
            {
                cats.Add(new CommissionCategoryInfo {
                    Category = "Cash Express / Economy Booking",
                    Description = "Cash booking commission (Express up to 2KG, above 2KG, Leopard Box, Economy) — IsPercent=True (% of revenue)",
                    RateDisplay = "Express ≤2KG: 5%, Above 2KG: 1.5%, Leopard Box: 3%, Economy: 3%",
                    Source = "hr_commissionpolicy RateID 79-82 → hr_commissionprocess.CASH_Economy_Booking",
                    Icon = "fa-cash-register"
                });
            }

            vm.CommissionCategories = cats;
        }

        private static async Task LoadCashCnsAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm,
            DateTime from, DateTime to)
        {
            // hr_cash_consignments uses cour_id (RouteCode) — NOT Emp_No.
            // Table confirmed columns: cn_number, billing_date, Shipment_id, Gross_Amount,
            //   TotalCommission, Criteria, cour_id, Station_id (no Emp_No, no RateId column).
            if (!vm.RouteCodes.Any())
            {
                vm.NoCnReason = "hr_cash_consignments aur hr_cod_consignments mein CN records cour_id (Route Code) se query hote hain — Emp_No se nahi. Is employee ka koi Route Code assign nahi hai, isliye koi CN data retrieve nahi ho sakta. Pehle Route Code assign karein.";
                return;
            }

            var routeCodes = vm.RouteCodes.Select(r => r.RouteCode).Distinct().ToList();
            var rcParam    = string.Join(",", routeCodes.Select((_, i) => $"@rc{i}"));
            var p          = new DynamicParameters();
            p.Add("From", from);
            p.Add("To",   to);
            for (int i = 0; i < routeCodes.Count; i++) p.Add($"rc{i}", routeCodes[i]);

            var sql = $@"
                SELECT
                    cc.cn_number                                              AS CnNumber,
                    cc.billing_date                                           AS BillingDate,
                    CAST(cc.Shipment_id AS UNSIGNED)                         AS ShipmentTypeId,
                    IFNULL(sc.ShipmentType, cc.Shipment_id)                  AS ShipmentLabel,
                    IFNULL(cc.Billing_Type, 'CASH')                          AS BillingCategory,
                    IFNULL(cc.Gross_Amount, 0)                               AS BilledAmount,
                    IFNULL(cc.TotalCommission, 0)                            AS CommissionAmount,
                    NULL                                                      AS RateId,
                    cc.Criteria                                               AS Criteria,
                    cc.cour_id                                                AS RouteCode,
                    cc.Station_id                                             AS StationId
                FROM hr_cash_consignments cc
                LEFT JOIN shipment_codes sc ON sc.ShipmentTypeId = CAST(cc.Shipment_id AS UNSIGNED)
                WHERE cc.cour_id IN ({rcParam})
                  AND cc.billing_date BETWEEN @From AND @To
                ORDER BY cc.billing_date DESC
                LIMIT 3000";

            var rows = await conn.QueryAsync<CashCommissionCnDetail>(sql, p);
            vm.CashCns   = rows.ToList();
            vm.CashTotal = vm.CashCns.Sum(r => r.CommissionAmount);

            // Billing_Type breakdown — run a fast aggregate query (indexed on cour_id+billing_date)
            // CASH = normal deliveries (also in S6 billing_details)
            // Retail COD = COD deliveries NOT in S6 billing_details — explains count difference
            try
            {
                var btSql = $@"
                    SELECT IFNULL(Billing_Type,'CASH') AS BillingType, COUNT(*) AS Count
                    FROM hr_cash_consignments
                    WHERE cour_id IN ({rcParam})
                      AND billing_date BETWEEN @From AND @To
                    GROUP BY Billing_Type
                    ORDER BY COUNT(*) DESC";
                var btRows = await conn.QueryAsync<BillingTypeCount>(btSql, p);
                vm.CashBillingTypes = btRows.ToList();
            }
            catch { /* column may not exist on older schema */ }
        }

        private static async Task LoadCodCnsAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            if (!vm.RouteCodes.Any()) return;

            // COD CNs are identified by RouteCode (Cour_id), not emp_no
            var routeList = string.Join(",",
                vm.RouteCodes.Select((r, i) => $"@rc{i}"));
            var p = new DynamicParameters();
            p.Add("Year",  year);
            p.Add("Month", month);
            for (int i = 0; i < vm.RouteCodes.Count; i++)
                p.Add($"rc{i}", vm.RouteCodes[i].RouteCode);

            var sql = $@"
                SELECT
                    hcc.CN_Number        AS CnNumber,
                    hcc.Cour_date        AS CourDate,
                    hcc.Delivery_date    AS DeliveryDate,
                    IFNULL(hcc.DateDif,0)         AS DateDif,
                    CASE WHEN IFNULL(hcc.DateDif,0) <= 0 THEN 1 ELSE 0 END AS IsOnTime,
                    IFNULL(hcc.ComAmnt, 0)        AS CommissionAmount,
                    hcc.Reason           AS Reason,
                    hcc.Cour_id          AS RouteCode
                FROM hr_cod_consignments hcc
                WHERE hcc.Cyear = @Year AND hcc.CMonth = @Month
                  AND hcc.Cour_id IN ({routeList})
                ORDER BY hcc.Cour_date DESC
                LIMIT 500";

            var rows = await conn.QueryAsync<CodCommissionCnDetail>(sql, p);
            vm.CodCns  = rows.ToList();
            vm.CodTotal = vm.CodCns.Sum(r => r.CommissionAmount);
        }

        private static async Task LoadReturnCodAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            // hr_codreturncommissionprocess columns (verified): Year, Month, GlLocationId,
            //   CourierID, RateId, OleCommission — no Emp_No, no Commission column.
            try
            {
                if (!vm.RouteCodes.Any()) return;

                var routeCodes = vm.RouteCodes.Select(r => r.RouteCode).Distinct().ToList();
                var rcParam    = string.Join(",", routeCodes.Select((_, i) => $"@rc{i}"));
                var p          = new DynamicParameters();
                p.Add("Year",  year);
                p.Add("Month", month);
                for (int i = 0; i < routeCodes.Count; i++) p.Add($"rc{i}", routeCodes[i]);

                var row = await conn.QueryFirstOrDefaultAsync<dynamic>($@"
                    SELECT
                        IFNULL(SUM(OleCommission), 0) AS Amount,
                        COUNT(*)                      AS CnCount
                    FROM hr_codreturncommissionprocess
                    WHERE Year = @Year AND Month = @Month
                      AND CourierID IN ({rcParam})", p);

                if (row != null && (int)row.CnCount > 0)
                {
                    vm.ReturnCod = new ReturnCodSummary {
                        Amount  = (decimal)row.Amount,
                        CnCount = (int)row.CnCount
                    };
                }
            }
            catch { /* table may not exist for this period */ }
        }

        private static async Task LoadFinalCommissionAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            var city = vm.CityCode;
            var sql  = $@"
                SELECT
                    {GtSql}                                   AS GrandTotal,
                    IFNULL(OVERNIGHT, 0)                      AS Overnight,
                    IFNULL(COD, 0)                            AS Cod,
                    IFNULL(COD_Bonus, 0)                      AS CodBonus,
                    IFNULL(COD_Deduction, 0)                  AS CodDeduction,
                    IFNULL(OVERLAND, 0)                       AS Overland,
                    IFNULL(OLE_Delivery, 0)                   AS OleDelivery,
                    IFNULL(OLE_Credit_Booking, 0)             AS OleCreditBooking,
                    IFNULL(YB1KG, 0)                          AS Yb1Kg,
                    IFNULL(YB2KG, 0)                          AS Yb2Kg,
                    IFNULL(YB5KG, 0)                          AS Yb5Kg,
                    IFNULL(YB10KG, 0)                         AS Yb10Kg,
                    IFNULL(YB15KG, 0)                         AS Yb15Kg,
                    IFNULL(YB25KG, 0)                         AS Yb25Kg,
                    IFNULL(FLAYER, 0)                         AS Flayer,
                    IFNULL(DETAIN, 0)                         AS Detain,
                    IFNULL(DomesticDelivery, 0)               AS Economy,
                    IFNULL(PREPAID, 0)                        AS Prepaid,
                    IFNULL(LOVELINE, 0)                       AS LoveLine,
                    IFNULL(VAS, 0)                            AS Vas,
                    IFNULL(General_Light_Delivery, 0)         AS GeneralLight,
                    IFNULL(General_Heavy_Delivery, 0)         AS GeneralHeavy,
                    IFNULL(CASH_Economy_Booking, 0)           AS CashEconomyBooking,
                    IFNULL(Retail_Deduction, 0)               AS RetailDeduction,
                    IFNULL(COD_Deduction, 0)                  AS CodDeduct,
                    IFNULL(Ecom_overall_SR_Bonus, 0)          AS EcomSrBonus,
                    IFNULL(MTD_Delivery, 0)                   AS MtdDelivery,
                    Cour_id                                   AS CourId
                FROM hr_commissionprocess
                WHERE emp_no = @EmpNo AND Year = @Year AND Month = @Month
                  AND (@City = '' OR citycode = @City)
                ORDER BY Cour_id IS NULL ASC, Cour_id ASC
                LIMIT 1";

            // Detect duplicate records (Cour_id=NULL phantom + real record existing together)
            vm.CommissionRecordCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM hr_commissionprocess WHERE emp_no = @EmpNo AND Year = @Year AND Month = @Month AND (@City = '' OR citycode = @City)",
                new { EmpNo = vm.EmpNo, Year = year, Month = month, City = city });

            var row = await conn.QueryFirstOrDefaultAsync<FinalCommissionBreakdown>(sql,
                new { EmpNo = vm.EmpNo, Year = year, Month = month, City = city });

            // Count + list all-time zero commission months for this employee
            var gtExpr = GtSql; // reuse grand total expression
            var zeroMonths = await conn.QueryAsync<dynamic>($@"
                SELECT Year, Month FROM hr_commissionprocess
                WHERE emp_no = @EmpNo AND ({gtExpr}) = 0
                ORDER BY Year DESC, Month DESC",
                new { EmpNo = vm.EmpNo });
            var zeroList = zeroMonths.ToList();
            vm.ZeroCommissionMonths = zeroList.Count;
            vm.ZeroMonthsList = zeroList.Select(r => ((int)r.Year, (int)r.Month)).ToList();

            if (row != null)
            {
                // Also fetch all non-zero columns for full transparency
                var allCols = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT * FROM hr_commissionprocess WHERE emp_no = @EmpNo AND Year = @Year AND Month = @Month AND (@City = '' OR citycode = @City) LIMIT 1",
                    new { EmpNo = vm.EmpNo, Year = year, Month = month, City = city });

                if (allCols != null)
                {
                    var dict = (IDictionary<string, object>)allCols;
                    row.NonZeroColumns = dict
                        .Where(kv => kv.Value != null && kv.Value != DBNull.Value)
                        .Where(kv => {
                            if (decimal.TryParse(kv.Value?.ToString(), out var d)) return d != 0;
                            return false;
                        })
                        .Where(kv => !new[] { "emp_no","citycode","Year","Month","CreatedBy","CreatedDate","UpdatedBy","UpdatedDate" }.Contains(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => {
                            decimal.TryParse(kv.Value?.ToString(), out var d);
                            return d;
                        });
                }
                vm.FinalComm = row;
            }
        }

        private static async Task LoadUsedRatePoliciesAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm)
        {
            if (!vm.CashCns.Any()) return;
            var usedRateIds = vm.CashCns
                .Where(c => c.RateId.HasValue)
                .Select(c => c.RateId!.Value)
                .Distinct().ToList();
            if (!usedRateIds.Any()) return;

            var paramList = string.Join(",", usedRateIds.Select((_, i) => $"@r{i}"));
            var p = new DynamicParameters();
            for (int i = 0; i < usedRateIds.Count; i++) p.Add($"r{i}", usedRateIds[i]);

            var rows = await conn.QueryAsync<CommissionRateInfo>(
                $"SELECT RateID AS RateId, Type, Rate, IsPercent, RateType, Comments FROM hr_commissionpolicy WHERE RateID IN ({paramList})", p);
            vm.UsedRates = rows.ToList();
        }

        /// <summary>
        /// Loads ALL active rate policies (new + old) regardless of whether CN data exists.
        /// Shown in the view even for employees with no Route Code assigned.
        /// </summary>
        private static async Task LoadAllRatePoliciesAsync(MySqlConnection conn, EmployeeCommissionInvestigationVm vm)
        {
            try
            {
                // New system: hr_commissionpolicy
                var newRows = await conn.QueryAsync<CommissionRateInfo>(@"
                    SELECT RateID AS RateId, Type, Rate, IsPercent, RateType, Comments
                    FROM hr_commissionpolicy
                    WHERE IsDeleted = 0
                    ORDER BY RateType, RateID",
                    new { });
                vm.AllRatePolicies = newRows.ToList();
            }
            catch { /* ignore if schema differs */ }

            try
            {
                // Old system: hr_comm_insentives (legacy incentive rates)
                var oldRows = await conn.QueryAsync<OldIncentiveRate>(@"
                    SELECT Id, Type, Rate, Comments
                    FROM hr_comm_insentives
                    ORDER BY Id",
                    new { });
                vm.OldIncentiveRates = oldRows.ToList();
            }
            catch { /* ignore if table doesn't exist */ }
        }

        private static async Task LoadInvestigationNotesAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            try
            {
                var rows = await conn.QueryAsync<InvestigationNote>(@"
                    SELECT Id, EmpNo, Year, Month, ActionType, Notes,
                           CreatedBy, CreatedDate, Status, ResolvedBy, ResolvedDate, IsDeleted
                    FROM hr_commission_investigation_notes
                    WHERE EmpNo = @EmpNo AND Year = @Year AND Month = @Month
                      AND IsDeleted = 0
                    ORDER BY CreatedDate DESC",
                    new { EmpNo = vm.EmpNo, Year = year, Month = month });
                vm.Notes = rows.ToList();
            }
            catch { /* table may not exist yet */ }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SERVER 6 — Billing data
        // ─────────────────────────────────────────────────────────────────────

        private static async Task LoadServer6DataAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm,
            DateTime from, DateTime to)
        {
            // Server 6 connects to lcs_billing database.
            // hr_employeeroutecode lives in lcs_hr on Server 6 — must use fully-qualified name.
            //
            // IMPORTANT — Why S6 count < S10 hr_cash_consignments count (verified 2026-04-04):
            // billing_details on S6 tracks only Billing_Type='CASH' deliveries.
            // hr_cash_consignments on S10 includes BOTH 'CASH' + 'Retail COD' Billing_Types.
            // For emp 12385: S10=2446 (1073 CASH + 1373 Retail COD), S6=1077 (CASH only).
            // This is expected — Retail COD CNs are NOT in billing_details. Not a data error.
            try
            {
                var rcRows = await conn.QueryAsync<RouteCodeInfo>(@"
                    SELECT RouteCode, citycode AS CityCode, LocationId, FromDate, ToDate,
                           (RBIExclude + 0) AS RBIExclude,
                           'Billing' AS FoundOn
                    FROM lcs_hr.hr_employeeroutecode
                    WHERE Emp_No = @EmpNo",
                    new { EmpNo = vm.EmpNo });
                vm.S6RouteCodes = rcRows.ToList();
            }
            catch { }

            // Billing CNs from lcs_billing.billing_details
            // Confirmed columns: cn_number (not cn_no), amount (not billed_amount), cour_id
            if (!vm.RouteCodes.Any()) return;

            var routeCodes = vm.RouteCodes.Select(r => r.RouteCode).Distinct().ToList();
            var rcParam    = string.Join(",", routeCodes.Select((_, i) => $"@rc{i}"));
            var p          = new DynamicParameters();
            p.Add("From", from);
            p.Add("To",   to);
            for (int i = 0; i < routeCodes.Count; i++) p.Add($"rc{i}", routeCodes[i]);

            try
            {
                var countSql = $@"
                    SELECT COUNT(*)
                    FROM billing_details bd
                    WHERE bd.billing_date BETWEEN @From AND @To
                      AND bd.cour_id IN ({rcParam})";
                vm.S6TotalBillingCns = await conn.ExecuteScalarAsync<int>(countSql, p);

                // Load up to 2000 rows for proper matching (S6 billing_details = CASH type only)
                var sampleSql = $@"
                    SELECT
                        bd.cn_number        AS CnNumber,
                        bd.billing_date     AS BillingDate,
                        bd.shipment_type_id AS ShipmentTypeId,
                        IFNULL(sc.shipment_type, CAST(bd.shipment_type_id AS CHAR)) AS ShipmentLabel,
                        IFNULL(bd.amount, 0) AS BilledAmount,
                        bd.cour_id          AS CourId,
                        bd.Station_id       AS StationId
                    FROM billing_details bd
                    LEFT JOIN shipment_codes sc ON sc.shipment_code_id = bd.shipment_type_id
                    WHERE bd.billing_date BETWEEN @From AND @To
                      AND bd.cour_id IN ({rcParam})
                    ORDER BY bd.billing_date DESC
                    LIMIT 2000";

                var billingRows = (await conn.QueryAsync<BillingCnInfo>(sampleSql, p)).ToList();

                // Mark which CNs made it to commission (compare against S10 CASH CNs only)
                var commCnNumbers = vm.CashCns.Select(c => c.CnNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var r in billingRows)
                    r.IsInCommission = commCnNumbers.Contains(r.CnNumber ?? "");

                vm.S6SampleCns   = billingRows;
                vm.S6MatchedCns  = billingRows.Count(r => r.IsInCommission);
                // S6 only has CASH CNs — Retail COD are not in billing_details (not a missing error)
                vm.S6MissingCns  = billingRows.Count(r => !r.IsInCommission);

                // Per-route CN count from S6 (for Cash CNs tab comparison with S10 per-route counts)
                var perRouteSql = $@"
                    SELECT cour_id AS RouteCode, COUNT(*) AS Count
                    FROM billing_details
                    WHERE billing_date BETWEEN @From AND @To
                      AND cour_id IN ({rcParam})
                      AND is_deleted = 0
                    GROUP BY cour_id";
                var perRouteRows = await conn.QueryAsync<dynamic>(perRouteSql, p);
                vm.S6CnCountByRoute = perRouteRows
                    .ToDictionary(r => (string)(r.RouteCode ?? ""), r => (int)(r.Count ?? 0));
            }
            catch (Exception ex)
            {
                vm.S6Error = $"Billing CN query failed: {ex.Message}";
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SERVER 7 — MIS data
        // ─────────────────────────────────────────────────────────────────────

        private static async Task LoadServer7DataAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            // Server 7 (MIS) connects to lcs_db by default.
            // hr_employeeroutecode, hr_olecommissionprocess live in lcs_hr on S7.
            // NOTE: hr_employeelocationdetails, hr_rbi_incentive_detail,
            //       hr_ole_vas_incentive_detail, hr_codreturn_consignments
            //       are on Server 10 (lcs_hr) — loaded in LoadServer10Async.

            // Route codes on Server 7
            try
            {
                var rcRows = await conn.QueryAsync<RouteCodeInfo>(@"
                    SELECT RouteCode, citycode AS CityCode, LocationId, FromDate, ToDate,
                           (RBIExclude + 0) AS RBIExclude,
                           'MIS' AS FoundOn
                    FROM lcs_hr.hr_employeeroutecode
                    WHERE Emp_No = @EmpNo",
                    new { EmpNo = vm.EmpNo });
                vm.S7RouteCodes = rcRows.ToList();
            }
            catch { }

            // OLE commission records — table in lcs_hr on Server 7
            try
            {
                var locationIds = vm.S7Locations
                    .Where(l => l.LocationId.HasValue)
                    .Select(l => l.LocationId!.Value)
                    .Distinct().ToList();

                // Try via route codes first (works even without location record)
                var routeCodes = vm.RouteCodes.Select(r => r.RouteCode).Distinct().ToList();
                if (routeCodes.Any())
                {
                    var rcParam = string.Join(",", routeCodes.Select((_, i) => $"@rc{i}"));
                    var p2      = new DynamicParameters();
                    p2.Add("Year",  year);
                    p2.Add("Month", month);
                    for (int i = 0; i < routeCodes.Count; i++) p2.Add($"rc{i}", routeCodes[i]);

                    var oleRows = await conn.QueryAsync<OleCommissionRow>($@"
                        SELECT GlLocationId, CourierID AS CourierId, RateId,
                               IFNULL(OleCommission, 0) AS OleCommission
                        FROM lcs_hr.hr_olecommissionprocess
                        WHERE Year = @Year AND Month = @Month
                          AND CourierID IN ({rcParam})", p2);
                    vm.S7OleRecords = oleRows.ToList();
                }

                // If not found by route codes but we have locationIds, try by location
                if (!vm.S7OleRecords.Any() && locationIds.Any())
                {
                    var locParam = string.Join(",", locationIds.Select((_, i) => $"@lid{i}"));
                    var p3       = new DynamicParameters();
                    p3.Add("Year",  year);
                    p3.Add("Month", month);
                    for (int i = 0; i < locationIds.Count; i++) p3.Add($"lid{i}", locationIds[i]);

                    var oleRows = await conn.QueryAsync<OleCommissionRow>($@"
                        SELECT GlLocationId, CourierID AS CourierId, RateId,
                               IFNULL(OleCommission, 0) AS OleCommission
                        FROM lcs_hr.hr_olecommissionprocess
                        WHERE Year = @Year AND Month = @Month
                          AND GlLocationId IN ({locParam})", p3);
                    vm.S7OleRecords = oleRows.ToList();
                }

                vm.S7OleTotal = vm.S7OleRecords.Sum(r => r.OleCommission);
            }
            catch { }

            // NOTE: StationId resolution + RBI/VAS/COD Return loading
            // is done in LoadServer10Async (those tables are on S10, not S7).
        }

        private static async Task LoadRbiDetailAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            // hr_rbi_incentive_detail: queried by station_id + Cour_id
            // Includes Overland (Overland is INSIDE RBI — lagkar aata hai)
            var stationId  = vm.ResolvedStationId ?? vm.StationId;
            var routeCodes = vm.RouteCodes.Select(r => r.RouteCode).Distinct().ToList();

            // GUARD: Route Code is required — without it we'd load entire station's data (wrong)
            if (!routeCodes.Any())
            {
                vm.RbiError = "Route Code assign nahi hai — hr_rbi_incentive_detail sirf Route Code se hi employee-specific query ho sakti hai. Station-only query se saray station ka data aata jo galat hoga.";
                return;
            }
            if (string.IsNullOrEmpty(stationId) && !routeCodes.Any()) return;

            try
            {
                DynamicParameters p = new();
                p.Add("Year",  year);
                p.Add("Month", month);

                // Actual columns (verified on live DB 2026-04-05):
                // Station_id, Cour_Id, CN_Number, ShimpmentType, Total_Weight, Total_Amount,
                // RateID, FinalIncentive (=commission), year, month, Created_Date
                // Route codes required (guarded above); optionally narrow by station
                var rcParam2 = string.Join(",", routeCodes.Select((_, i) => $"@rc{i}"));
                for (int i = 0; i < routeCodes.Count; i++) p.Add($"rc{i}", routeCodes[i]);
                string whereClause;
                if (!string.IsNullOrEmpty(stationId))
                {
                    p.Add("StationId", stationId);
                    whereClause = $"a.year = @Year AND a.month = @Month AND a.Station_id = @StationId AND a.Cour_Id IN ({rcParam2})";
                }
                else
                {
                    whereClause = $"a.year = @Year AND a.month = @Month AND a.Cour_Id IN ({rcParam2})";
                }

                // First get total count (for badge accuracy)
                var totalCount = await conn.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM lcs_hr.hr_rbi_incentive_detail a WHERE {whereClause}", p);
                vm.RbiTotalCount = totalCount;

                var rows = await conn.QueryAsync<dynamic>($@"
                    SELECT a.Cour_Id      AS CourId,
                           a.Station_id   AS StationId,
                           a.CN_Number    AS CnNumber,
                           a.ShimpmentType AS TypeName,
                           a.Total_Weight AS Weight,
                           a.Total_Amount AS Amount,
                           a.RateID       AS RateId,
                           IFNULL(a.FinalIncentive, 0) AS Commission,
                           a.Created_Date AS CreatedDate,
                           p.Type         AS RateName
                    FROM lcs_hr.hr_rbi_incentive_detail a
                    LEFT JOIN lcs_hr.hr_commissionpolicy p ON p.RateID = a.RateID
                    WHERE {whereClause}
                    ORDER BY a.Created_Date DESC
                    LIMIT 2000", p);

                vm.RbiRows = rows.Select(r => new RbiIncentiveRow {
                    CnNumber     = r.CnNumber?.ToString(),
                    CourId       = r.CourId?.ToString(),
                    StationId    = r.StationId?.ToString(),
                    Commission   = r.Commission == null ? 0m : (decimal)r.Commission,
                    Type         = r.TypeName?.ToString(),
                    Weight       = r.Weight == null ? (decimal?)null : (decimal)r.Weight,
                    Amount       = r.Amount == null ? (decimal?)null : (decimal)r.Amount,
                    RateId       = r.RateId == null ? (int?)null : (int)r.RateId,
                    RateName     = r.RateName?.ToString(),
                    DeliveryDate = r.CreatedDate == null ? (DateTime?)null : (DateTime)r.CreatedDate,
                    BasisLabel   = r.Weight != null && (decimal)r.Weight > 0 ? "Weight" : "Amount"
                }).ToList();
                vm.RbiTotal = vm.RbiRows.Sum(r => r.Commission);
            }
            catch (Exception ex)
            {
                vm.RbiError = $"RBI data load failed: {ex.Message}";
            }
        }

        private static async Task LoadVasGeneralAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            // hr_ole_vas_incentive_detail: ARVL_DEST = station_id, COURIER_ID = RouteCode
            // date range = period from/to
            var stationId  = vm.ResolvedStationId ?? vm.StationId;
            var routeCodes = vm.RouteCodes.Select(r => r.RouteCode).Distinct().ToList();

            // GUARD: Route Code is required
            if (!routeCodes.Any())
            {
                vm.VasError = "Route Code assign nahi hai — hr_ole_vas_incentive_detail sirf Route Code (COURIER_ID) se hi employee-specific query ho sakti hai.";
                return;
            }
            if (string.IsNullOrEmpty(stationId) && !routeCodes.Any()) return;

            // Calculate period dates (21st prev month → 20th this month)
            var from = new DateTime(year, month, 1).AddMonths(-1).AddDays(20); // 21st of prev
            var to   = new DateTime(year, month, 20);

            try
            {
                DynamicParameters p = new();
                p.Add("From", from);
                p.Add("To",   to);

                // Route codes required (guarded above)
                var rcParam3 = string.Join(",", routeCodes.Select((_, i) => $"@rc{i}"));
                for (int i = 0; i < routeCodes.Count; i++) p.Add($"rc{i}", routeCodes[i]);
                string whereClause;
                if (!string.IsNullOrEmpty(stationId))
                {
                    p.Add("StationId", stationId);
                    whereClause = $"a.DELIVERY_DATE BETWEEN @From AND @To AND a.ARVL_DEST = @StationId AND a.COURIER_ID IN ({rcParam3})";
                }
                else
                {
                    whereClause = $"a.DELIVERY_DATE BETWEEN @From AND @To AND a.COURIER_ID IN ({rcParam3})";
                }

                // Total count for badge accuracy
                var vasTotalCount = await conn.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM lcs_hr.hr_ole_vas_incentive_detail a WHERE {whereClause}", p);
                vm.VasTotalCount = vasTotalCount;

                // Actual columns (verified on live DB 2026-04-05):
                // CN_NUMBER, ARVL_DEST, COURIER_ID, Billing_Type, shipment_type,
                // OPS_Weight_KG, Billed_Weight, DELIVERY_DATE, RateID, Incentive (=commission), Category
                var rows = await conn.QueryAsync<dynamic>($@"
                    SELECT a.COURIER_ID     AS CourierId,
                           a.ARVL_DEST      AS ArvlDest,
                           a.CN_NUMBER      AS CnNumber,
                           a.shipment_type  AS TypeName,
                           a.Category       AS Category,
                           a.Billing_Type   AS BillingType,
                           a.OPS_Weight_KG  AS Weight,
                           a.RateID         AS RateId,
                           IFNULL(a.Incentive, 0) AS Commission,
                           a.DELIVERY_DATE  AS DeliveryDate,
                           p.Type           AS RateName
                    FROM lcs_hr.hr_ole_vas_incentive_detail a
                    LEFT JOIN lcs_hr.hr_commissionpolicy p ON p.RateID = a.RateID
                    WHERE {whereClause}
                    ORDER BY a.DELIVERY_DATE DESC
                    LIMIT 2000", p);

                vm.VasRows = rows.Select(r => new VasGeneralRow {
                    CnNumber     = r.CnNumber?.ToString(),
                    CourierId    = r.CourierId?.ToString(),
                    ArvlDest     = r.ArvlDest?.ToString(),
                    Commission   = r.Commission == null ? 0m : (decimal)r.Commission,
                    Type         = r.Category?.ToString() ?? r.TypeName?.ToString(),
                    Weight       = r.Weight == null ? (decimal?)null : (decimal)r.Weight,
                    Amount       = null,   // no direct amount in this table
                    RateId       = r.RateId == null ? (int?)null : (int)r.RateId,
                    RateName     = r.RateName?.ToString(),
                    DeliveryDate = r.DeliveryDate == null ? (DateTime?)null : (DateTime)r.DeliveryDate,
                    BasisLabel   = r.Weight != null && (decimal)r.Weight > 0 ? "Weight" : "CN"
                }).ToList();
                vm.VasTotal = vm.VasRows.Sum(r => r.Commission);
            }
            catch (Exception ex)
            {
                vm.VasError = $"VAS/General data load failed: {ex.Message}";
            }
        }

        private static async Task LoadCodReturnDetailAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm, int year, int month)
        {
            // hr_codreturn_consignments: Station_id + COURIER_ID
            // Return statuses: DAS, DR, DW
            var stationId  = vm.ResolvedStationId ?? vm.StationId;
            var routeCodes = vm.RouteCodes.Select(r => r.RouteCode).Distinct().ToList();

            // GUARD: Route Code is required — without it we'd load entire station's data (wrong)
            if (!routeCodes.Any())
            {
                vm.CodReturnError = "Route Code assign nahi hai — hr_codreturn_consignments sirf Route Code (COURIER_ID) se hi employee-specific query ho sakti hai. Station-only query se saray station ka data aata jo galat hoga.";
                return;
            }

            try
            {
                DynamicParameters p = new();
                p.Add("Year",  year);
                p.Add("Month", month);

                var rcParam = string.Join(",", routeCodes.Select((_, i) => $"@rc{i}"));
                for (int i = 0; i < routeCodes.Count; i++) p.Add($"rc{i}", routeCodes[i]);

                string whereClause;
                if (!string.IsNullOrEmpty(stationId))
                {
                    p.Add("StationId", stationId);
                    whereClause = $"a.Year = @Year AND a.Month = @Month AND a.Station_id = @StationId AND a.COURIER_ID IN ({rcParam})";
                }
                else
                {
                    whereClause = $"a.Year = @Year AND a.Month = @Month AND a.COURIER_ID IN ({rcParam})";
                }

                var codReturnTotalCount = await conn.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM lcs_hr.hr_codreturn_consignments a WHERE {whereClause}", p);
                vm.CodReturnTotalCount = codReturnTotalCount;

                // Actual columns (verified on live DB 2026-04-05):
                // Station_id, COURIER_ID, CN_NUMBER, STATUS, DELIVERY_DATE,
                // RateID, RatePerShipment, OpsInc (=commission), Year, Month
                var rows = await conn.QueryAsync<dynamic>($@"
                    SELECT a.COURIER_ID       AS CourierId,
                           a.Station_id       AS StationId,
                           a.CN_NUMBER        AS CnNumber,
                           a.STATUS           AS Status,
                           a.RateID           AS RateId,
                           a.RatePerShipment  AS Amount,
                           IFNULL(a.OpsInc, 0) AS Commission,
                           a.DELIVERY_DATE    AS ReturnDate,
                           p.Type             AS RateName
                    FROM lcs_hr.hr_codreturn_consignments a
                    LEFT JOIN lcs_hr.hr_commissionpolicy p ON p.RateID = a.RateID
                    WHERE {whereClause}
                    ORDER BY a.DELIVERY_DATE DESC
                    LIMIT 1000", p);

                vm.CodReturnRows = rows.Select(r => new CodReturnCnRow {
                    CnNumber   = r.CnNumber?.ToString(),
                    CourierId  = r.CourierId?.ToString(),
                    StationId  = r.StationId?.ToString(),
                    Status     = r.Status?.ToString(),
                    Commission = r.Commission == null ? 0m : (decimal)r.Commission,
                    Amount     = r.Amount == null ? (decimal?)null : (decimal)r.Amount,
                    RateId     = r.RateId == null ? (int?)null : (int)r.RateId,
                    RateName   = r.RateName?.ToString(),
                    ReturnDate = r.ReturnDate == null ? (DateTime?)null : (DateTime)r.ReturnDate,
                    BasisLabel = "Amount"
                }).ToList();
                vm.CodReturnTotal = vm.CodReturnRows.Sum(r => r.Commission);
            }
            catch (Exception ex)
            {
                vm.CodReturnError = $"COD Return data load failed: {ex.Message}";
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LOCATION + STATION ID RESOLUTION (hr_employeelocationdetails, Server 10)
        // lcs_hr.hr_employeelocationdetails.LocationId → lcs_setup.locations.BILLINGCITYID
        // ─────────────────────────────────────────────────────────────────────

        private async Task LoadLocationAndStationAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm)
        {
            try
            {
                // hr_employeelocationdetails: columns Emp_No, LocationId, FromDate, ToDate (no CityCode)
                var locRows = await conn.QueryAsync<dynamic>(@"
                    SELECT eld.LocationId, eld.FromDate, eld.ToDate,
                           l.LocationName,
                           LPAD(CAST(l.BILLINGCITYID AS CHAR), 5, '0') AS StationId
                    FROM lcs_hr.hr_employeelocationdetails eld
                    LEFT JOIN lcs_setup.locations l ON l.LocationID = eld.LocationId
                    WHERE eld.Emp_No = @EmpNo
                    ORDER BY eld.ToDate IS NULL DESC, eld.FromDate DESC",
                    new { EmpNo = vm.EmpNo });

                vm.S7Locations = locRows.Select(r => new LocationInfo {
                    LocationId = r.LocationId == null ? (int?)null : (int)r.LocationId,
                    CityCode   = null,   // not stored in this table
                    CityName   = r.LocationName?.ToString(),
                    FromDate   = r.FromDate == null ? (DateTime?)null : (DateTime)r.FromDate,
                    ToDate     = r.ToDate  == null ? (DateTime?)null : (DateTime)r.ToDate
                }).ToList();

                // Resolve active StationId for RBI/VAS/CODReturn queries
                var activeRow = locRows.Cast<dynamic>().FirstOrDefault(r => r.ToDate == null)
                             ?? locRows.Cast<dynamic>().FirstOrDefault();
                if (activeRow != null)
                {
                    vm.ResolvedStationId = activeRow.StationId?.ToString();
                    vm.LocationName      = activeRow.LocationName?.ToString();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "LoadLocationAndStationAsync failed for {EmpNo}", vm.EmpNo);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ADJUSTMENTS (hr_empcommadjdtl + adjusment_policy, Server 10)
        // ─────────────────────────────────────────────────────────────────────

        private static async Task LoadAdjustmentsAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm,
            string empNo, int year, int month)
        {
            try
            {
                var rows = await conn.QueryAsync<dynamic>(@"
                    SELECT a.Id, a.Emp_No, a.year, a.month,
                           a.adjusment_type_id AS AdjTypeId,
                           IFNULL(p.Name, CAST(a.adjusment_type_id AS CHAR)) AS AdjustmentType,
                           IFNULL(a.amount, 0) AS Amount,
                           a.Remarks,
                           a.CreatedDate
                    FROM hr_empcommadjdtl a
                    LEFT JOIN adjusment_policy p ON p.Id = a.adjusment_type_id
                    WHERE a.Emp_No = @EmpNo AND a.year = @Year AND a.month = @Month
                    ORDER BY a.Id",
                    new { EmpNo = empNo, Year = year, Month = month });

                vm.Adjustments = rows.Select(r => new AdjustmentRow
                {
                    Id             = r.Id == null ? 0 : (int)r.Id,
                    AdjustmentType = r.AdjustmentType?.ToString(),
                    Amount         = r.Amount == null ? 0m : (decimal)r.Amount,
                    Remarks        = r.Remarks?.ToString(),
                    CreatedDate    = r.CreatedDate == null ? (DateTime?)null : (DateTime)r.CreatedDate
                }).ToList();
            }
            catch (Exception ex)
            {
                // Non-fatal — leave Adjustments empty
                vm.Adjustments = new();
                _ = ex; // logged at RunLoader level
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SR BONUS (hr_incentive_overall_SR, Server 10)
        // ─────────────────────────────────────────────────────────────────────

        private static async Task LoadSrBonusAsync(
            MySqlConnection conn, EmployeeCommissionInvestigationVm vm,
            string empNo, int year, int month)
        {
            try
            {
                var row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT emp_no, year, month,
                           is_eligible         AS IsEligible,
                           bonus_amount        AS BonusAmount,
                           total_cn            AS TotalCn,
                           eligible_cn         AS EligibleCn,
                           remarks             AS Remarks
                    FROM hr_incentive_overall_SR
                    WHERE emp_no = @EmpNo AND year = @Year AND month = @Month
                    LIMIT 1",
                    new { EmpNo = empNo, Year = year, Month = month });

                if (row != null)
                {
                    vm.SrBonus = new SrBonusRow
                    {
                        IsEligible  = row.IsEligible?.ToString(),
                        BonusAmount = row.BonusAmount == null ? 0m : (decimal)row.BonusAmount,
                        TotalCn     = row.TotalCn  == null ? (int?)null : (int)row.TotalCn,
                        EligibleCn  = row.EligibleCn == null ? (int?)null : (int)row.EligibleCn,
                        Remarks     = row.Remarks?.ToString()
                    };
                }
            }
            catch
            {
                // Non-fatal — leave SrBonus null
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ROUTE CODE SUGGESTION
        // ─────────────────────────────────────────────────────────────────────

        public async Task<RouteCodeSuggestionVm> GetRouteCodeSuggestionsAsync(string empNo, string cityCode)
        {
            var result = new RouteCodeSuggestionVm();

            // ── S10: Employee job/type info ──────────────────────────────────
            try
            {
                using var conn = OpenMain();

                var emp = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT epd.EMPLOYEE_TYPE, et.FullName AS EmployeeTypeName,
                           epd.JobTypeId,    j.FullName  AS JobTitle
                    FROM hr_employeepersonaldetail epd
                    LEFT JOIN hr_employeetype et ON et.Code = epd.EMPLOYEE_TYPE
                    LEFT JOIN hr_jobs         j  ON j.Code  = epd.JobTypeId
                    WHERE epd.EMP_NO = @EmpNo LIMIT 1",
                    new { EmpNo = empNo });

                if (emp != null)
                {
                    result.EmployeeType     = emp.EMPLOYEE_TYPE?.ToString();
                    result.EmployeeTypeName = emp.EmployeeTypeName?.ToString();
                    result.JobTypeId        = emp.JobTypeId?.ToString();
                    result.JobTitle         = emp.JobTitle?.ToString();
                }

                // ── S10: Commission process Cour_id history (last 12 months) ──
                var history = await conn.QueryAsync<CommissionCourIdRow>(@"
                    SELECT Cour_id AS CourId, Year, Month
                    FROM hr_commissionprocess
                    WHERE emp_no = @EmpNo
                    ORDER BY Year DESC, Month DESC
                    LIMIT 12",
                    new { EmpNo = empNo });
                result.CommissionHistory = history.ToList();

                // Best route code hint from commission history
                var bestCourId = result.CommissionHistory
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.CourId))?.CourId;
                if (bestCourId != null)
                {
                    result.SuggestedRouteCode = bestCourId;
                    result.Notes.Add($"Commission Process mein Cour_id = '{bestCourId}' mila — yeh Route Code ho sakta hai.");
                }
                else
                {
                    result.Notes.Add("Commission Process ke saare records mein Cour_id = NULL hai — Route Code kabhi assign nahi hua.");
                }

                // ── S10: Similar active colleagues in same city ─────────────
                var similar = await conn.QueryAsync<SimilarRouteRow>(@"
                    SELECT erc.Emp_No     AS EmpNo,
                           epd.NAME       AS Name,
                           erc.RouteCode,
                           erc.CodeType,
                           cct.Name       AS CodeTypeName,
                           epd.EMPLOYEE_TYPE AS EmployeeType,
                           j.FullName     AS JobTitle,
                           erc.LocationId    AS LocationId,
                           loc.LocationName  AS LocationName
                    FROM hr_employeeroutecode erc
                    LEFT JOIN hr_employeepersonaldetail epd ON epd.EMP_NO    = erc.Emp_No
                    LEFT JOIN couriercodetype            cct ON cct.Id        = erc.CodeType
                    LEFT JOIN hr_jobs                   j   ON j.Code        = epd.JobTypeId
                    LEFT JOIN lcs_setup.locations       loc ON loc.LocationID = erc.LocationId
                    WHERE erc.ToDate IS NULL
                      AND erc.citycode = @CityCode
                      AND erc.Emp_No  != @EmpNo
                    ORDER BY erc.Code DESC
                    LIMIT 12",
                    new { CityCode = cityCode, EmpNo = empNo });
                result.SimilarEmployees = similar.ToList();

                if (result.SimilarEmployees.Any())
                    result.Notes.Add($"Is city mein {result.SimilarEmployees.Count} active employees Route Codes ke sath milein — neechay table mein dekheein (reference ke liye use karein).");

                // Suggest CodeType from most common type among similar employees
                if (!result.SuggestedCodeType.HasValue && result.SimilarEmployees.Any())
                {
                    var commonType = result.SimilarEmployees
                        .Where(s => s.CodeType.HasValue)
                        .GroupBy(s => s.CodeType)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault();
                    if (commonType != null)
                    {
                        result.SuggestedCodeType     = commonType.Key;
                        result.SuggestedCodeTypeName = commonType.First().CodeTypeName;
                        result.Notes.Add($"Is city ke employees mein sabse zyada CodeType {commonType.Key} ({commonType.First().CodeTypeName ?? "?"}) use hota hai — suggestion ke taur par set kiya gaya.");
                    }
                }

                // ── S10: Available route codes in this city ─────────────────
                var availableRoutes = await conn.QueryAsync<AvailableRouteRow>(@"
                    SELECT RouteCode, Description
                    FROM hr_routecodes_hdr
                    WHERE CityCode = @CityCode
                    ORDER BY RouteCode
                    LIMIT 30",
                    new { CityCode = cityCode });
                result.AvailableRouteCodes = availableRoutes.ToList();

                if (result.AvailableRouteCodes.Any())
                    result.Notes.Add($"Is city mein {result.AvailableRouteCodes.Count} valid Route Codes hain (hr_routecodes_hdr) — neechay list se koi bhi select kar saktay hain.");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "GetRouteCodeSuggestionsAsync: S10 query failed for EmpNo={EmpNo}", empNo);
                result.Notes.Add($"Server 10 se data load nahi hua: {ex.Message}");
            }

            // ── S6: Check hr_employeeroutecode on Billing server ────────────
            try
            {
                var (s6, _) = await TryOpenExternalAsync("LHR_Billing", "Server 6");
                if (s6 != null)
                {
                    using (s6)
                    {
                        var s6rc = await s6.QueryFirstOrDefaultAsync<dynamic>(@"
                            SELECT RouteCode, CodeType
                            FROM lcs_hr.hr_employeeroutecode
                            WHERE Emp_No = @EmpNo
                            ORDER BY Code DESC LIMIT 1",
                            new { EmpNo = empNo });

                        if (s6rc != null)
                        {
                            result.S6RouteCode    = s6rc.RouteCode?.ToString();
                            result.S6CodeType     = s6rc.CodeType == null ? (int?)null : (int)s6rc.CodeType;
                            result.S6CodeTypeName = null; // couriercodetype not available on S6

                            result.Notes.Add($"Server 6 (Billing) par Route Code mila: '{result.S6RouteCode}' (CodeType {result.S6CodeType} — {result.S6CodeTypeName ?? "?"})");

                            // Use S6 as suggestion if nothing found yet
                            result.SuggestedRouteCode ??= result.S6RouteCode;
                            result.SuggestedCodeType  ??= result.S6CodeType;
                        }
                        else
                        {
                            result.Notes.Add("Server 6 (Billing) par bhi koi hr_employeeroutecode record nahi mila.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Notes.Add($"Server 6 check failed: {ex.Message}");
            }

            // ── S7: Check hr_employeeroutecode on MIS server ────────────────
            try
            {
                var (s7, _) = await TryOpenExternalAsync("MIS", "Server 7");
                if (s7 != null)
                {
                    using (s7)
                    {
                        var s7rc = await s7.QueryFirstOrDefaultAsync<dynamic>(@"
                            SELECT erc.RouteCode, erc.CodeType, cct.Name AS CodeTypeName
                            FROM lcs_hr.hr_employeeroutecode erc
                            LEFT JOIN lcs_hr.couriercodetype cct ON cct.Id = erc.CodeType
                            WHERE erc.Emp_No = @EmpNo
                            ORDER BY erc.Code DESC LIMIT 1",
                            new { EmpNo = empNo });

                        if (s7rc != null)
                        {
                            result.S7RouteCode    = s7rc.RouteCode?.ToString();
                            result.S7CodeType     = s7rc.CodeType == null ? (int?)null : (int)s7rc.CodeType;
                            result.S7CodeTypeName = s7rc.CodeTypeName?.ToString();

                            result.Notes.Add($"Server 7 (MIS) par Route Code mila: '{result.S7RouteCode}' (CodeType {result.S7CodeType} — {result.S7CodeTypeName ?? "?"})");

                            result.SuggestedRouteCode ??= result.S7RouteCode;
                            result.SuggestedCodeType  ??= result.S7CodeType;
                        }
                        else
                        {
                            result.Notes.Add("Server 7 (MIS) par bhi koi Route Code record nahi mila (hr_employeeroutecode table mein). Note: yeh Route Code table hai — employee ka Location record S7 par alag hota hai aur woh mojood ho sakta hai.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Notes.Add($"Server 7 check failed: {ex.Message}");
            }

            // Final fallback note
            if (string.IsNullOrEmpty(result.SuggestedRouteCode))
            {
                if (result.AvailableRouteCodes.Any())
                    result.Notes.Add($"Kisi bhi server par is employee ka Route Code record nahi mila. Is city ke {result.AvailableRouteCodes.Count} valid Route Codes neechay list mein hain — supervisor se poochein konsa assign karna hai, phir 'Fix Now' form mein woh code enter karein.");
                else
                    result.Notes.Add("Kisi bhi server par Route Code nahi mila aur is city mein koi valid Route Code (hr_routecodes_hdr) bhi nahi hai — pehle MIS team se Route Code banwayein, phir employee ko assign karein.");
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ISSUE DETECTION
        // ─────────────────────────────────────────────────────────────────────

        private static void DetectIssues(EmployeeCommissionInvestigationVm vm)
        {
            var issues = vm.Issues;

            // ── Server availability ────────────────────────────────────────────
            if (!vm.S6Connected)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Info,
                    Category = "Server",
                    Title    = "Server 6 (Billing) Unavailable",
                    Description = $"Billing server not reachable: {vm.S6Error}. Cannot verify raw billing CNs.",
                    Server   = "Server 6 — 172.16.0.6",
                    SuggestedAction = "Check LHR_Billing connection string. Raw billing CN comparison skipped."
                });

            if (!vm.S7Connected)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Info,
                    Category = "Server",
                    Title    = "Server 7 (MIS) Unavailable",
                    Description = $"MIS server not reachable: {vm.S7Error}. Cannot verify location / OLE data.",
                    Server   = "Server 7 — 172.16.0.7",
                    SuggestedAction = "Check MIS connection string. Location + OLE verification skipped."
                });

            // ── Route codes ────────────────────────────────────────────────────
            if (!vm.HasRouteCodes)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Critical,
                    Category = "RouteCode",
                    Title    = "No Active Route Code on Main Server",
                    Description = $"Employee {vm.EmpNo} has no active route code in hr_employeeroutecode (Server 10). Commission cannot be generated without a route code.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_employeeroutecode",
                    SuggestedAction = "Transaction → Employee Route Code mein route code assign karein, 3 servers par replicate karein, phir Commission Process chalayein.",
                    FixPageUrl   = $"/Transaction/EmployeeRoutCode",
                    FixPageLabel = "Open Employee Route Code",
                    FixModalId   = "modal-fix-route",
                    FixModalLabel = "Route Code Assign Karein",
                    FixSteps     =
                    [
                        $"Setup menu mein jaaein → Route Codes (hr_employeeroutecode) kholein.",
                        $"'Add New' click karein: Employee No = {vm.EmpNo} | RouteCode = unique 5-char code assign karein | CodeType = employee ka type (1=D&P, 2=Overland, 4=Ground Leopard, etc.) | City = {vm.CityCode ?? "relevant city"} | FromDate = appointment date ya aaj.",
                        $"SAME route code Server 6 (172.16.0.6 — lcs_billing.hr_employeeroutecode) mein bhi add karein — warna billing CNs is employee se link nahi honge.",
                        $"SAME route code Server 7 (172.16.0.7 — lcs_db.hr_employeeroutecode) mein bhi add karein — warna OLE/MIS commission nahi banega.",
                        $"Commission Eligibility check karein — agar CodeType 2/3/4/5 hai toh Setup → Commission Eligibility mein bhi record add karna hoga (dekheein Eligibility issue).",
                        $"Commission Process Master chalayein: City = {vm.CityCode ?? "employee ki city"}, Year = {vm.Year}, Month = {vm.Month}.",
                        "Process complete hone ke baad is page ko Refresh karein — issue resolve ho jaega."
                    ]
                });

            if (vm.S6Connected && !vm.S6RouteCodesMatch)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Warning,
                    Category = "RouteCode",
                    Title    = "Route Code Mismatch — Server 6 vs Main",
                    Description = "Route codes on Server 6 (Billing) do not match route codes on Server 10 (Main). Billing CNs may not link to this employee.",
                    Server      = "Server 6 — 172.16.0.6",
                    SourceTable = "hr_employeeroutecode",
                    SuggestedAction = "Server 6 (172.16.0.6 lcs_billing) ke hr_employeeroutecode mein same route code add karein.",
                    FixSteps     =
                    [
                        "Server 6 (172.16.0.6 — lcs_billing database) se connect karein (SQLyog/HeidiSQL).",
                        $"Query: SELECT * FROM hr_employeeroutecode WHERE Emp_No = '{vm.EmpNo}'  — likely empty hoga.",
                        $"Server 10 (Main) ke record ka RouteCode, CodeType, CityCode copy karein.",
                        "Server 6 mein INSERT karein ya Setup → Route Codes (Billing server mode) se add karein.",
                        "Commission Process Master re-run karein aur page refresh karein."
                    ]
                });

            if (vm.S7Connected && !vm.S7RouteCodesMatch)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Warning,
                    Category = "RouteCode",
                    Title    = "Route Code Mismatch — Server 7 vs Main",
                    Description = "Route codes on Server 7 (MIS) do not match route codes on Server 10 (Main). OLE commission may not process correctly.",
                    Server      = "Server 7 — 172.16.0.7",
                    SourceTable = "hr_employeeroutecode",
                    SuggestedAction = "Server 7 (172.16.0.7 lcs_db) ke hr_employeeroutecode mein same route code add karein. MIS team (18th floor) se rabta karein.",
                    FixSteps     =
                    [
                        "Server 7 (172.16.0.7 — lcs_db database) se connect karein.",
                        $"Query: SELECT * FROM hr_employeeroutecode WHERE Emp_No = '{vm.EmpNo}'",
                        "Server 10 se matching record insert karein.",
                        "MIS team ko inform karein agar OLE commission affected hai."
                    ]
                });

            // ── Location ───────────────────────────────────────────────────────
            if (vm.S7Connected && !vm.S7LocationPresent)
            {
                bool isInactive = vm.EmpStatus == "I" || vm.EmpStatus == "L";
                issues.Add(new InvestigationIssue {
                    Severity    = isInactive ? IssueSeverity.Info : IssueSeverity.Warning,
                    Category    = "Location",
                    Title       = "No Active Location on MIS Server (Server 7)",
                    Description = isInactive
                        ? $"No active location for {vm.EmpNo} on Server 7. Employee is inactive (left {vm.LeftDate?.ToString("dd-MMM-yyyy") ?? "N/A"}) — location data not expected."
                        : $"No active location found in hr_employeelocationdetails for {vm.EmpNo}. OLE commission requires a valid location.",
                    Server      = "Server 7 — 172.16.0.7",
                    SourceTable = "hr_employeelocationdetails",
                    SuggestedAction = isInactive
                        ? "No action needed — this is an inactive/left employee."
                        : "Add location assignment for this employee in Server 7 MIS. Contact MIS team (18th floor)."
                });
            }

            // ── Eligibility ────────────────────────────────────────────────────
            if (vm.IsEligible == null && !vm.HasFinalCommission)
                // No eligibility record AND no commission — raise warning
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Warning,
                    Category = "Eligibility",
                    Title    = "Eligibility Record Not Found",
                    Description = $"No record in hr_empcommissioneligibility for {vm.EmpNo}. Employee must be assigned a commission policy in Setup.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_empcommissioneligibility",
                    SuggestedAction = "Setup → Commission Eligibility mein is employee ka record add karein aur IsEligible = TRUE set karein.",
                    FixPageUrl   = $"/Setup/CommissionEligibility?empNo={vm.EmpNo}",
                    FixPageLabel = "Open Commission Eligibility",
                    FixModalId   = "modal-fix-eligibility",
                    FixModalLabel = "Eligibility Setup Karein",
                    FixSteps     =
                    [
                        $"Setup menu → Commission Eligibility (hr_empcommissioneligibility) kholein.",
                        $"Employee No '{vm.EmpNo}' search karein — koi record nahi milega.",
                        $"'Add New' click karein: Employee No = {vm.EmpNo} | CommissionId = employee ke CodeType ke hisaab se (2=Ground Leopard/In-House, 3=Express Center In-House, 4=Express Center) | IsEligible = TRUE (checked).",
                        "Agar CodeType 1 (D&P) hai toh hr_empcommissioneligibility record zaroori NAHI hota — D&P employees ka commission alag mechanism se hota hai.",
                        "Record save karne ke baad Commission Process Master chalayein: yahi City, Year, Month.",
                        "Is page ko Refresh karein — Eligibility Unknown issue resolve ho jayega."
                    ]
                });
            else if (vm.IsEligible == null && vm.HasFinalCommission && vm.FinalComm!.GrandTotal == 0)
                // No eligibility record AND commission record exists but ZERO — still a problem, not OK
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Warning,
                    Category = "Eligibility",
                    Title    = "Eligibility Record Missing — Commission = Rs 0",
                    Description = $"No record in hr_empcommissioneligibility for {vm.EmpNo}. Commission record exists but GrandTotal = Rs 0. "
                                + "Eligibility record zaroor hona chahiye taa ke commission correctly process ho.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_empcommissioneligibility",
                    SuggestedAction = "Setup → Commission Eligibility mein is employee ka record add karein aur IsEligible = TRUE set karein, phir reprocess karein.",
                    FixPageUrl   = $"/Setup/CommissionEligibility?empNo={vm.EmpNo}",
                    FixPageLabel = "Open Commission Eligibility",
                    FixModalId   = "modal-fix-eligibility",
                    FixModalLabel = "Eligibility Setup Karein",
                    FixSteps     =
                    [
                        $"Setup menu → Commission Eligibility (hr_empcommissioneligibility) kholein.",
                        $"Employee No '{vm.EmpNo}' search karein — koi record nahi milega.",
                        $"'Add New' click karein: Employee No = {vm.EmpNo} | CommissionId = employee ke CodeType ke hisaab se | IsEligible = TRUE (checked).",
                        "Record save karne ke baad Commission Process Master chalayein: yahi City, Year, Month.",
                        "Is page ko Refresh karein."
                    ]
                });
            else if (vm.IsEligible == null && vm.HasFinalCommission && vm.FinalComm!.GrandTotal > 0)
                // No eligibility record BUT commission DID process with actual amount — just info
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Info,
                    Category = "Eligibility",
                    Title    = "Eligibility Record Not in Master Table",
                    Description = $"No record in hr_empcommissioneligibility for {vm.EmpNo}, but commission was successfully processed (Grand Total = Rs {vm.FinalComm?.GrandTotal:N0}). Eligibility may be controlled by a different mechanism.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_empcommissioneligibility",
                    SuggestedAction = "No action needed — commission was processed. Review eligibility setup if future reprocessing is planned."
                });

            if (vm.IsEligible == false)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Critical,
                    Category = "Eligibility",
                    Title    = "Employee Marked Not Eligible",
                    Description = $"IsEligible = FALSE in hr_empcommissioneligibility. Commission will NOT be generated. Reason: {vm.EligibilityNote ?? "No reason recorded"}.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_empcommissioneligibility",
                    SuggestedAction = "Setup → Commission Eligibility mein IsEligible = TRUE karein, phir Commission Process Master re-run karein.",
                    FixPageUrl   = $"/Setup/CommissionEligibility?empNo={vm.EmpNo}",
                    FixPageLabel = "Open Commission Eligibility",
                    FixModalId   = "modal-fix-eligibility",
                    FixModalLabel = "Eligibility Update Karein",
                    FixSteps     =
                    [
                        $"Setup → Commission Eligibility kholein.",
                        $"Employee No '{vm.EmpNo}' ka record dhundein.",
                        $"IsEligible = TRUE (checked) karein aur reason note delete ya update karein.",
                        "Save karein.",
                        $"Commission Process Master chalayein: City = {vm.CityCode ?? "employee ki city"}, Year = {vm.Year}, Month = {vm.Month}.",
                        "Is page ko Refresh karein."
                    ]
                });

            // ── Attendance ─────────────────────────────────────────────────────
            if (!vm.Attendance.IsProcessed)
                issues.Add(new InvestigationIssue {
                    // If commission was processed anyway, attendance missing is just info.
                    // If commission is missing, attendance being absent could be the reason — warn.
                    Severity    = vm.HasFinalCommission ? IssueSeverity.Info : IssueSeverity.Warning,
                    Category    = "Attendance",
                    Title       = "Attendance Not Processed",
                    Description = vm.HasFinalCommission
                        ? "No attendance record in hr_employeeattendanceprocess, but commission was processed successfully. Proration was likely skipped or defaulted to full month."
                        : "No attendance record found in hr_employeeattendanceprocess. Commission proration may be incorrect.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_employeeattendanceprocess",
                    SuggestedAction = "Run attendance processing for this employee for the selected month if proration is required."
                });

            // ── Commission record ──────────────────────────────────────────────
            if (vm.IsEligible != false && !vm.HasFinalCommission)
            {
                bool periodNotDone = vm.PeriodTo > DateTime.Today;
                issues.Add(new InvestigationIssue {
                    Severity = periodNotDone ? IssueSeverity.Info : IssueSeverity.Critical,
                    Category = "Commission",
                    Title    = periodNotDone
                        ? "Commission Period Not Yet Complete"
                        : "Final Commission Record Missing",
                    Description = periodNotDone
                        ? $"Commission period ends {vm.PeriodTo:dd MMM yyyy} — period is not yet complete. Commission process is run after the 20th of each month."
                        : $"No record in hr_commissionprocess for {vm.EmpNo}. Commission process may not have run, or ran with errors.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_commissionprocess",
                    SuggestedAction = periodNotDone
                        ? $"{vm.PeriodTo:dd MMM yyyy} ke baad Commission Process Master chalayein — abhi period complete nahi hua."
                        : $"Commission Process Master chalayein: City = {vm.CityCode ?? "employee ki city"}, Year = {vm.Year}, Month = {vm.Month}.",
                    FixSteps = periodNotDone
                        ? [$"Commission period {vm.PeriodTo:dd MMM yyyy} ko khatam hoga — abhi incomplete hai.", "20 tarikh ke baad Commission Process Master chalayein."]
                        : [
                            $"Pehle check karein ke employee ka Route Code assign hai (tab Routes mein dekheein).",
                            $"Commission Eligibility check karein — eligibility record hona chahiye.",
                            $"Commission Process → Commission Process Master kholein.",
                            $"City = {vm.CityCode ?? "employee ki city"} | Year = {vm.Year} | Month = {vm.Month} select karein.",
                            "Process run karein — complete hone ke baad is page ko Refresh karein."
                          ]
                });
            }

            // ── Duplicate commission records (phantom NULL Cour_id + real record) ───
            if (vm.HasDuplicateCommission)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Critical,
                    Category = "Commission",
                    Title    = $"Duplicate Commission Records — {vm.CommissionRecordCount} Rows For Same Month",
                    Description = $"hr_commissionprocess mein {vm.Year}/{vm.Month} ke liye {vm.CommissionRecordCount} records hain (ek Cour_id=NULL phantom record, ek real Cour_id record). "
                                + "Yeh hota hai jab commission process multiple baar chalti hai ya koi bug aata hai. "
                                + "NULL Cour_id wala record galat hai — ise delete karna chahiye.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_commissionprocess",
                    SuggestedAction = "NULL Cour_id wala phantom record delete karein, phir Reprocess chalayein.",
                    FixModalId    = "modal-delete-phantom",
                    FixModalLabel = "Phantom Record Delete Karein",
                    FixSteps = [
                        $"Issues tab mein 'Phantom Record Delete Karein' button click karein.",
                        "Confirm dialog mein 'Haan, Delete Karein' click karein.",
                        "Delete ke baad page Refresh karein.",
                        "Agar commission phir bhi zero hai toh Commission Process Master se Reprocess chalayein."
                    ]
                });

            // ── No billing data at station despite route code being assigned ────
            if (vm.HasRouteCodes && !vm.CashCns.Any() && vm.S6TotalBillingCns == 0 && vm.HasFinalCommission && vm.FinalComm!.GrandTotal == 0)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Warning,
                    Category = "Data",
                    Title    = "Station Par Koi Billing Data Nahi — CNs Zero",
                    Description = $"Route Code assign hai ({string.Join(", ", vm.RouteCodes.Select(r => r.RouteCode))}) aur commission process bhi chali — "
                                + "lekin is route ke liye hr_cash_consignments (S10) mein 0 CNs aur billing_details (S6) mein bhi 0 CNs hain. "
                                + "Iska matlab is station par billing integration set up nahi hai ya CNs is route code ke sath tag nahi ho rhe.",
                    Server      = "Server 10 + Server 6",
                    SourceTable = "hr_cash_consignments + lcs_billing.billing_details",
                    SuggestedAction = "Operations/Billing team se check karein: kya is station ke CNs billing system mein ja rahe hain? Kya route code correctly assigned hai billing mein?",
                    FixModalId    = "modal-billing-not-integrated",
                    FixModalLabel = "Billing Integration Issue — Kya Karein?",
                    FixSteps = [
                        $"S6 billing_details mein check karein: SELECT * FROM billing_details WHERE cour_id = '{string.Join("/", vm.RouteCodes.Select(r => r.RouteCode))}' LIMIT 10;",
                        "Agar S6 par bhi 0 CNs hain toh billing team se report karein.",
                        "Billing team ko batayein ke is route code par CNs properly tag hoने chahiyen.",
                        "Jab CNs aana shuru hon tab Reprocess karein."
                    ]
                });

            // ── Commission = 0 despite route code having CNs ──────────────────
            if (vm.HasFinalCommission && vm.FinalComm!.GrandTotal == 0 && vm.CashCns.Any())
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Warning,
                    Category = "Commission",
                    Title    = "Final Commission = Zero Despite CN Records",
                    Description = $"hr_commissionprocess has a record with GrandTotal = 0, but employee has {vm.CashCns.Count} cash CNs. Possible rate policy issue or process error.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_commissionprocess",
                    SuggestedAction = "Check hr_commissionpolicy for applicable rate IDs. Reprocess commission after verifying rates."
                });

            // ── Commission record exists but NULL Cour_id + GrandTotal = 0 ────
            // This means the process ran BUT could not link any route code → all columns zero
            if (vm.HasFinalCommission && string.IsNullOrEmpty(vm.FinalComm!.CourId) && vm.FinalComm.GrandTotal == 0)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Critical,
                    Category = "Commission",
                    Title    = "Commission Record Exists But Cour_id = NULL — Rs 0",
                    Description = $"hr_commissionprocess mein {vm.Year}/{vm.Month} ka record hai LEKIN Cour_id = NULL aur GrandTotal = Rs 0. "
                                + "Yeh hota hai jab commission process chalti hai magar employee ko koi route code assign nahi hota — process blank record bana deta hai. "
                                + "Is record ko sirf route code assign karne ke baad REPROCESS karne se theek kiya ja sakta hai.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_commissionprocess",
                    SuggestedAction = "Route Code assign karein (upar Critical Issue dekheein), phir Commission Process Master mein is employee ki City/Year/Month se REPROCESS chalayein.",
                    FixSteps = [
                        $"PEHLE: Route Code assign karein — 'No Active Route Code' wala Critical issue aur uske fix steps dekheein.",
                        $"PHIR: Commission Process → Commission Process Master kholein.",
                        $"City = {vm.CityCode ?? "employee ki city"} | Year = {vm.Year} | Month = {vm.Month} select karein.",
                        "Process run karein — yeh existing NULL Cour_id record ko overwrite kare ga with correct data.",
                        "Process complete hone ke baad is page Refresh karein — GrandTotal > 0 aana chahiye."
                    ]
                });

            // ── Historical zero commission months ─────────────────────────────
            // If employee has multiple months of zero commission → long-standing issue
            if (vm.ZeroCommissionMonths >= 2 && string.IsNullOrEmpty(vm.FinalComm?.CourId))
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Critical,
                    Category = "Commission",
                    Title    = $"Longstanding Issue — {vm.ZeroCommissionMonths} Months With Rs 0 Commission",
                    Description = $"hr_commissionprocess mein is employee ke {vm.ZeroCommissionMonths} months ka record hai jisme GrandTotal = Rs 0 aur Cour_id = NULL. "
                                + $"Yeh joining date ({vm.AppointDate?.ToString("dd-MMM-yyyy") ?? "N/A"}) se abhi tak ka masla hai. "
                                + "Route Code assign hone ke baad sirf aage ke months process honge — PURANE months ke liye bhi manually reprocess karna hoga.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_commissionprocess",
                    SuggestedAction = $"Route Code assign karein, phir hare ek zero month ke liye Commission Process Master se reprocess karein (City = {vm.CityCode ?? "N/A"}).",
                    FixSteps = [
                        "Route Code assign karein (pehle wala Critical issue dekheein).",
                        "Commission Process Master kholein.",
                        $"Hare ek zero-amount month ke liye: City = {vm.CityCode ?? "employee ki city"} | Year = X | Month = Y set kar ke run karein.",
                        $"Total {vm.ZeroCommissionMonths} months reprocess karni hogi — joining date se abhi tak.",
                        "Har month ka commission amount verify karein reprocess ke baad."
                    ]
                });

            // ── Missing CNs (billing vs commission) ───────────────────────────
            if (vm.S6Connected && vm.S6MissingCns > 0)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Warning,
                    Category = "Data",
                    Title    = $"{vm.S6MissingCns} Billing CNs Not in Commission",
                    Description = $"Server 6 shows {vm.S6TotalBillingCns} total billing CNs for this employee in the period. {vm.S6MatchedCns} matched commission records. {vm.S6MissingCns} CNs have no commission.",
                    Server      = "Server 6 — 172.16.0.6",
                    SourceTable = "lcs_billing.billing_details vs hr_cash_consignments",
                    SuggestedAction = "Review missing CN list below. If shipment type is commissionable, reprocess cash commission."
                });

            // ── OLE/Overland ───────────────────────────────────────────────────
            if (vm.S7Connected && !vm.S7OleRecords.Any() && vm.HasFinalCommission)
            {
                var oleTotal = vm.FinalComm!.OleDelivery + vm.FinalComm.OleCreditBooking + vm.FinalComm.Overland;
                if (oleTotal > 0)
                    issues.Add(new InvestigationIssue {
                        Severity = IssueSeverity.Info,
                        Category = "Commission",
                        Title    = "OLE Commission in Final but No MIS Staging Record",
                        Description = $"Final commission includes OLE amount (Rs {oleTotal:N0}) but no matching hr_olecommissionprocess record found on Server 7.",
                        Server      = "Server 7 — 172.16.0.7",
                        SourceTable = "hr_olecommissionprocess"
                    });
            }

            // ── Employee status ────────────────────────────────────────────────
            if (!vm.IsActive)
                issues.Add(new InvestigationIssue {
                    Severity = IssueSeverity.Info,
                    Category = "Employee",
                    Title    = "Employee Status: Not Active",
                    Description = $"Employee EMP_STATUS = '{vm.EmpStatus}'. Left date: {vm.LeftDate?.ToString("dd-MMM-yyyy") ?? "N/A"}.",
                    Server      = "Server 10 — 172.16.0.10",
                    SourceTable = "hr_employeepersonaldetail"
                });
        }

        // ─────────────────────────────────────────────────────────────────────
        // COMMISSION STATUS DETERMINATION
        // ─────────────────────────────────────────────────────────────────────

        private static string DetermineCommissionStatus(EmployeeCommissionInvestigationVm vm)
        {
            if (vm.IsEligible == false) return "NotEligible";
            if (!vm.HasFinalCommission && vm.PeriodTo > DateTime.Today) return "Pending";
            if (!vm.HasFinalCommission) return "Missing";
            if (vm.HasFinalCommission && vm.FinalComm!.GrandTotal > 0) return "Processed";
            // Commission record exists but GrandTotal = 0
            if (vm.FinalComm!.GrandTotal == 0 && (vm.CashCns.Any() || vm.CodCns.Any())) return "Partial";
            if (vm.FinalComm.GrandTotal == 0 && !vm.HasRouteCodes) return "ZeroNoRoute";  // process ran, no route code
            if (vm.FinalComm.GrandTotal == 0) return "ZeroAmount";                        // process ran, other reason
            return "Unknown";
        }

        // ─────────────────────────────────────────────────────────────────────
        // SQL QUERY PANEL — copy-paste queries for SQLyog verification
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildSqlQueryPanel(
            EmployeeCommissionInvestigationVm vm,
            string empNo, int year, int month, string cityCode,
            DateTime from, DateTime to)
        {
            string monthName = new DateTime(year, month, 1).ToString("MMMM").ToLower();
            vm.SqlQueries.AddRange(new[] {
                new SqlQueryPanelItem {
                    QueryKey    = "Q01_EmpMaster",
                    Title       = "Q01 — Employee Master",
                    Purpose     = "Employee basic info, status, joining/leaving date",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_employeepersonaldetail",
                    SqlText     = $@"SELECT EMP_NO, NAME, EMP_STATUS, EMPLOYEE_TYPE,
       APPOINT_DATE, LEFT_DATE, BIRTH_DATE, NIC_NO
FROM hr_employeepersonaldetail
WHERE EMP_NO = '{empNo}';"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q02_RouteCodes",
                    Title       = "Q02 — Route Codes (Main Server)",
                    Purpose     = "All route codes assigned to employee + city mapping",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_employeeroutecode + hr_city",
                    SqlText     = $@"SELECT erc.RouteCode, erc.citycode, hc.FullName AS CityName,
       hc.station_id, erc.LocationId, erc.FromDate, erc.ToDate,
       erc.CodeType, erc.RBIExclude
FROM hr_employeeroutecode erc
LEFT JOIN hr_city hc ON hc.Code = erc.citycode
WHERE erc.Emp_No = '{empNo}'
ORDER BY erc.FromDate DESC;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q03_Eligibility",
                    Title       = "Q03 — Commission Eligibility",
                    Purpose     = "Is employee eligible for commission? (Master table — no Year/Month filter)",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_empcommissioneligibility",
                    SqlText     = $@"-- hr_empcommissioneligibility is a MASTER setup table (no Year/Month columns)
SELECT Emp_no, CommissionId, IsEligible, CreatedDate, UpdatedDate
FROM hr_empcommissioneligibility
WHERE Emp_no = '{empNo}';"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q04_Attendance",
                    Title       = "Q04 — Attendance Process",
                    Purpose     = "Working days, absences, adjustments — used for commission proration",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_employeeattendanceprocess",
                    SqlText     = $@"SELECT *
FROM hr_employeeattendanceprocess
WHERE Emp_No = '{empNo}' AND Year = {year} AND Month = {month};"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q05_CashCns",
                    Title       = "Q05 — Cash Commission CNs",
                    Purpose     = "All cash CNs processed for employee's route code(s) this period (table uses cour_id, not Emp_No)",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_cash_consignments",
                    SqlText     = $@"-- hr_cash_consignments uses cour_id (RouteCode), NOT Emp_No
SELECT cn_number, billing_date, Shipment_id, cour_id,
       Station_id, Gross_Amount, TotalCommission, Criteria,
       Weight_KG, Billing_Type
FROM hr_cash_consignments
WHERE cour_id IN (
    SELECT RouteCode FROM hr_employeeroutecode WHERE Emp_No = '{empNo}'
)
  AND billing_date BETWEEN '{from:yyyy-MM-dd}' AND '{to:yyyy-MM-dd}'
ORDER BY billing_date DESC;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q06_CodCns",
                    Title       = "Q06 — COD Commission CNs",
                    Purpose     = "All COD CNs for this employee's route code(s)",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_cod_consignments + hr_employeeroutecode",
                    SqlText     = $@"SELECT hcc.CN_Number, hcc.Cour_date, hcc.Delivery_date,
       hcc.DateDif, hcc.ComAmnt, hcc.Reason, hcc.Cour_id
FROM hr_cod_consignments hcc
JOIN hr_employeeroutecode erc
    ON erc.RouteCode = hcc.Cour_id
   AND erc.Emp_No    = '{empNo}'
WHERE hcc.Cyear = {year} AND hcc.CMonth = {month}
ORDER BY hcc.Cour_date DESC;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q07_ReturnCod",
                    Title       = "Q07 — Return COD Commission",
                    Purpose     = "Return COD commission by route code (table uses CourierID, OleCommission — no Emp_No)",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_codreturncommissionprocess",
                    SqlText     = $@"-- hr_codreturncommissionprocess uses CourierID (RouteCode), OleCommission
SELECT CourierID, GlLocationId, Year, Month, RateId,
       OleCommission, CreatedDate
FROM hr_codreturncommissionprocess
WHERE Year = {year} AND Month = {month}
  AND CourierID IN (
    SELECT RouteCode FROM hr_employeeroutecode WHERE Emp_No = '{empNo}'
  );"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q08_FinalComm",
                    Title       = "Q08 — Final Commission Record",
                    Purpose     = "Final processed commission — all 90+ columns",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_commissionprocess",
                    SqlText     = $@"SELECT emp_no, citycode, Year, Month,
       OVERNIGHT, COD, COD_Bonus, COD_Deduction,
       OLE_Delivery, OLE_Credit_Booking, OVERLAND,
       YB1KG, YB2KG, YB5KG, YB10KG, YB15KG, YB25KG,
       FLAYER, DETAIN, PREPAID, LOVELINE, VAS,
       General_Light_Delivery, General_Heavy_Delivery,
       CASH_Economy_Booking, Retail_Deduction,
       Ecom_overall_SR_Bonus,
       {GtSql} AS GrandTotal
FROM hr_commissionprocess
WHERE emp_no = '{empNo}' AND Year = {year} AND Month = {month}
  AND citycode = '{cityCode}';"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q09_RatePolicy",
                    Title       = "Q09 — Commission Rate Policies",
                    Purpose     = "All rate IDs used — flat amount vs percentage, policy type",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_commissionpolicy",
                    SqlText     = $@"SELECT RateID, ProductId, Type, Rate, IsPercent, RateType, Comments
FROM hr_commissionpolicy
ORDER BY RateID;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q10_S6RouteCodes",
                    Title       = "Q10 — Route Codes on Server 6 (Billing)",
                    Purpose     = "Verify route codes in lcs_hr on Billing server (must match Main)",
                    Server      = "Server 6 (Billing — LHR_Billing)",
                    Database    = "lcs_hr",
                    SourceTable = "lcs_hr.hr_employeeroutecode",
                    SqlText     = $@"-- Run this on Server 6 (172.16.0.6)
-- Table is in lcs_hr database, not lcs_billing
SELECT RouteCode, citycode, LocationId, FromDate, ToDate
FROM lcs_hr.hr_employeeroutecode
WHERE Emp_No = '{empNo}';"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q11_S6BillingCns",
                    Title       = "Q11 — Billing CNs from Server 6",
                    Purpose     = "Raw billing deliveries — source data for cash commission (cour_id = RouteCode)",
                    Server      = "Server 6 (Billing — LHR_Billing)",
                    Database    = "lcs_billing",
                    SourceTable = "lcs_billing.billing_details",
                    SqlText     = $@"-- Run this on Server 6 (172.16.0.6)
-- billing_details columns: cn_number (not cn_no), amount (not billed_amount)
SELECT bd.cn_number, bd.billing_date, bd.shipment_type_id,
       bd.amount AS BilledAmount, bd.cour_id, bd.Station_id
FROM lcs_billing.billing_details bd
WHERE bd.billing_date BETWEEN '{from:yyyy-MM-dd}' AND '{to:yyyy-MM-dd}'
  AND bd.cour_id IN (
    SELECT RouteCode FROM lcs_hr.hr_employeeroutecode WHERE Emp_No = '{empNo}'
  )
ORDER BY bd.billing_date DESC;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q12_S6MissingCns",
                    Title       = "Q12 — Missing CNs (Billing vs Commission)",
                    Purpose     = "CNs in billing that have no commission record — investigate why",
                    Server      = "Server 6 → Server 10 cross-check",
                    Database    = "lcs_billing + lcs_hr",
                    SourceTable = "billing_details vs hr_cash_consignments",
                    SqlText     = $@"-- Step 1: Get CNs processed in commission (run on Server 10)
SELECT cn_number FROM hr_cash_consignments
WHERE cour_id IN (
    SELECT RouteCode FROM hr_employeeroutecode WHERE Emp_No = '{empNo}'
)
  AND billing_date BETWEEN '{from:yyyy-MM-dd}' AND '{to:yyyy-MM-dd}';

-- Step 2: All billing CNs from Server 6 — compare with Step 1
-- Any CN in Step 2 NOT in Step 1 = MISSING commission
SELECT bd.cn_number, bd.billing_date, bd.shipment_type_id, bd.amount
FROM lcs_billing.billing_details bd
WHERE bd.billing_date BETWEEN '{from:yyyy-MM-dd}' AND '{to:yyyy-MM-dd}'
  AND bd.cour_id IN (
    SELECT RouteCode FROM lcs_hr.hr_employeeroutecode WHERE Emp_No = '{empNo}'
  )
ORDER BY bd.billing_date DESC;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q13_S10Location",
                    Title       = "Q13 — Employee Location Details (S10 — CORRECTED)",
                    Purpose     = "Employee location + StationId resolution from S10 lcs_hr — NO CityCode column in this table",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_employeelocationdetails + lcs_setup.locations",
                    SqlText     = $@"-- Run this on Server 10 (172.16.0.10)
-- hr_employeelocationdetails has: Emp_No, LocationId, FromDate, ToDate  (NO CityCode column!)
-- StationId resolution: LocationId → lcs_setup.locations.BILLINGCITYID → LPAD to 5 chars
SELECT eld.Emp_No, eld.LocationId, eld.FromDate, eld.ToDate,
       l.LocationName,
       LPAD(CAST(l.BILLINGCITYID AS CHAR), 5, '0') AS StationId
FROM lcs_hr.hr_employeelocationdetails eld
LEFT JOIN lcs_setup.locations l ON l.LocationID = eld.LocationId
WHERE eld.Emp_No = '{empNo}'
ORDER BY eld.ToDate IS NULL DESC, eld.FromDate DESC;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q14_S7Ole",
                    Title       = "Q14 — OLE Commission on Server 7 (MIS)",
                    Purpose     = "Overland commission staging data for this employee",
                    Server      = "Server 7 (MIS)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_olecommissionprocess",
                    SqlText     = $@"-- Run this on Server 7 (172.16.0.7)
-- Table is in lcs_hr database, not lcs_db
SELECT ole.GlLocationId, ole.CourierID, ole.RateId,
       ole.OleCommission, ole.CreatedDate
FROM lcs_hr.hr_olecommissionprocess ole
WHERE ole.Year = {year} AND ole.Month = {month}
  AND ole.CourierID IN (
    SELECT RouteCode FROM lcs_hr.hr_employeeroutecode WHERE Emp_No = '{empNo}'
  );"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q15_CitySchema",
                    Title       = "Q15 — City Code Schema Verification",
                    Purpose     = "Verify station_id vs Code mapping — critical for all JOINs",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_city",
                    SqlText     = $@"-- Verify: Code='001' ≠ station_id='00592' — NOT numerically related!
SELECT Code, FullName, station_id
FROM hr_city
WHERE Code = '{cityCode}' OR FullName LIKE '%{vm.CityName?.Split(' ').FirstOrDefault() ?? cityCode}%'
ORDER BY FullName;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q16_RbiDetail",
                    Title       = "Q16 — RBI Per-CN Detail (S10)",
                    Purpose     = "RBI/Overland per-CN incentive records — S10 lcs_hr (63M rows). Use Cour_Id, station_id to filter",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "lcs_hr.hr_rbi_incentive_detail",
                    SqlText     = $@"-- S10 lcs_hr — Verified columns: Cour_Id (capital I), FinalIncentive, Total_Weight, Total_Amount, Created_Date
-- station_id = StationId resolved from LocationId (see Q13). For {empNo}: StationId = '{vm.ResolvedStationId ?? "run Q13 first"}'
SELECT rbi.Cour_Id, rbi.station_id, rbi.CN_Number,
       rbi.Total_Weight, rbi.Total_Amount, rbi.FinalIncentive,
       rbi.Created_Date, rbi.RateId
FROM lcs_hr.hr_rbi_incentive_detail rbi
WHERE rbi.year = {year} AND rbi.month = {month}
  AND rbi.station_id = '{vm.ResolvedStationId ?? ""}'
  AND rbi.Cour_Id IN (
    SELECT RouteCode FROM lcs_hr.hr_employeeroutecode WHERE Emp_No = '{empNo}'
  )
ORDER BY rbi.Created_Date DESC
LIMIT 500;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q17_VasDetail",
                    Title       = "Q17 — VAS/General Per-CN Detail (S10)",
                    Purpose     = "VAS incentive per-CN records — Ecommerce Zero COD, SOA, Utility Bill, Passport, CNIC etc.",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "lcs_hr.hr_ole_vas_incentive_detail",
                    SqlText     = $@"-- S10 lcs_hr — Verified columns: COURIER_ID, Incentive, OPS_Weight_KG, DELIVERY_DATE, Category, shipment_type
-- ARVL_DEST = station_id (destination station). For {empNo}: StationId = '{vm.ResolvedStationId ?? "run Q13 first"}'
SELECT vas.COURIER_ID, vas.CN_NUMBER, vas.DELIVERY_DATE,
       vas.OPS_Weight_KG, vas.Incentive,
       vas.Category, vas.shipment_type, vas.RateId,
       vas.ARVL_DEST AS StationId
FROM lcs_hr.hr_ole_vas_incentive_detail vas
WHERE vas.DELIVERY_DATE BETWEEN '{from:yyyy-MM-dd}' AND '{to:yyyy-MM-dd}'
  AND vas.ARVL_DEST = '{vm.ResolvedStationId ?? ""}'
  AND vas.COURIER_ID IN (
    SELECT RouteCode FROM lcs_hr.hr_employeeroutecode WHERE Emp_No = '{empNo}'
  )
ORDER BY vas.DELIVERY_DATE DESC
LIMIT 500;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q18_CodReturnDetail",
                    Title       = "Q18 — COD Return Per-CN Detail (S10)",
                    Purpose     = "COD Return per-CN records with statuses DAS/DR/DW",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "lcs_hr.hr_codreturn_consignments",
                    SqlText     = $@"-- S10 lcs_hr — Verified columns: COURIER_ID, CN_NUMBER, OpsInc, RatePerShipment, DELIVERY_DATE, STATUS, Station_id
-- Statuses: DAS, DR, DW
SELECT crc.COURIER_ID, crc.CN_NUMBER, crc.Station_id,
       crc.DELIVERY_DATE, crc.STATUS,
       crc.OpsInc AS Commission, crc.RatePerShipment AS Amount,
       crc.RateId
FROM lcs_hr.hr_codreturn_consignments crc
WHERE crc.Year = {year} AND crc.Month = {month}
  AND crc.Station_id = '{vm.ResolvedStationId ?? ""}'
  AND crc.COURIER_ID IN (
    SELECT RouteCode FROM lcs_hr.hr_employeeroutecode WHERE Emp_No = '{empNo}'
  )
ORDER BY crc.DELIVERY_DATE DESC
LIMIT 500;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q19_Adjustments",
                    Title       = "Q19 — Commission Adjustments (S10)",
                    Purpose     = "Manual commission adjustments for employee — types: RBI Billing (1), Cash Billing (2)",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_empcommadjdtl + adjusment_policy",
                    SqlText     = $@"-- hr_empcommadjdtl JOIN adjusment_policy (note: adjusment not adjustment — typo in table name)
-- Types: Id=1 RBI Billing, Id=2 Cash Billing
SELECT adj.Id, adj.Emp_No, adj.year, adj.month,
       adj.Amount, adj.Remarks,
       ap.policy_name AS PolicyName,
       ap.Id AS PolicyId
FROM hr_empcommadjdtl adj
LEFT JOIN adjusment_policy ap ON ap.Id = adj.adjusment_policy_id
WHERE adj.Emp_No = '{empNo}' AND adj.year = {year} AND adj.month = {month}
ORDER BY adj.Id DESC;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q20_SrBonus",
                    Title       = "Q20 — SR Bonus / Ecom Overall Bonus (S10)",
                    Purpose     = "SR Bonus eligibility record — only Eligible employees get Ecom_overall_SR_Bonus in Final Commission",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_incentive_overall_SR",
                    SqlText     = $@"-- is_eligible = 'Eligible' → Ecom_overall_SR_Bonus column in hr_commissionprocess gets value
SELECT emp_no, year, month, is_eligible,
       TotalCn, EligibleCn, BonusAmount
FROM hr_incentive_overall_SR
WHERE emp_no = '{empNo}' AND year = {year} AND month = {month};"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q21_OleCommDetail",
                    Title       = "Q21 — OLE Commission Per-CN Detail (S10)",
                    Purpose     = "OLE (Cargo/Overland) per-CN commission detail — CodeType=7 Cargo Officers",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_olecommission",
                    SqlText     = $@"-- hr_olecommission — filter by stationid + courierid
-- stationid here = station_id from hr_city (5-char code like '00592')
-- For {empNo}: StationId = '{vm.ResolvedStationId ?? "run Q13 first"}'
SELECT ole.stationid, ole.courierid, ole.cn_number,
       ole.comyear, ole.commonth,
       ole.OleCommission, ole.RateId,
       ole.CreatedDate
FROM hr_olecommission ole
WHERE ole.comyear = {year} AND ole.commonth = {month}
  AND ole.stationid = '{vm.ResolvedStationId ?? ""}'
  AND ole.courierid IN (
    SELECT RouteCode FROM hr_employeeroutecode WHERE Emp_No = '{empNo}'
  )
ORDER BY ole.CreatedDate DESC
LIMIT 500;"
                },
                new SqlQueryPanelItem {
                    QueryKey    = "Q22_OleCommSummary",
                    Title       = "Q22 — OLE Commission Summary (S10)",
                    Purpose     = "OLE commission compiled summary per route code — hr_olecommissionprocess on S10",
                    Server      = "Server 10 (Main HR)",
                    Database    = "lcs_hr",
                    SourceTable = "hr_olecommissionprocess",
                    SqlText     = $@"-- hr_olecommissionprocess summary table
-- GlLocationId = LocationId from lcs_setup.locations
SELECT ole.GlLocationId, ole.Cour_id AS CourierID, ole.RateId,
       ole.OleCommission, ole.Year, ole.Month, ole.CreatedDate
FROM hr_olecommissionprocess ole
WHERE ole.Year = {year} AND ole.Month = {month}
  AND ole.Cour_id IN (
    SELECT RouteCode FROM hr_employeeroutecode WHERE Emp_No = '{empNo}'
  );"
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // CRUD — Investigation Notes
        // ─────────────────────────────────────────────────────────────────────

        public async Task<List<InvestigationNote>> GetNotesAsync(string empNo, int year, int month)
        {
            try
            {
                using var conn = OpenMain();
                var rows = await conn.QueryAsync<InvestigationNote>(@"
                    SELECT Id, EmpNo, Year, Month, ActionType, Notes,
                           CreatedBy, CreatedDate, Status, ResolvedBy, ResolvedDate, IsDeleted
                    FROM hr_commission_investigation_notes
                    WHERE EmpNo = @EmpNo AND Year = @Year AND Month = @Month AND IsDeleted = 0
                    ORDER BY CreatedDate DESC",
                    new { EmpNo = empNo, Year = year, Month = month });
                return rows.ToList();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "GetNotesAsync failed");
                return new List<InvestigationNote>();
            }
        }

        public async Task<int> CreateNoteAsync(CreateNoteRequest req, string createdBy)
        {
            try
            {
                using var conn = OpenMain();
                return await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO hr_commission_investigation_notes
                        (EmpNo, Year, Month, CityCode, ActionType, Notes, CreatedBy, Status)
                    VALUES
                        (@EmpNo, @Year, @Month, @CityCode, @ActionType, @Notes, @CreatedBy, @Status);
                    SELECT LAST_INSERT_ID();",
                    new {
                        req.EmpNo, req.Year, req.Month,
                        CityCode   = req.CityCode ?? "",
                        req.ActionType, req.Notes,
                        CreatedBy  = createdBy,
                        req.Status
                    });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "CreateNoteAsync failed");
                return 0;
            }
        }

        public async Task<bool> UpdateNoteStatusAsync(int noteId, string newStatus, string updatedBy)
        {
            try
            {
                using var conn = OpenMain();
                var affected = await conn.ExecuteAsync(@"
                    UPDATE hr_commission_investigation_notes
                    SET Status = @Status,
                        ResolvedBy   = CASE WHEN @Status = 'Resolved' THEN @UpdatedBy ELSE ResolvedBy END,
                        ResolvedDate = CASE WHEN @Status = 'Resolved' THEN NOW()       ELSE ResolvedDate END
                    WHERE Id = @Id AND IsDeleted = 0",
                    new { Id = noteId, Status = newStatus, UpdatedBy = updatedBy });
                return affected > 0;
            }
            catch { return false; }
        }

        public async Task<bool> SoftDeleteNoteAsync(int noteId, string deletedBy)
        {
            try
            {
                using var conn = OpenMain();
                var affected = await conn.ExecuteAsync(
                    "UPDATE hr_commission_investigation_notes SET IsDeleted = 1 WHERE Id = @Id",
                    new { Id = noteId });
                return affected > 0;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static int? GetInt(IDictionary<string, object> d, params string[] keys)
        {
            foreach (var k in keys)
                if (d.TryGetValue(k, out var v) && v != null && v != DBNull.Value
                    && int.TryParse(v.ToString(), out var i)) return i;
            return null;
        }

        private static string? GetStr(IDictionary<string, object> d, params string[] keys)
        {
            foreach (var k in keys)
                if (d.TryGetValue(k, out var v) && v != null && v != DBNull.Value) return v.ToString();
            return null;
        }
    }
}
