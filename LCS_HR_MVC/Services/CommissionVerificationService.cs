using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    /// <summary>
    /// Commission verification service.
    ///
    /// KEY SCHEMA FACTS (verified on live DB 2026-04-02):
    ///   hr_city.Code          = 3-char city code (e.g. '001')
    ///   hr_city.station_id    = 5-char station id (e.g. '00592')  — NOT related to Code numerically
    ///   hr_cash_consignments.Station_id = hr_city.station_id  → join via station_id=Station_id, then use hc.Code
    ///   hr_cod_consignments.Arivl_Dest  = hr_city.station_id  → same pattern
    ///   hr_employeeroutecode.citycode   = hr_city.Code (3-char)
    ///   hr_cod_consignments.Cour_id     = varchar(5) = hr_employeeroutecode.RouteCode  (NOT emp_no)
    ///   hr_cash_consignments.cour_id    = varchar(5) = hr_employeeroutecode.RouteCode
    ///   hr_commissionprocess grand total includes CASH_Economy_Booking and 60+ columns.
    /// </summary>
    public class CommissionVerificationService : ICommissionVerificationService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly ILogger<CommissionVerificationService> _logger;

        // Commission period: 21st of prev month → 20th of selected month
        private const int PeriodStartDay = 21;
        private const int PeriodEndDay   = 20;

        // Grand total of ALL commission columns in hr_commissionprocess (verified 2026-04-02).
        // Deductions: Retail_Deduction, COD_Deduction are subtracted.
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

        public CommissionVerificationService(
            IDbConnectionFactory connectionFactory,
            ILogger<CommissionVerificationService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger            = logger;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private MySqlConnection OpenConnection()
        {
            var conn = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot open DB connection.");
            conn.Open();
            return conn;
        }

        private static (DateTime From, DateTime To) GetPeriod(int year, int month)
        {
            var from = new DateTime(year, month, PeriodStartDay).AddMonths(-1);
            var to   = new DateTime(year, month, PeriodEndDay);
            return (from, to);
        }

        // ── Available years ──────────────────────────────────────────────────────

        public async Task<List<int>> GetAvailableYearsAsync()
        {
            try
            {
                using var conn = OpenConnection();
                var years = await conn.QueryAsync<int>(
                    "SELECT DISTINCT Year FROM hr_commissionprocess ORDER BY Year DESC");
                var list = years.ToList();
                if (!list.Contains(DateTime.Now.Year))
                    list.Insert(0, DateTime.Now.Year);
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetAvailableYearsAsync failed.");
                return new List<int> { DateTime.Now.Year };
            }
        }

        // ── Available cities ─────────────────────────────────────────────────────

        public async Task<List<(string Code, string Name)>> GetCitiesAsync()
        {
            try
            {
                using var conn = OpenConnection();
                // hr_city.FullName is the city name column (NOT city_name — verified on live DB)
                var rows = await conn.QueryAsync<(string Code, string Name)>(
                    "SELECT Code, FullName FROM hr_city WHERE Code IS NOT NULL AND Code <> '' ORDER BY FullName");
                return rows.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetCitiesAsync failed.");
                return new List<(string, string)>();
            }
        }

        // ── All commissions paged (default view, Mode 1) ─────────────────────────
        // CITY JOIN FIX: hr_city.station_id = cc.Station_id → hc.Code = citycode
        // COD EMP FIX:  Cour_id is RouteCode (5-char) → join via hr_employeeroutecode

        public async Task<(List<CommissionFlatRow> Rows, int TotalCount)> GetAllCommissionsPagedAsync(
            CommissionVerificationFilter filter, int page, int pageSize)
        {
            try
            {
                using var conn = OpenConnection();
                var (fromDate, toDate) = GetPeriod(filter.Year, filter.Month);
                var city     = filter.CityCode            ?? "";
                var commType = filter.CommissionTypeFilter ?? "All";
                var offset   = (page - 1) * pageSize;

                var p = new
                {
                    From     = fromDate,
                    To       = toDate,
                    Year     = filter.Year,
                    Month    = filter.Month,
                    City     = city,
                    CommType = commType,
                    PageSize = pageSize,
                    Offset   = offset
                };

                // ── Count — subquery for city avoids slow JOIN on full table ────
                int cashCount = 0, codCount = 0;

                if (commType == "All" || commType == "Cash")
                {
                    cashCount = await conn.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(*)
                        FROM hr_cash_consignments cc
                        WHERE cc.billing_date BETWEEN @From AND @To
                          AND (@City = '' OR cc.Station_id =
                              (SELECT station_id FROM hr_city WHERE Code = @City LIMIT 1))", p);
                }

                if (commType == "All" || commType == "COD")
                {
                    codCount = await conn.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(*)
                        FROM hr_cod_consignments hcc
                        WHERE hcc.Cyear = @Year AND hcc.CMonth = @Month
                          AND (@City = '' OR hcc.Arivl_Dest =
                              (SELECT station_id FROM hr_city WHERE Code = @City LIMIT 1))", p);
                }

                var totalCount = cashCount + codCount;
                if (totalCount == 0)
                    return (new List<CommissionFlatRow>(), 0);

                // ── Paginated data ────────────────────────────────────────────────
                // CASH: join hr_city via station_id → get hc.Code → match erc.citycode
                // COD:  Cour_id is RouteCode → join erc on RouteCode+citycode
                var sql = @"
                    SELECT CnDate, EmpNo, EmpName, RouteCode, CityCode, CityName,
                           CnNumber, CommissionType, ShipmentId, Criteria,
                           DeliveryDate, DateDif, IsOnTime, CommissionAmount, Reason
                    FROM (

                        -- CASH side
                        SELECT
                            cc.billing_date                      AS CnDate,
                            IFNULL(erc.Emp_No,   '')             AS EmpNo,
                            IFNULL(epd.NAME,     '')             AS EmpName,
                            IFNULL(erc.RouteCode,'')             AS RouteCode,
                            IFNULL(hc.Code,      '')             AS CityCode,
                            IFNULL(hc.FullName,  '')             AS CityName,
                            cc.cn_number                         AS CnNumber,
                            'Cash'                               AS CommissionType,
                            cc.Shipment_id                       AS ShipmentId,
                            cc.Criteria                          AS Criteria,
                            NULL                                 AS DeliveryDate,
                            NULL                                 AS DateDif,
                            TRUE                                 AS IsOnTime,
                            IFNULL(cc.TotalCommission, 0)        AS CommissionAmount,
                            NULL                                 AS Reason
                        FROM hr_cash_consignments cc
                        LEFT JOIN hr_city hc ON hc.station_id = cc.Station_id
                        LEFT JOIN hr_employeeroutecode erc
                            ON  erc.RouteCode = cc.cour_id
                            AND erc.citycode  = hc.Code
                            AND erc.ToDate IS NULL
                        LEFT JOIN hr_employeepersonaldetail epd ON epd.EMP_NO = erc.Emp_No
                        WHERE cc.billing_date BETWEEN @From AND @To
                          AND (@City     = '' OR hc.Code = @City)
                          AND (@CommType = 'All' OR @CommType = 'Cash')

                        UNION ALL

                        -- COD side — Cour_id is 5-char RouteCode, NOT emp_no
                        SELECT
                            hcc.Cour_date                                               AS CnDate,
                            IFNULL(erc.Emp_No,   '')                                    AS EmpNo,
                            IFNULL(epd.NAME,     '')                                    AS EmpName,
                            IFNULL(hcc.Cour_id,  '')                                    AS RouteCode,
                            IFNULL(hc.Code,      '')                                    AS CityCode,
                            IFNULL(hc.FullName,  '')                                    AS CityName,
                            hcc.CN_Number                                               AS CnNumber,
                            'COD'                                                       AS CommissionType,
                            NULL                                                        AS ShipmentId,
                            NULL                                                        AS Criteria,
                            hcc.Delivery_date                                           AS DeliveryDate,
                            hcc.DateDif                                                 AS DateDif,
                            CASE WHEN IFNULL(hcc.DateDif, 0) <= 0 THEN TRUE
                                 ELSE FALSE END                                         AS IsOnTime,
                            IFNULL(hcc.ComAmnt, 0)                                     AS CommissionAmount,
                            hcc.Reason                                                  AS Reason
                        FROM hr_cod_consignments hcc
                        LEFT JOIN hr_city hc ON hc.station_id = hcc.Arivl_Dest
                        LEFT JOIN hr_employeeroutecode erc
                            ON  erc.RouteCode = hcc.Cour_id
                            AND erc.citycode  = hc.Code
                            AND erc.ToDate IS NULL
                        LEFT JOIN hr_employeepersonaldetail epd ON epd.EMP_NO = erc.Emp_No
                        WHERE hcc.Cyear = @Year AND hcc.CMonth = @Month
                          AND (@City     = '' OR hc.Code = @City)
                          AND (@CommType = 'All' OR @CommType = 'COD')

                    ) t
                    ORDER BY CnDate DESC
                    LIMIT @PageSize OFFSET @Offset";

                var rows = await conn.QueryAsync<CommissionFlatRow>(sql, p);
                return (rows.ToList(), totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllCommissionsPagedAsync failed for {Year}/{Month}.", filter.Year, filter.Month);
                return (new List<CommissionFlatRow>(), 0);
            }
        }

        // ── Processed commissions paged (from hr_commissionprocess) ─────────────

        public async Task<(List<ProcessedCommissionRow> Rows, int TotalCount)> GetProcessedCommissionsPagedAsync(
            CommissionVerificationFilter filter, int page, int pageSize)
        {
            try
            {
                using var conn = OpenConnection();
                var city   = filter.CityCode ?? "";
                var offset = (page - 1) * pageSize;

                var p = new { Year = filter.Year, Month = filter.Month, City = city, PageSize = pageSize, Offset = offset };

                var totalCount = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM hr_commissionprocess
                    WHERE Year = @Year AND Month = @Month
                      AND (@City = '' OR citycode = @City)", p);

                if (totalCount == 0)
                    return (new List<ProcessedCommissionRow>(), 0);

                // hr_city.Code = hcp.citycode (both are 3-char codes) — this join is correct
                var sql = $@"
                    SELECT
                        hcp.emp_no                    AS EmpNo,
                        IFNULL(epd.NAME, '')          AS EmpName,
                        IFNULL(hcp.citycode, '')      AS CityCode,
                        IFNULL(hc.FullName, '')       AS CityName,
                        hcp.Year, hcp.Month,
                        IFNULL(hcp.OVERNIGHT,0)       AS Overnight,
                        IFNULL(hcp.COD,0)             AS Cod,
                        IFNULL(hcp.COD_Bonus,0)       AS CodBonus,
                        IFNULL(hcp.COD_Deduction,0)   AS CodDeduction,
                        IFNULL(hcp.OLE_Delivery,0)    AS OleDelivery,
                        IFNULL(hcp.OVERLAND,0)        AS Overland,
                        IFNULL(hcp.VAS,0)             AS Vas,
                        (IFNULL(hcp.YB1KG,0)+IFNULL(hcp.YB2KG,0)+IFNULL(hcp.YB5KG,0)+
                         IFNULL(hcp.YB10KG,0)+IFNULL(hcp.YB15KG,0)+IFNULL(hcp.YB25KG,0)+
                         IFNULL(hcp.FLAYER,0)+IFNULL(hcp.DETAIN,0)+IFNULL(hcp.PREPAID,0)+
                         IFNULL(hcp.LOVELINE,0)+IFNULL(hcp.DOM_CREDIT,0)+IFNULL(hcp.LOCAL_CREDIT,0)+
                         IFNULL(hcp.LOCAL_DLD,0)+IFNULL(hcp.PMCL,0)+IFNULL(hcp.DomesticDelivery,0)+
                         IFNULL(hcp.INTL_CREDIT,0)+IFNULL(hcp.Porter,0)+IFNULL(hcp.INTL_CASH,0)+
                         IFNULL(hcp.OLE_Credit_Booking,0)+IFNULL(hcp.OLE_Dispatch_Proper,0)+
                         IFNULL(hcp.OLE_Transit_Dispatch,0)+IFNULL(hcp.OLE_Delivery_OPS,0)+
                         IFNULL(hcp.MOFA_OTO,0)+IFNULL(hcp.MOFA_OTD,0)+IFNULL(hcp.Rms_Cod_Booking,0)+
                         IFNULL(hcp.AllInOne,0)+IFNULL(hcp.DocumnetCare,0)+IFNULL(hcp.MTD,0)+
                         IFNULL(hcp.IntlDox,0)+IFNULL(hcp.IntlEconomy,0)+IFNULL(hcp.IntlParcel,0)+
                         IFNULL(hcp.ONUpto1kg,0)+IFNULL(hcp.ONAbove1kg,0)+IFNULL(hcp.ONUpto1kgRetailCOD,0)+
                         IFNULL(hcp.ONAbove1kgRetailCOD,0)+IFNULL(hcp.EconomyRetail,0)+
                         IFNULL(hcp.YB1KGRetail,0)+IFNULL(hcp.YB2KGRetail,0)+IFNULL(hcp.YB5KGRetail,0)+
                         IFNULL(hcp.YB10KGRetail,0)+IFNULL(hcp.YB15KGRetail,0)+IFNULL(hcp.YB25KGRetail,0)+
                         IFNULL(hcp.MyCollect,0)+IFNULL(hcp.Attestation,0)+
                         IFNULL(hcp.CEB_UpTo_2Kg,0)+IFNULL(hcp.CEB_Above_2Kg,0)+
                         IFNULL(hcp.Cor_Economy_Booking,0)+IFNULL(hcp.Cor_Ole_Booking,0)+
                         IFNULL(hcp.CEB_Upto_2KG_Exis,0)+IFNULL(hcp.CEB_Upto_2KG_New,0)+
                         IFNULL(hcp.CEB_Above_2Kg_Exis,0)+IFNULL(hcp.CEB_Above_2Kg_New,0)+
                         IFNULL(hcp.ECON_Credit_Booking_Exis,0)+IFNULL(hcp.ECON_Credit_Booking_New,0)+
                         IFNULL(hcp.OLE_CORP_Booking_Exis,0)+IFNULL(hcp.OLE_CORP_Booking_New,0)+
                         IFNULL(hcp.Project_Local_Exis,0)+IFNULL(hcp.Project_Local_New,0)+
                         IFNULL(hcp.Project_Domestic_Exis,0)+IFNULL(hcp.Project_Domestic_New,0)+
                         IFNULL(hcp.CASH_EXP_BKG_UpTo_2Kg,0)+IFNULL(hcp.CASH_EXP_BKG_Above_2Kg,0)+
                         IFNULL(hcp.CASH_Leop_BOX_Above_2Kg,0)+IFNULL(hcp.CASH_Economy_Booking,0)+
                         IFNULL(hcp.CASH_OLE_Booking,0)+IFNULL(hcp.Insurance_Com,0)+
                         IFNULL(hcp.Credit_Debit_Card,0)+IFNULL(hcp.ECommerce_Zero_COD,0)+
                         IFNULL(hcp.Passport,0)+IFNULL(hcp.CNIC_Card,0)+IFNULL(hcp.Return_E_Com,0)+
                         IFNULL(hcp.Pickup_Leopard,0)+IFNULL(hcp.SOA,0)+IFNULL(hcp.Utility_Bill,0)+
                         IFNULL(hcp.General_Light_Delivery,0)+IFNULL(hcp.General_Heavy_Delivery,0)+
                         IFNULL(hcp.MTD_Delivery,0)+IFNULL(hcp.Giftwifts_Delivery,0)+
                         IFNULL(hcp.Ecom_overall_SR_Bonus,0)
                         -IFNULL(hcp.Retail_Deduction,0))  AS OtherCash
                    FROM hr_commissionprocess hcp
                    LEFT JOIN hr_employeepersonaldetail epd ON epd.EMP_NO = hcp.emp_no
                    LEFT JOIN hr_city hc ON hc.Code = hcp.citycode
                    WHERE hcp.Year = @Year AND hcp.Month = @Month
                      AND (@City = '' OR hcp.citycode = @City)
                    ORDER BY {GtSql} DESC
                    LIMIT @PageSize OFFSET @Offset";

                var rows = await conn.QueryAsync<ProcessedCommissionRow>(sql, p);
                return (rows.ToList(), totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetProcessedCommissionsPagedAsync failed for {Year}/{Month}.", filter.Year, filter.Month);
                return (new List<ProcessedCommissionRow>(), 0);
            }
        }

        // ── Employee Search ──────────────────────────────────────────────────────
        // hr_city join fix: hc.Code = erc.citycode (direct match, no LPAD needed)

        public async Task<List<EmployeeCommissionSummary>> SearchEmployeesAsync(CommissionVerificationFilter filter)
        {
            try
            {
                using var conn = OpenConnection();
                var empNoSearch   = (filter.EmpNoSearch   ?? "").Trim();
                var empNameSearch = (filter.EmpNameSearch ?? "").Trim();

                if (string.IsNullOrEmpty(empNoSearch) && string.IsNullOrEmpty(empNameSearch))
                    return new List<EmployeeCommissionSummary>();

                var sql = @"
                    SELECT DISTINCT
                        erc.Emp_No    AS EmpNo,
                        epd.NAME      AS EmpName,
                        erc.RouteCode AS RouteCode,
                        erc.citycode  AS CityCode,
                        hc.FullName   AS CityName
                    FROM hr_employeeroutecode erc
                    INNER JOIN hr_employeepersonaldetail epd ON epd.EMP_NO = erc.Emp_No
                    INNER JOIN hr_city hc ON hc.Code = erc.citycode
                    WHERE erc.ToDate IS NULL
                      AND (@EmpNo = '' OR erc.Emp_No  LIKE @EmpNoLike)
                      AND (@Name  = '' OR epd.NAME     LIKE @NameLike)
                      AND (@City  = '' OR erc.citycode = @City)
                    ORDER BY epd.NAME
                    LIMIT 50";

                var rows = await conn.QueryAsync<EmployeeCommissionSummary>(sql, new
                {
                    EmpNo     = empNoSearch,
                    EmpNoLike = $"%{empNoSearch}%",
                    Name      = empNameSearch,
                    NameLike  = $"%{empNameSearch}%",
                    City      = filter.CityCode ?? ""
                });

                return rows.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchEmployeesAsync failed.");
                return new List<EmployeeCommissionSummary>();
            }
        }

        // ── Employee Detail (full) ───────────────────────────────────────────────

        public async Task<(EmployeeCommissionSummary Summary, List<CnCommissionRow> Cns, List<MissingCnRow> MissingCns)>
            GetEmployeeDetailAsync(string empNo, int year, int month, string cityCode)
        {
            var summary    = new EmployeeCommissionSummary { EmpNo = empNo };
            var cns        = new List<CnCommissionRow>();
            var missingCns = new List<MissingCnRow>();

            try
            {
                using var conn = OpenConnection();
                var (fromDate, toDate) = GetPeriod(year, month);

                await LoadEmployeeInfoAsync(conn, summary, empNo, cityCode);

                var cashCns = await LoadCashCnsAsync(conn, empNo, cityCode, fromDate, toDate);
                cns.AddRange(cashCns);
                summary.CashCnCount          = cashCns.Count;
                summary.CashCommissionAmount = cashCns.Sum(c => c.CommissionAmount);

                var codCns = await LoadCodCnsAsync(conn, empNo, summary.RouteCode, cityCode, year, month);
                cns.AddRange(codCns);
                summary.CodCnCount          = codCns.Count;
                summary.CodOnTimeCount      = codCns.Count(c => c.IsOnTime);
                summary.CodDelayedCount     = codCns.Count(c => !c.IsOnTime);
                summary.CodCommissionAmount = codCns.Sum(c => c.CommissionAmount);

                await LoadProcessedCommissionAsync(conn, summary, empNo, year, month, cityCode);

                missingCns = await LoadMissingCnsAsync(conn, empNo, summary.RouteCode, cityCode, fromDate, toDate, year, month);
                summary.MissingCnCount = missingCns.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetEmployeeDetailAsync failed for {EmpNo} {Year}/{Month}.", empNo, year, month);
            }

            return (summary, cns, missingCns);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private async Task LoadEmployeeInfoAsync(
            MySqlConnection conn, EmployeeCommissionSummary summary, string empNo, string cityCode)
        {
            // hr_city join: hc.Code = erc.citycode (3-char = 3-char, direct match)
            var sql = @"
                SELECT
                    erc.Emp_No    AS EmpNo,
                    epd.NAME      AS EmpName,
                    erc.RouteCode AS RouteCode,
                    erc.citycode  AS CityCode,
                    hc.FullName   AS CityName
                FROM hr_employeeroutecode erc
                INNER JOIN hr_employeepersonaldetail epd ON epd.EMP_NO = erc.Emp_No
                INNER JOIN hr_city hc ON hc.Code = erc.citycode
                WHERE erc.Emp_No = @EmpNo
                  AND erc.ToDate IS NULL
                  AND (@City = '' OR erc.citycode = @City)
                LIMIT 1";

            var row = await conn.QueryFirstOrDefaultAsync<EmployeeCommissionSummary>(sql, new
            {
                EmpNo = empNo,
                City  = cityCode ?? ""
            });

            if (row != null)
            {
                summary.EmpName   = row.EmpName;
                summary.RouteCode = row.RouteCode;
                summary.CityCode  = row.CityCode;
                summary.CityName  = row.CityName;
            }
        }

        private async Task<List<CnCommissionRow>> LoadCashCnsAsync(
            MySqlConnection conn, string empNo, string cityCode,
            DateTime fromDate, DateTime toDate)
        {
            // CITY JOIN FIX: join hr_city via station_id, then match erc.citycode = hc.Code
            var sql = @"
                SELECT
                    cc.cn_number        AS CnNumber,
                    'Cash'              AS CommissionType,
                    cc.billing_date     AS CnDate,
                    NULL                AS DeliveryDate,
                    NULL                AS DateDif,
                    1                   AS IsOnTime,
                    IFNULL(cc.TotalCommission, 0) AS CommissionAmount,
                    cc.Shipment_id      AS ShipmentId,
                    NULL                AS ShipmentLabel,
                    cc.Criteria         AS Criteria,
                    NULL                AS Reason
                FROM hr_cash_consignments cc
                INNER JOIN hr_city hc ON hc.station_id = cc.Station_id
                INNER JOIN hr_employeeroutecode erc
                    ON  erc.RouteCode = cc.cour_id
                    AND erc.citycode  = hc.Code
                    AND erc.Emp_No    = @EmpNo
                    AND erc.ToDate IS NULL
                WHERE cc.billing_date BETWEEN @From AND @To
                  AND (@City = '' OR hc.Code = @City)
                ORDER BY cc.billing_date DESC";

            var rows = await conn.QueryAsync<CnCommissionRow>(sql, new
            {
                EmpNo = empNo,
                From  = fromDate,
                To    = toDate,
                City  = cityCode ?? ""
            });

            return rows.ToList();
        }

        private async Task<List<CnCommissionRow>> LoadCodCnsAsync(
            MySqlConnection conn, string empNo, string routeCode,
            string cityCode, int year, int month)
        {
            // COD FIX: Cour_id in hr_cod_consignments is RouteCode (5-char), NOT emp_no (14-char)
            // Join via routeCode if available, fallback to emp lookup
            var filterByRoute = !string.IsNullOrEmpty(routeCode);

            var sql = @"
                SELECT
                    hcc.CN_Number       AS CnNumber,
                    'COD'               AS CommissionType,
                    hcc.Cour_date       AS CnDate,
                    hcc.Delivery_date   AS DeliveryDate,
                    hcc.DateDif         AS DateDif,
                    CASE WHEN IFNULL(hcc.DateDif, 0) <= 0 THEN 1 ELSE 0 END AS IsOnTime,
                    IFNULL(hcc.ComAmnt, 0) AS CommissionAmount,
                    NULL                AS ShipmentId,
                    NULL                AS ShipmentLabel,
                    NULL                AS Criteria,
                    hcc.Reason          AS Reason
                FROM hr_cod_consignments hcc
                INNER JOIN hr_employeeroutecode erc
                    ON  erc.RouteCode = hcc.Cour_id
                    AND erc.Emp_No    = @EmpNo
                    AND erc.ToDate IS NULL
                LEFT JOIN hr_city hc ON hc.station_id = hcc.Arivl_Dest
                WHERE hcc.Cyear  = @Year
                  AND hcc.CMonth = @Month
                  AND (@City = '' OR hc.Code = @City)
                ORDER BY hcc.Cour_date DESC";

            var rows = await conn.QueryAsync<CnCommissionRow>(sql, new
            {
                EmpNo  = empNo,
                Year   = year,
                Month  = month,
                City   = cityCode ?? ""
            });

            return rows.ToList();
        }

        private async Task<List<MissingCnRow>> LoadMissingCnsAsync(
            MySqlConnection conn, string empNo, string routeCode,
            string cityCode, DateTime fromDate, DateTime toDate,
            int year, int month)
        {
            var result = new List<MissingCnRow>();

            // Cash CNs with TotalCommission = 0 or NULL
            try
            {
                var cashSql = @"
                    SELECT
                        cc.cn_number    AS CnNumber,
                        'Cash'          AS CommissionType,
                        cc.billing_date AS CnDate,
                        NULL            AS DeliveryDate,
                        IFNULL(cc.TotalCommission, 0) AS BilledAmount,
                        cc.Criteria     AS Reason,
                        cc.Shipment_id  AS ShipmentId
                    FROM hr_cash_consignments cc
                    INNER JOIN hr_city hc ON hc.station_id = cc.Station_id
                    INNER JOIN hr_employeeroutecode erc
                        ON  erc.RouteCode = cc.cour_id
                        AND erc.citycode  = hc.Code
                        AND erc.Emp_No    = @EmpNo
                        AND erc.ToDate IS NULL
                    WHERE cc.billing_date BETWEEN @From AND @To
                      AND (@City = '' OR hc.Code = @City)
                      AND (cc.TotalCommission IS NULL OR cc.TotalCommission = 0)
                    ORDER BY cc.billing_date DESC
                    LIMIT 500";

                var cashMissing = await conn.QueryAsync<MissingCnRow>(cashSql, new
                {
                    EmpNo = empNo,
                    From  = fromDate,
                    To    = toDate,
                    City  = cityCode ?? ""
                });
                result.AddRange(cashMissing);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LoadMissingCnsAsync cash part failed for {EmpNo}.", empNo);
            }

            // COD CNs with ComAmnt = 0 (may be normal for delayed COD, mark differently)
            try
            {
                var codSql = @"
                    SELECT
                        hcc.CN_Number       AS CnNumber,
                        'COD'               AS CommissionType,
                        hcc.Cour_date       AS CnDate,
                        hcc.Delivery_date   AS DeliveryDate,
                        IFNULL(hcc.ComAmnt, 0) AS BilledAmount,
                        hcc.Reason          AS Reason,
                        NULL                AS ShipmentId
                    FROM hr_cod_consignments hcc
                    INNER JOIN hr_employeeroutecode erc
                        ON  erc.RouteCode = hcc.Cour_id
                        AND erc.Emp_No    = @EmpNo
                        AND erc.ToDate IS NULL
                    LEFT JOIN hr_city hc ON hc.station_id = hcc.Arivl_Dest
                    WHERE hcc.Cyear  = @Year
                      AND hcc.CMonth = @Month
                      AND (@City = '' OR hc.Code = @City)
                      AND (hcc.ComAmnt IS NULL OR hcc.ComAmnt = 0)
                    ORDER BY hcc.Cour_date DESC
                    LIMIT 500";

                var codMissing = await conn.QueryAsync<MissingCnRow>(codSql, new
                {
                    EmpNo  = empNo,
                    Year   = year,
                    Month  = month,
                    City   = cityCode ?? ""
                });
                result.AddRange(codMissing);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LoadMissingCnsAsync COD part failed for {EmpNo}.", empNo);
            }

            return result;
        }

        private async Task LoadProcessedCommissionAsync(
            MySqlConnection conn, EmployeeCommissionSummary summary,
            string empNo, int year, int month, string cityCode)
        {
            // Use complete grand total formula covering ALL 80+ columns
            var sql = $@"
                SELECT
                    1 AS IsProcessed,
                    {GtSql} AS ProcessedTotal
                FROM hr_commissionprocess
                WHERE emp_no = @EmpNo
                  AND Year   = @Year
                  AND Month  = @Month
                  AND (@City = '' OR citycode = @City)
                LIMIT 1";

            try
            {
                var row = await conn.QueryFirstOrDefaultAsync(sql, new
                {
                    EmpNo = empNo,
                    Year  = year,
                    Month = month,
                    City  = cityCode ?? ""
                });

                if (row != null)
                {
                    summary.IsCommissionProcessed = true;
                    summary.ProcessedTotalAmount  = (decimal?)row.ProcessedTotal;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LoadProcessedCommissionAsync failed for {EmpNo}.", empNo);
                try
                {
                    var exists = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM hr_commissionprocess WHERE emp_no=@E AND Year=@Y AND Month=@M",
                        new { E = empNo, Y = year, M = month });
                    summary.IsCommissionProcessed = exists > 0;
                }
                catch { /* swallow */ }
            }
        }
    }
}
