using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        // adjusment_policy on S10 defines type 1/2 as manual billing adjustments.
        // The commission process auto-writes type 0 rows for minimum-guarantee and RBI-cap adjustments.
        private const int MinimumGuaranteeAdjustmentTypeId = 0;


        public async Task<CommissionProcessPreviewResult> PreviewCommissionProcessAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();
            await connection.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;");

            var previewCfg = await CommissionConfig.LoadAsync(connection);
            var baseline = await CaptureCommissionPreviewBaselineAsync(connection, year, month, cityCode, previewCfg);

            using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                var result = await ProcessCommissionInternalAsync(connection, transaction, year, month, cityCode, currentUserId);
                await transaction.RollbackAsync();
                result.RollbackIntegrityPreserved = await VerifyCommissionPreviewBaselineAsync(connection, year, month, cityCode, baseline, previewCfg);
                return result;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                }

                throw;
            }
        }

        private async Task<CommissionProcessPreviewResult> ProcessCommissionInternalAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            bool billingStatusConfirmed = true,
            bool attendanceStatusConfirmed = true,
            bool allCommissionTypesConfirmed = true)
        {
            await EnsureProcessesOpenAsync(connection, year, month, cityCode);

            var commCfg = await CommissionConfig.LoadAsync(connection, transaction);

            var info = await connection.QueryFirstOrDefaultAsync(
                $@"SELECT t.CreatedDate AS ProcessedDate, t.CreatedBy, u.UserName
                  FROM {CommissionProcessTable} t
                  LEFT JOIN lcs_users u ON u.userID = t.CreatedBy
                  WHERE t.Year = @Year AND t.Month = @Month AND t.citycode = @CityCode
                  ORDER BY t.CreatedDate DESC LIMIT 1",
                new { Year = year, Month = month, CityCode = cityCode },
                transaction,
                commandTimeout: 30);
            if (info?.ProcessedDate != null)
            {
                throw new ArgumentException($"Already Processed on {((DateTime)info.ProcessedDate):dd-MMM-yyyy}.{BuildProcessedByInfo(info.CreatedBy?.ToString(), info.UserName?.ToString())}");
            }

            var context = await LoadCommissionProcessContextAsync(connection, transaction, year, month, cityCode, currentUserId, commCfg);
            context.Cfg = commCfg;
            await DeleteCommissionRecordsAsync(connection, transaction, year, month, cityCode);

            var result = new CommissionProcessPreviewResult
            {
                Year = year,
                Month = month,
                CityCode = cityCode,
                EmployeeRouteCount = context.EmployeeRoutes.Rows.Count
            };

            var cashRows = BuildCommissionProcessRows(connection, year, month, cityCode, currentUserId, context, result);
            await ProcessOleRecordsAsync(connection, transaction, year, month, cityCode, currentUserId, context, cashRows, result);
            result.AcknowledgmentRowsInserted = await InsertCommissionAcknowledgmentAsync(
                connection,
                transaction,
                currentUserId,
                billingStatusConfirmed,
                attendanceStatusConfirmed,
                allCommissionTypesConfirmed);

            return result;
        }

        private static async Task<int> InsertCommissionAcknowledgmentAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string currentUserId,
            bool billingStatusConfirmed,
            bool attendanceStatusConfirmed,
            bool allCommissionTypesConfirmed)
        {
            return await connection.ExecuteAsync(
                $@"INSERT INTO {AcTestTableNames.T_Acknowledgment}
                  VALUES (3, @UserId, NOW(), @Billing, @Attendance, @AllCommissionTypes, NULL)",
                new
                {
                    UserId = currentUserId,
                    Billing = billingStatusConfirmed,
                    Attendance = attendanceStatusConfirmed,
                    AllCommissionTypes = allCommissionTypesConfirmed
                },
                transaction);
        }

        private async Task<CommissionProcessContext> LoadCommissionProcessContextAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            CommissionConfig cfg)
        {
            bool cityAllowed = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM lcs_user_location
                  WHERE userid = @UserId
                    AND city_code = @CityCode",
                new
                {
                    UserId = currentUserId,
                    CityCode = cityCode
                },
                transaction) > 0;

            if (!cityAllowed)
            {
                throw new ArgumentException("You are not allowed to process commission for the selected city.");
            }

            var salaryDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));

            var dtRates = DAL.ExecuteDataTable(connection, CommandType.Text, "SELECT * FROM hr_comm_insentives hci");
            var dtRatesNew = DAL.ExecuteDataTable(connection, CommandType.Text, "SELECT p.RateID,p.Type,p.Rate,p.RateType,p.IsPercent FROM hr_commissionpolicy p WHERE p.IsDeleted = 0;");

            if (dtRates.Rows.Count == 0 || dtRatesNew.Rows.Count == 0)
            {
                throw new ArgumentException("Commission Rates not defined.");
            }

            var routeExcludeCodeTypesCsv = string.IsNullOrWhiteSpace(cfg.RouteExcludeCodeTypesCsv)
                ? "0"
                : cfg.RouteExcludeCodeTypesCsv;

            var employeeRoutes = DAL.ExecuteDataTable(
                connection,
                CommandType.Text,
                $@"SELECT
                      erc.RouteCode,
                      erc.citycode,
                      erc.emp_no,
                      hrh.porter_comm AS Porter,
                      IFNULL(erc.CodeType,1) AS CodeType,
                      RBIExclude
                  FROM hr_employeeroutecode erc
                  LEFT JOIN hr_routecodes_hdr hrh
                    ON erc.RouteCode = hrh.RouteCode
                   AND erc.citycode = hrh.CityCode
                  WHERE IFNULL(erc.CodeType,0) NOT IN ({routeExcludeCodeTypesCsv})
                    AND erc.citycode = @CityCode
                    AND @SalaryDate BETWEEN erc.fromdate AND IFNULL(erc.todate,'2099-08-28')",
                new MySqlParameter("@SalaryDate", salaryDate),
                new MySqlParameter("@CityCode", cityCode));

            if (employeeRoutes.Rows.Count == 0)
            {
                throw new ArgumentException("Couriers not found in selected city.");
            }

            var stationId = await connection.ExecuteScalarAsync<string>(
                @"SELECT hc.station_id
                  FROM hr_city hc
                  WHERE hc.Code = @CityCode",
                new { CityCode = cityCode },
                transaction);

            if (string.IsNullOrWhiteSpace(stationId))
            {
                throw new ArgumentException("Station ID is not define for the selected city");
            }

            var locationIds = (await connection.QueryAsync<string>(
                @"SELECT DISTINCT lm.GlLocationId
                  FROM lcs_hr.hr_locationmapping lm
                  WHERE lm.GlLocationId IN (
                      SELECT l.LocationID
                      FROM lcs_setup.locations l
                      WHERE l.BILLINGCITYID = (
                          SELECT c.station_id
                          FROM lcs_hr.hr_city c
                          WHERE c.Code = @CityCode
                      )
                  )",
                new { CityCode = cityCode },
                transaction)).ToList();

            if (locationIds.Count == 0)
            {
                throw new ArgumentException("Location ID is not define for the selected city");
            }

            DataTable zoneInfo;
            using (var billingConn = await OpenExternalCodConnectionAsync("LHR_Billing"))
            {
                zoneInfo = DAL.ExecuteDataTable(
                    billingConn,
                    CommandType.Text,
                    @"SELECT c.OVERLAND_ZONE, zc.color_zone
                      FROM lcs.city c
                      LEFT JOIN lcs_billing.zone_color zc ON c.ZONE_ID = zc.city_id
                      WHERE c.CITY_ID = @StationId",
                    new MySqlParameter("@StationId", stationId));
            }

            if (zoneInfo.Rows.Count == 0)
            {
                throw new ArgumentException("Zone not found.");
            }

            return new CommissionProcessContext
            {
                Year = year,
                Month = month,
                CityCode = cityCode,
                SalaryDate = salaryDate,
                StationId = stationId,
                LocationIdsCsv = string.Join(",", locationIds),
                OverlandZone = Convert.ToString(zoneInfo.Rows[0][0], CultureInfo.InvariantCulture) ?? string.Empty,
                OvernightZone = Convert.ToString(zoneInfo.Rows[0][1], CultureInfo.InvariantCulture) ?? string.Empty,
                EmployeeRoutes = employeeRoutes
            };
        }

        private static async Task DeleteCommissionRecordsAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            string cityCode)
        {
            await connection.ExecuteAsync(
                $@"DELETE FROM {AcTestTableNames.T_CommissionProcess}
                  WHERE month = @Month
                    AND year = @Year
                    AND citycode = @CityCode",
                new
                {
                    Month = month,
                    Year = year,
                    CityCode = cityCode
                },
                transaction);
        }

        private DataTable BuildCommissionProcessRows(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            CommissionProcessContext context,
            CommissionProcessPreviewResult result)
        {
            var data = LCS.GetDataTableSchema(connection, CommissionProcessTableName).Tables[0];
            var sourceData = BuildCommissionSourceDataSet(connection, context);

            // ── PERF: Build O(1) hash-index per source DataTable by cour_id ──────
            // Eliminates O(routes × tableRows) linear scans → O(1) dictionary lookup.
            // Original: ~500 routes × 50 tables × O(n) scan each = millions of comparisons.
            // Now:      ~500 routes × 50 tables × O(1) lookup = ~25,000 hash hits.
            var sourceIndex = new Dictionary<string, ILookup<string, DataRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (DataTable dt in sourceData.Tables)
            {
                // Some source tables (e.g. Porter = "SELECT 1;") have no cour_id column.
                // Only index tables that actually contain the lookup key.
                if (!dt.Columns.Contains("cour_id"))
                    continue;

                sourceIndex[dt.TableName] = dt.AsEnumerable().ToLookup(
                    row => (Convert.ToString(row["cour_id"], CultureInfo.InvariantCulture) ?? string.Empty).Trim(),
                    StringComparer.OrdinalIgnoreCase);
            }

            result.CashRowsGenerated = context.EmployeeRoutes.Rows.Count;

            foreach (DataRow routeRow in context.EmployeeRoutes.Rows)
            {
                var routeCode = Convert.ToString(routeRow["RouteCode"], CultureInfo.InvariantCulture) ?? string.Empty;
                var trimmedRoute = routeCode.Trim();
                var newRow = data.NewRow();

                newRow["Year"] = year;
                newRow["Month"] = month;
                newRow["citycode"] = cityCode;
                newRow["Cour_id"] = routeCode;
                newRow["emp_no"] = Convert.ToString(routeRow["Emp_No"], CultureInfo.InvariantCulture) ?? string.Empty;
                newRow["DOM_CREDIT"] = FirstCommissionValueIndexed(sourceIndex, "Dom_Cr", trimmedRoute, 2);
                newRow["LOCAL_CREDIT"] = FirstCommissionValueIndexed(sourceIndex, "Lcl_Cr", trimmedRoute, 2);
                newRow["LOCAL_DLD"] = FirstCommissionValueIndexed(sourceIndex, "LOC_Delivery", trimmedRoute, 2);
                newRow["PMCL"] = 0m;
                newRow["DomesticDelivery"] = FirstCommissionValueIndexed(sourceIndex, "DOM_Delivery", trimmedRoute, 2);
                newRow["INTL_CREDIT"] = FirstCommissionValueIndexed(sourceIndex, "Intl_Cr", trimmedRoute, 2);
                newRow["Porter"] = 0m;
                newRow["COD"] = FirstCommissionValueIndexed(sourceIndex, "COD", trimmedRoute, 2);
                newRow["OVERNIGHT"] = 0m;
                newRow["YB1KG"] = SumCommissionValueIndexed(sourceIndex, "YELLOWBOX1KG", trimmedRoute, "Base_Com");
                newRow["YB2KG"] = SumCommissionValueIndexed(sourceIndex, "YELLOWBOX2KG", trimmedRoute, "Base_Com");
                newRow["YB5KG"] = SumCommissionValueIndexed(sourceIndex, "YELLOWBOX5KG", trimmedRoute, "Base_Com");
                newRow["YB10KG"] = SumCommissionValueIndexed(sourceIndex, "YELLOWBOX10KG", trimmedRoute, "Base_Com");
                newRow["YB15KG"] = SumCommissionValueIndexed(sourceIndex, "YELLOWBOX15KG", trimmedRoute, "Base_Com");
                newRow["YB25KG"] = SumCommissionValueIndexed(sourceIndex, "YELLOWBOX25KG", trimmedRoute, "Base_Com");
                newRow["FLAYER"] = SumCommissionValueIndexed(sourceIndex, "FLAYER", trimmedRoute, "Base_Com");
                newRow["DETAIN"] = SumCommissionValueIndexed(sourceIndex, "ECONOMY", trimmedRoute, "Base_Com");
                newRow["OVERLAND"] = SumCommissionValueIndexed(sourceIndex, "OVERLAND", trimmedRoute, "Base_Com");
                newRow["PREPAID"] = 0m;
                newRow["LOVELINE"] = SumCommissionValueIndexed(sourceIndex, "LOVELINE", trimmedRoute, "Base_Com");
                newRow["INTL_CASH"] = 0m;
                newRow["MOFA_OTO"] = SumCommissionValueIndexed(sourceIndex, "MOFA_OFFICE_TO_OFFICE", trimmedRoute, "Base_Com");
                newRow["MOFA_OTD"] = SumCommissionValueIndexed(sourceIndex, "MOFA_OFFICE_TO_DOORSTEP", trimmedRoute, "Base_Com");
                newRow["Rms_Cod_Booking"] = 0m;
                newRow["CASH_EXP_BKG_UpTo_2Kg"] = 0m;
                newRow["CASH_EXP_BKG_Above_2Kg"] = 0m;
                newRow["CASH_Leop_BOX_Above_2Kg"] = 0m;
                newRow["CASH_Economy_Booking"] = 0m;
                newRow["CASH_OLE_Booking"] = 0m;
                newRow["Insurance_Com"] = SumCommissionValueIndexed(sourceIndex, "Insurance_Com", trimmedRoute, "Ins_Com");
                newRow["AllInOne"] = SumCommissionValueIndexed(sourceIndex, "AllInOne", trimmedRoute, "Base_Com");
                newRow["DocumnetCare"] = SumCommissionValueIndexed(sourceIndex, "DocumnetCare", trimmedRoute, "Base_Com");
                newRow["MTD"] = SumCommissionValueIndexed(sourceIndex, "MTD", trimmedRoute, "MTD_Com");
                newRow["VAS"] = SumCommissionValueIndexed(sourceIndex, "VAS", trimmedRoute, "Vas_Com");
                newRow["IntlDox"] = SumCommissionValueIndexed(sourceIndex, "IntlDox", trimmedRoute, "Base_Com");
                newRow["IntlEconomy"] = SumCommissionValueIndexed(sourceIndex, "IntlEconomy", trimmedRoute, "Base_Com");
                newRow["IntlParcel"] = SumCommissionValueIndexed(sourceIndex, "IntlParcel", trimmedRoute, "Base_Com");
                newRow["ONUpto1kg"] = SumCommissionValueIndexed(sourceIndex, "ONUpto1kg", trimmedRoute, "Base_Com");
                newRow["ONAbove1kg"] = SumCommissionValueIndexed(sourceIndex, "ONAbove1kg", trimmedRoute, "Base_Com");
                newRow["ONUpto1kgRetailCOD"] = SumCommissionValueIndexed(sourceIndex, "ONUpto1kgRetailCOD", trimmedRoute, "Base_Com");
                newRow["ONAbove1kgRetailCOD"] = SumCommissionValueIndexed(sourceIndex, "ONAbove1kgRetailCOD", trimmedRoute, "Base_Com");
                newRow["EconomyRetail"] = SumCommissionValueIndexed(sourceIndex, "EconomyRetail", trimmedRoute, "Base_Com");
                newRow["YB1KGRetail"] = SumCommissionValueIndexed(sourceIndex, "YB1KGRetail", trimmedRoute, "Base_Com");
                newRow["YB2KGRetail"] = SumCommissionValueIndexed(sourceIndex, "YB2KGRetail", trimmedRoute, "Base_Com");
                newRow["YB5KGRetail"] = SumCommissionValueIndexed(sourceIndex, "YB5KGRetail", trimmedRoute, "Base_Com");
                newRow["YB10KGRetail"] = SumCommissionValueIndexed(sourceIndex, "YB10KGRetail", trimmedRoute, "Base_Com");
                newRow["YB15KGRetail"] = SumCommissionValueIndexed(sourceIndex, "YB15KGRetail", trimmedRoute, "Base_Com");
                newRow["YB25KGRetail"] = SumCommissionValueIndexed(sourceIndex, "YB25KGRetail", trimmedRoute, "Base_Com");
                newRow["MyCollect"] = SumCommissionValueIndexed(sourceIndex, "MyCollect", trimmedRoute, "Base_Com");
                newRow["Attestation"] = SumCommissionValueIndexed(sourceIndex, "Attestation", trimmedRoute, "Base_Com");
                newRow["Retail_Deduction"] = 0m;
                newRow["Credit_Debit_Card"] = SumCommissionValueIndexed(sourceIndex, "Credit_Debit_Card", trimmedRoute, "Base_Com");
                newRow["ECommerce_Zero_COD"] = SumCommissionValueIndexed(sourceIndex, "ECommerce_Zero_COD", trimmedRoute, "Base_Com");
                newRow["Passport"] = SumCommissionValueIndexed(sourceIndex, "Passport", trimmedRoute, "Base_Com");
                newRow["CNIC_Card"] = SumCommissionValueIndexed(sourceIndex, "CNIC_Card", trimmedRoute, "Base_Com");
                newRow["Return_E_Com"] = SumCommissionValueIndexed(sourceIndex, "Return_E_Com", trimmedRoute, "OleCommission");
                newRow["Pickup_Leopard"] = SumCommissionValueIndexed(sourceIndex, "Pickup_Leopard", trimmedRoute, "Base_Com");
                newRow["COD_Bonus"] = SumCommissionValueIndexed(sourceIndex, "COD_Bonus", trimmedRoute, "Base_Com");
                newRow["COD_Deduction"] = SumCommissionValueIndexed(sourceIndex, "COD_Deduction", trimmedRoute, "Base_Com");
                newRow["CreatedBy"] = currentUserId;
                newRow["CreatedDate"] = DateTime.Now;
                data.Rows.Add(newRow);
            }

            return data;
        }

        private DataSet BuildCommissionSourceDataSet(MySqlConnection connection, CommissionProcessContext context)
        {
            var (fromDate, toDate) = GetCommissionPeriod(context.Year, context.Month, context.Cfg);
            var query = new StringBuilder();

            AppendCommissionSourceQueriesCore(query, context);
            AppendCommissionSourceQueriesExtension(query, context);

            var ds = DAL.ExecuteDataset(
       connection,
       CommandType.Text,
       query.ToString(),
       600,
       new MySqlParameter("@todate", fromDate),
       new MySqlParameter("@fromdate", toDate),
       new MySqlParameter("@stationid", context.StationId),
       new MySqlParameter("@Overnight_Zone", context.OvernightZone),
       new MySqlParameter("@Overland_Zone", context.OverlandZone),
       new MySqlParameter("@month", context.Month),
       new MySqlParameter("@year", context.Year),
       new MySqlParameter("@city", context.CityCode));

            var tableNames = new[]
            {
                "Dom_Cr", "Lcl_Cr", "Intl_Cr", "Porter", "COD", "OVERNIGHT", "YELLOWBOX1KG", "YELLOWBOX2KG",
                "YELLOWBOX5KG", "YELLOWBOX10KG", "YELLOWBOX25KG", "FLAYER", "ECONOMY", "OVERLAND", "PREPAID",
                "LOVELINE", "INTERNATIONAL", "LOC_Delivery", "DOM_Delivery", "MOFA_OFFICE_TO_OFFICE",
                "MOFA_OFFICE_TO_DOORSTEP", "YELLOWBOX15KG", "rmsCodBooking", "RBI_CashExpressBooking",
                "RBI_Cash_LEOPARDS_BOX_Booking_Above2KG", "RBI_ECONOMY_BOOKING", "RBI_OLE_BOOKING", "AllInOne",
                "DocumnetCare", "MTD", "VAS", "IntlDox", "IntlEconomy", "IntlParcel", "ONUpto1kg", "ONAbove1kg",
                "ONUpto1kgRetailCOD", "ONAbove1kgRetailCOD", "EconomyRetail", "YB1KGRetail", "YB2KGRetail",
                "YB5KGRetail", "YB10KGRetail", "YB15KGRetail", "YB25KGRetail", "MyCollect", "Attestation",
                "Insurance_Com", "Credit_Debit_Card", "ECommerce_Zero_COD", "Passport", "CNIC_Card", "Return_E_Com",
                "Pickup_Leopard", "COD_Bonus", "COD_Deduction"
            };

            for (int index = 0; index < tableNames.Length && index < ds.Tables.Count; index++)
            {
                ds.Tables[index].TableName = tableNames[index];
            }

            return ds;
        }

        private async Task ProcessOleRecordsAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            CommissionProcessContext context,
            DataTable cashRows,
            CommissionProcessPreviewResult result)
        {
            // ── PERF: Combine 3 aggregate OLE queries (RateId 2,3,4) into 1 round-trip ──
            var oleAggregateAll = (await connection.QueryAsync<OLECommission>(
                $@"SELECT `GlLocationId`, `RateId` AS RateID, SUM(`OleCommission`) AS OleCommission
                   FROM {OleCommissionProcessTable}
                   WHERE YEAR = @Year AND MONTH = @Month AND RateId IN (2, 3, 4)
                     AND `GlLocationId` IN ({context.LocationIdsCsv})
                   GROUP BY `GlLocationId`, `RateId`",
                new { Year = year, Month = month },
                transaction,
                commandTimeout: 120)).ToList();

            var objOleDispatchProper = oleAggregateAll.Where(r => r.RateID == 2).ToList();
            var objOleTransitDispatch = oleAggregateAll.Where(r => r.RateID == 3).ToList();
            var objOleDeliveryOps = oleAggregateAll.Where(r => r.RateID == 4).ToList();

            // ── PERF: Combine 3 joined OLE queries (Credit RateId=1, Delivery RateId=5, Corporate/RBI)
            //    into 1 round-trip. DISTINCT is applied globally (harmless for RateId 1/5,
            //    required for corporate RBI rows).
            var oleJoinedAll = (await connection.QueryAsync<OLECommission>(
                $@"SELECT DISTINCT RateID, `CourierID`, rt.`Emp_No`, `GlLocationId`, OleCommission
                   FROM {OleCommissionProcessTable} cp
                   INNER JOIN `hr_employeeroutecode` rt
                     ON rt.`RouteCode` = cp.`CourierID`
                    AND ToDate IS NULL
                    AND cp.`GlLocationId` = rt.`LocationId`
                   WHERE YEAR = @Year AND MONTH = @Month
                     AND `GlLocationId` IN ({context.LocationIdsCsv})
                     AND (
                           (RateId = 1 AND IFNULL(rt.CodeType, 0) IN ({context.Cfg.OleCreditBookingIncludeCodeTypesCsv}))
                           OR
                           (RateId IN (5, {context.Cfg.OverlandProcessRateIdsCsv}) AND IFNULL(rt.CodeType, 0) NOT IN ({context.Cfg.OleDeliveryExcludeCodeTypesCsv}))
                         )",
                new { Year = year, Month = month },
                transaction,
                commandTimeout: 120)).ToList();

            var oleCreditBooking = oleJoinedAll.Where(r => r.RateID == 1).ToList();
            var oleDelivery = oleJoinedAll.Where(r => r.RateID == 5).ToList();
            var overlandRateIds = new HashSet<int>(
                context.Cfg.OverlandProcessRateIdsCsv.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(int.Parse));
            var corporateRbi = oleJoinedAll
                .Where(r => overlandRateIds.Contains(r.RateID))
                .ToList();

            int days = DateTime.DaysInMonth(year, month);
            var allEmployees = await LoadCommissionEmployeePopulationAsync(connection, transaction, year, month, days, context.SalaryDate, context.LocationIdsCsv, context.Cfg);

            // ── SOURCE-DRIVEN POPULATION SUPPLEMENT ─────────────────────────────
            // The master-based population misses employees who have actual commission
            // source data but are not in hr_empcommissioneligibility / hr_employeepersonaldetail
            // for this city's LocationIds (e.g. roaming staff, non-rider staff with
            // COD/OLE activity, employees whose P_CITY_CODE differs from work city).
            // Supplement the population from actual commission source tables.
            var sourceEmpNos = await LoadSourceDrivenEmployeeIdsAsync(
                connection, transaction, year, month, cityCode, context);
            var existingEmpNos = new HashSet<string>(
                allEmployees
                    .Select(e => (e.Emp_no ?? string.Empty).Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var srcEmp in sourceEmpNos)
            {
                if (!existingEmpNos.Contains(srcEmp.Trim()))
                {
                    allEmployees.Add(new OLEEmpList
                    {
                        Emp_no = srcEmp,
                        Cour_id = null,
                        WD = 0,
                        IsEligible = false,
                        CodeType = 0,
                        CommissionId = 0,
                        LocationId = 0
                    });
                    existingEmpNos.Add(srcEmp.Trim());
                }
            }

            foreach (var item in oleCreditBooking)
            {
                allEmployees.Add(new OLEEmpList
                {
                    Emp_no = item.Emp_No,
                    Cour_id = item.CourierID,
                    OLE_Credit_Booking = item.OleCommission
                });
            }

            foreach (var item in oleDelivery)
            {
                allEmployees.Add(new OLEEmpList
                {
                    Emp_no = item.Emp_No,
                    Cour_id = item.CourierID,
                    OLE_Delivery = item.OleCommission
                });
            }

            DistributeOleLocationCommission(allEmployees, objOleDispatchProper, 2, (employee, amount) => employee.OLEDispatchProperAmount = amount);
            DistributeOleLocationCommission(allEmployees, objOleTransitDispatch, 3, (employee, amount) => employee.OLETransitDispatchAmount = amount);
            DistributeOleLocationCommission(allEmployees, objOleDeliveryOps, 4, (employee, amount) => employee.OLEDeliveryOPSAmount = amount);

            AppendCorporateRbiRows(allEmployees, corporateRbi, context.Cfg);

            allEmployees = AggregateEmployeeCommission(allEmployees);
            MergeCashRowsIntoEmployees(allEmployees, cashRows);

            // Route codes identify city ownership regardless of which ARVL_DEST station the
            // incentive record was filed under.  Emp_nos cover location-based staff already
            // in the merged population who have no route code.
            var routeCodesSql = string.Join(",", context.EmployeeRoutes.Rows
                .Cast<DataRow>()
                .Select(r => r["RouteCode"]?.ToString()?.Trim())
                .Where(rc => !string.IsNullOrWhiteSpace(rc))
                .Distinct()
                .Select(rc => $"'{rc}'"));

            var empNosSql = string.Join(",", allEmployees
                .Select(e => e.Emp_no?.Trim())
                .Where(en => !string.IsNullOrWhiteSpace(en))
                .Distinct()
                .Select(en => $"'{en}'"));

            var oleVasRows = await LoadOleVasFinalRowsAsync(connection, transaction, context, routeCodesSql, empNosSql);
            ApplyOleVasFinalRows(allEmployees, oleVasRows);
            var srBonusRows = await LoadSrBonusRowsAsync(connection, transaction, year, month, cityCode);
            ApplySrBonusRows(allEmployees, srBonusRows);

            // ── FINAL DEDUP SAFETY GATE ──────────────────────────────────────────
            // Post-aggregation steps (MergeCashRowsIntoEmployees, ApplyOleVasFinalRows,
            // ApplySrBonusRows) can each .Add() new OLEEmpList objects.  Collapse
            // everything to exactly one row per Emp_no so no duplicate/split rows
            // survive to DB insert.  This is the payroll-identity boundary.
            allEmployees = AggregateFinalCommissionByEmployee(allEmployees);

            var insertableEmployees = allEmployees
                .Where(HasAnyFinancialValue)
                .ToList();

            await InsertCommissionRowsAsync(connection, transaction, insertableEmployees, year, month, cityCode, currentUserId);

            var adjustments = BuildCommissionAdjustments(allEmployees, year, month, cityCode, currentUserId, context.Cfg);
            await ReplaceCommissionAdjustmentsAsync(connection, transaction, adjustments, year, month, cityCode, context.Cfg);

            result.CommissionRowsInserted = insertableEmployees.Count;
            result.AdjustmentRowsInserted = adjustments.Count;
            result.TotalCommissionAmount = insertableEmployees.Sum(CalculateCommissionTotal);
            result.Employees = BuildCommissionPreviewEmployees(allEmployees, adjustments);
        }

        private static async Task<List<OLEEmpList>> LoadCommissionEmployeePopulationAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            int days,
            DateTime salaryDate,
            string locationIdsCsv,
            CommissionConfig cfg)
        {
            var (commFromDate, commToDate) = GetCommissionPeriod(year, month, cfg);

            var rows = await connection.QueryAsync<OLEEmpList>(
                $@"SELECT
                        NULL AS Cour_id,
                        NULL AS CodeType,
                        el.Emp_no,
                        l.`LocationId`,
                        ((@Days - att.`Sundays` + att.`adjustmentAbsent`) - att.`Absents`) AS WD,
                        el.`IsEligible`,
                        CommissionId
                    FROM `hr_empcommissioneligibility` el
                    INNER JOIN lcs_hr.`hr_employeelocationdetails` l
                        ON el.`Emp_no` = l.`Emp_No`
                       AND l.`ToDate` IS NULL
                    INNER JOIN `hr_employeeattendanceprocess` att
                        ON att.`emp_no` = el.`Emp_no`
                       AND YEAR = @Year
                       AND MONTH = @Month
                    WHERE `CommissionId` IN ({cfg.EligibilityCommissionIdsCsv})
                      AND `IsEligible` = TRUE
                      AND l.`LocationId` IN ({locationIdsCsv})

                    UNION ALL

                    SELECT
                        rt.`RouteCode` AS Cour_id,
                        IFNULL(rt.CodeType, 1) AS CodeType,
                        e.EMP_NO AS Emp_no,
                        l.LocationId,
                        ((@Days - att.`Sundays` + att.`adjustmentAbsent`) - att.`Absents`) AS WD,
                        IF(d.`Courier_Dept` = 'Y', cast_to_bit(1), cast_to_bit(0)) AS `IsEligible`,
                        0 AS `CommissionId`
                    FROM lcs_hr.hr_employeepersonaldetail e
                    INNER JOIN lcs_hr.`hr_employeelocationdetails` l
                        ON e.EMP_NO = l.`Emp_No`
                       AND l.`ToDate` IS NULL
                    INNER JOIN `hr_employeedepartmentdetails` dp
                        ON dp.`Emp_No` = e.EMP_NO
                       AND dp.`ToDate` IS NULL
                    INNER JOIN `hr_subdepartment` d
                        ON d.`SDID` = dp.`DeptCode`
                    LEFT OUTER JOIN `hr_employeeroutecode` rt
                        ON rt.`Emp_No` = e.`Emp_no`
                       AND rt.`ToDate` IS NULL
                       AND l.`LocationId` = rt.`LocationId`
                    LEFT JOIN `hr_employeeattendanceprocess` att
                        ON att.`emp_no` = e.EMP_NO
                       AND YEAR = @Year
                       AND MONTH = @Month
                    WHERE IFNULL(rt.CodeType, 0) NOT IN ({cfg.RouteExcludeCodeTypesCsv})
                      AND (e.left_date IS NULL OR e.left_date BETWEEN @CommFromDate AND @CommToDate)
                      AND l.`LocationId` IN ({locationIdsCsv})",
                new
                {
                    Year = year,
                    Month = month,
                    Days = days,
                    SalaryDate = salaryDate,
                    CommFromDate = commFromDate,
                    CommToDate = commToDate
                },
                transaction,
                commandTimeout: 300);

            return rows.ToList();
        }

        /// <summary>
        /// Source-driven employee discovery: queries actual commission source tables
        /// to find employee IDs with payable activity for the target period/city,
        /// regardless of whether they appear in the master population.
        ///
        /// Sources queried:
        ///   1. hr_codcommission (COD) — bridge: Cour_id → hr_employeeroutecode.RouteCode → Emp_No
        ///   2. hr_ole_vas_incentive_detail (OLE/VAS) — direct Emp_No + COURIER_ID bridge
        ///   3. hr_olecommissionprocess (OLE process) — CourierID bridge
        ///   4. hr_incentive_overall_sr (SR Bonus) — direct Emp_No
        ///
        /// Employees discovered here but missing from the master population are
        /// added with WD=0, IsEligible=false so they receive their earned
        /// commission but are not eligible for minimum-guarantee adjustments.
        /// </summary>
        /// <remarks>
        /// PERF: All 4 source queries are combined into a single UNION ALL statement
        /// to eliminate 3 extra DB round-trips per city (4 queries → 1).
        /// </remarks>
        private static async Task<HashSet<string>> LoadSourceDrivenEmployeeIdsAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            string cityCode,
            CommissionProcessContext context)
        {
            var (fromDate, toDate) = GetCommissionPeriod(context.Year, context.Month, context.Cfg);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── UNION ALL: COD bridge + OLE/VAS + OLE process + SR Bonus ──
            var allSourceEmpNos = await connection.QueryAsync<string>(
                $@"SELECT DISTINCT emp_no FROM (
                       /* 1. COD commission bridge */
                       SELECT DISTINCT LPAD(CAST(rt.Emp_No AS CHAR), 14, '0') AS emp_no
                       FROM {CodCommissionTable} c
                       INNER JOIN hr_employeeroutecode rt
                         ON rt.RouteCode = c.Cour_id AND rt.ToDate IS NULL
                       WHERE c.Year = @Year AND c.Month = @Month AND c.City = @CityCode

                       UNION ALL

                       /* 2. OLE/VAS incentive detail */
                       SELECT DISTINCT LPAD(CAST(a.Emp_No AS CHAR), 14, '0') AS emp_no
                       FROM lcs_hr.hr_ole_vas_incentive_detail a
                       WHERE a.DELIVERY_DATE BETWEEN @FromDate AND @ToDate
                         AND a.RateID IN (5, 96, 97, 98, 99, 105, 106, 107, 108, 109, 110)
                         AND (
                               a.COURIER_ID IN (
                                   SELECT rt.RouteCode FROM hr_employeeroutecode rt
                                   WHERE rt.citycode = @CityCode AND rt.ToDate IS NULL
                               )
                               OR LPAD(CAST(a.Emp_No AS CHAR), 14, '0') IN (
                                   SELECT LPAD(CAST(rt.Emp_No AS CHAR), 14, '0') FROM hr_employeeroutecode rt
                                   WHERE rt.citycode = @CityCode AND rt.ToDate IS NULL
                               )
                             )

                       UNION ALL

                       /* 3. OLE commission process — CourierID bridge */
                       SELECT DISTINCT LPAD(CAST(rt.Emp_No AS CHAR), 14, '0') AS emp_no
                       FROM {OleCommissionProcessTable} cp
                       INNER JOIN hr_employeeroutecode rt
                         ON rt.RouteCode = cp.CourierID
                        AND rt.ToDate IS NULL
                        AND cp.GlLocationId = rt.LocationId
                       WHERE cp.Year = @Year AND cp.Month = @Month
                         AND cp.GlLocationId IN ({context.LocationIdsCsv})

                       UNION ALL

                       /* 4. SR Bonus — direct Emp_No */
                       SELECT DISTINCT LPAD(CAST(Emp_No AS CHAR), 14, '0') AS emp_no
                       FROM lcs_hr.hr_incentive_overall_sr
                       WHERE Year = @Year AND Month = @Month AND citycode = @CityCode
                         AND is_eligible = 'Eligible'
                   ) all_sources",
                new
                {
                    Year = year,
                    Month = month,
                    CityCode = cityCode,
                    FromDate = fromDate,
                    ToDate = toDate
                },
                transaction,
                commandTimeout: 300);

            foreach (var empNo in allSourceEmpNos)
            {
                if (!string.IsNullOrWhiteSpace(empNo))
                    result.Add(empNo.Trim());
            }

            return result;
        }

        private static async Task<List<OleVasFinalRow>> LoadOleVasFinalRowsAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            CommissionProcessContext context,
            string routeCodesSql,
            string empNosSql)
        {
            var (fromDate, toDate) = GetCommissionPeriod(context.Year, context.Month, context.Cfg);

            // Build the employee-identity filter.
            // Legacy behaviour filtered by ARVL_DEST (arrival station) which caused data stored
            // under other cities' station codes to be missed.  Route codes are the authoritative
            // city-identity key for route employees; emp_nos cover location-based staff already
            // in the population.
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(routeCodesSql))
                filters.Add($"a.COURIER_ID IN ({routeCodesSql})");
            if (!string.IsNullOrWhiteSpace(empNosSql))
                filters.Add($"LPAD(CAST(a.Emp_No AS CHAR), 14, '0') IN ({empNosSql})");

            if (filters.Count == 0)
                return new List<OleVasFinalRow>();

            var identityFilter = string.Join(" OR ", filters);

            var rows = await connection.QueryAsync<OleVasFinalRow>(
                $@"SELECT
                      LPAD(CAST(a.Emp_No AS CHAR), 14, '0') AS Emp_No,
                      a.COURIER_ID AS SourceCour_id,
                      CASE WHEN a.RateID = 5 THEN NULL ELSE a.COURIER_ID END AS Cour_id,
                      a.RateID,
                      SUM(IFNULL(a.Incentive, 0)) AS Amount
                  FROM lcs_hr.hr_ole_vas_incentive_detail a
                  WHERE a.DELIVERY_DATE BETWEEN @FromDate AND @ToDate
                    AND a.RateID IN (5, 96, 97, 98, 99, 105, 106, 107, 108, 109, 110)
                    AND ({identityFilter})
                  GROUP BY a.Emp_No, a.COURIER_ID, a.RateID",
                new
                {
                    FromDate = fromDate,
                    ToDate = toDate
                },
                transaction,
                commandTimeout: 300);

            return rows.ToList();
        }

        private static async Task<List<SrBonusFinalRow>> LoadSrBonusRowsAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            string cityCode)
        {
            var rows = await connection.QueryAsync<SrBonusFinalRow>(
                @"SELECT
                      LPAD(CAST(Emp_No AS CHAR), 14, '0') AS Emp_No,
                      SUM(IFNULL(Bonus, 0)) AS BonusAmount
                  FROM lcs_hr.hr_incentive_overall_sr
                  WHERE Year = @Year
                    AND Month = @Month
                    AND citycode = @CityCode
                    AND is_eligible = 'Eligible'
                  GROUP BY LPAD(CAST(Emp_No AS CHAR), 14, '0')",
                new
                {
                    Year = year,
                    Month = month,
                    CityCode = cityCode
                },
                transaction,
                commandTimeout: 300);

            return rows.ToList();
        }

        private static void DistributeOleLocationCommission(
            List<OLEEmpList> allEmployees,
            IEnumerable<OLECommission> source,
            int commissionId,
            Action<OLEEmpList, decimal> assignAmount)
        {
            foreach (var item in source)
            {
                var employees = allEmployees
                    .Where(employee => employee.LocationId == item.GlLocationId && employee.CommissionId == commissionId)
                    .ToList();

                if (employees.Count == 0)
                {
                    continue;
                }

                int totalWorkingDays = employees.Sum(employee => employee.WD);
                if (totalWorkingDays <= 0)
                {
                    continue;
                }

                decimal amountPerDay = item.OleCommission / totalWorkingDays;
                employees.ForEach(employee => assignAmount(employee, employee.WD * amountPerDay));
            }
        }

        private static void AppendCorporateRbiRows(List<OLEEmpList> allEmployees, IEnumerable<OLECommission> corporateRbi, CommissionConfig cfg)
        {
            // RateID → CommissionColumn mapping loaded from hr_commission_type_mapping (OLECommission rows with RateID)
            var rateMap = cfg.OleRateIdToColumn;
            decimal V(OLECommission item, string col) =>
                rateMap.TryGetValue(item.RateID, out var mapped) && mapped == col ? item.OleCommission : 0m;

            foreach (var item in corporateRbi)
            {
                allEmployees.Add(new OLEEmpList
                {
                    Emp_no = item.Emp_No,
                    Cour_id = item.CourierID,
                    INTL_CREDIT              = V(item, "INTL_CREDIT"),
                    CEB_Upto_2KG             = V(item, "CEB_UpTo_2Kg"),
                    CEB_Above_2KG            = V(item, "CEB_Above_2Kg"),
                    ECON_Credit_Booking      = V(item, "Cor_Economy_Booking"),
                    OLE_CORP_Booking         = V(item, "Cor_Ole_Booking"),
                    CEB_Upto_2KG_Exis        = V(item, "CEB_Upto_2KG_Exis"),
                    CEB_Upto_2KG_New         = V(item, "CEB_Upto_2KG_New"),
                    CEB_Above_2Kg_Exis       = V(item, "CEB_Above_2Kg_Exis"),
                    CEB_Above_2Kg_New        = V(item, "CEB_Above_2Kg_New"),
                    ECON_Credit_Booking_Exis = V(item, "ECON_Credit_Booking_Exis"),
                    ECON_Credit_Booking_New  = V(item, "ECON_Credit_Booking_New"),
                    OLE_CORP_Booking_Exis    = V(item, "OLE_CORP_Booking_Exis"),
                    OLE_CORP_Booking_New     = V(item, "OLE_CORP_Booking_New"),
                    Project_Local_Exis       = V(item, "Project_Local_Exis"),
                    Project_Local_New        = V(item, "Project_Local_New"),
                    Project_Domestic_Exis    = V(item, "Project_Domestic_Exis"),
                    Project_Domestic_New     = V(item, "Project_Domestic_New")
                });
            }
        }

        private static void ApplyOleVasFinalRows(List<OLEEmpList> allEmployees, IEnumerable<OleVasFinalRow> oleVasRows)
        {
            // ── PERF: Build dictionary indexes for O(1) employee lookups ──────────
            // Original: O(oleVasRows × allEmployees) linear scans per lookup.
            // Now: O(1) per lookup via hash-based dictionaries.
            var byEmpAndCour = new Dictionary<string, OLEEmpList>(StringComparer.OrdinalIgnoreCase);
            var byCourId = new Dictionary<string, OLEEmpList>(StringComparer.OrdinalIgnoreCase);
            var byEmpNullCour = new Dictionary<string, OLEEmpList>(StringComparer.OrdinalIgnoreCase);
            var byEmpAny = new Dictionary<string, OLEEmpList>(StringComparer.OrdinalIgnoreCase);

            foreach (var emp in allEmployees)
            {
                var empKey = (emp.Emp_no ?? string.Empty).Trim();
                var courKey = (emp.Cour_id ?? string.Empty).Trim();
                var compositeKey = empKey + "|" + courKey;

                if (!string.IsNullOrWhiteSpace(empKey) && !string.IsNullOrWhiteSpace(courKey))
                    byEmpAndCour.TryAdd(compositeKey, emp);

                if (!string.IsNullOrWhiteSpace(courKey))
                    byCourId.TryAdd(courKey, emp);

                if (!string.IsNullOrWhiteSpace(empKey) && string.IsNullOrWhiteSpace(courKey))
                    byEmpNullCour.TryAdd(empKey, emp);

                if (!string.IsNullOrWhiteSpace(empKey))
                    byEmpAny.TryAdd(empKey, emp);
            }

            foreach (var item in oleVasRows)
            {
                string empNo = item.Emp_No?.Trim() ?? string.Empty;
                string sourceCourId = item.SourceCour_id?.Trim() ?? string.Empty;
                string courId = item.Cour_id?.Trim() ?? string.Empty;

                OLEEmpList? employee = null;
                if (!string.IsNullOrWhiteSpace(empNo))
                {
                    byEmpAndCour.TryGetValue(empNo + "|" + courId, out employee);
                }

                if (employee == null && string.IsNullOrWhiteSpace(empNo) && !string.IsNullOrWhiteSpace(sourceCourId))
                {
                    byCourId.TryGetValue(sourceCourId, out employee);

                    if (employee != null && !string.IsNullOrWhiteSpace(employee.Emp_no))
                    {
                        empNo = employee.Emp_no!;
                    }
                }

                if (employee == null)
                {
                    if (string.IsNullOrWhiteSpace(empNo))
                    {
                        continue;
                    }

                    // Prefer an existing null-Cour_id row for the same employee before creating a
                    // new split row.  Location-based staff (Type 004/006, CommissionId 2/3/4) hold
                    // all their commission in a single null-Cour_id row; routing a non-null
                    // COURIER_ID VAS row to a fresh row would split their data across two records.
                    byEmpNullCour.TryGetValue(empNo, out employee);

                    // If no null-Cour_id row, merge into ANY existing row for this employee
                    // to prevent split-row creation.  The final AggregateFinalCommissionByEmployee
                    // step will collapse everything by Emp_no anyway.
                    if (employee == null)
                    {
                        byEmpAny.TryGetValue(empNo, out employee);
                    }

                    // Only create a truly new row if this employee has no row at all yet.
                    if (employee == null)
                    {
                        employee = CreateCashCommissionEmployee(
                            template: null,
                            empNo,
                            string.IsNullOrWhiteSpace(courId) ? null : courId);
                        allEmployees.Add(employee);

                        // Update indexes with the newly added employee
                        byEmpAny.TryAdd(empNo, employee);
                        if (string.IsNullOrWhiteSpace(courId))
                            byEmpNullCour.TryAdd(empNo, employee);
                    }
                }

                // SQL returns one row per (Emp_No, COURIER_ID, RateID) via GROUP BY.
                // Each case overwrites only its own column — cash-derived columns
                // (DomesticDlivery, LOCAL_DLD) and OLE_Delivery from hr_olecommissionprocess
                // are intentionally NOT touched here.
                switch (item.RateID)
                {
                    case 5:
                        employee.OLE_Delivery = item.Amount;
                        break;
                    case 96:
                        employee.Passport = item.Amount;
                        break;
                    case 97:
                        employee.Credit_Debit_Card = item.Amount;
                        break;
                    case 98:
                        employee.ECommerce_Zero_COD = item.Amount;
                        break;
                    case 99:
                        employee.CNIC_Card = item.Amount;
                        break;
                    case 105:
                        employee.General_Light_Delivery = item.Amount;
                        break;
                    case 106:
                        employee.General_Heavy_Delivery = item.Amount;
                        break;
                    case 107:
                        employee.MTD_Delivery = item.Amount;
                        break;
                    case 108:
                        employee.Giftwifts_Delivery = item.Amount;
                        break;
                    case 109:
                        employee.SOA = item.Amount;
                        break;
                    case 110:
                        employee.Utility_Bill = item.Amount;
                        break;
                }
            }
        }

        private static void ApplySrBonusRows(List<OLEEmpList> allEmployees, IEnumerable<SrBonusFinalRow> srBonusRows)
        {
            // ── PERF: Build dictionary indexes for O(1) employee lookups ──────────
            var byEmpWithCour = new Dictionary<string, OLEEmpList>(StringComparer.OrdinalIgnoreCase);
            var byEmpAny = new Dictionary<string, OLEEmpList>(StringComparer.OrdinalIgnoreCase);

            foreach (var emp in allEmployees)
            {
                var key = (emp.Emp_no ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!string.IsNullOrWhiteSpace(emp.Cour_id))
                    byEmpWithCour.TryAdd(key, emp);

                byEmpAny.TryAdd(key, emp);
            }

            foreach (var row in srBonusRows)
            {
                var empKey = (row.Emp_No ?? string.Empty).Trim();
                OLEEmpList? employee = null;

                if (!string.IsNullOrWhiteSpace(empKey))
                {
                    if (!byEmpWithCour.TryGetValue(empKey, out employee))
                        byEmpAny.TryGetValue(empKey, out employee);
                }

                if (employee == null)
                {
                    employee = CreateCashCommissionEmployee(template: null, row.Emp_No, courId: null);
                    allEmployees.Add(employee);
                    byEmpAny.TryAdd(empKey, employee);
                }

                employee.Ecom_overall_SR_Bonus = row.BonusAmount;
            }
        }

        private static List<OLEEmpList> AggregateEmployeeCommission(IEnumerable<OLEEmpList> allEmployees)
        {
            return allEmployees
                .GroupBy(employee => new { employee.Emp_no, employee.Cour_id })
                .Select(group =>
                {
                    var first = group.First();
                    return new OLEEmpList
                    {
                        Emp_no = group.Key.Emp_no,
                        Cour_id = group.Key.Cour_id,
                        IsEligible = first.IsEligible,
                        WD = first.WD,
                        CodeType = first.CodeType,
                        CommissionId = first.CommissionId,
                        LocationId = first.LocationId,
                        OLEDispatchProperAmount = group.Sum(item => item.OLEDispatchProperAmount),
                        OLETransitDispatchAmount = group.Sum(item => item.OLETransitDispatchAmount),
                        OLEDeliveryOPSAmount = group.Sum(item => item.OLEDeliveryOPSAmount),
                        OLE_Delivery = group.Sum(item => item.OLE_Delivery),
                        OLE_Credit_Booking = group.Sum(item => item.OLE_Credit_Booking),
                        CEB_Upto_2KG = group.Sum(item => item.CEB_Upto_2KG),
                        CEB_Above_2KG = group.Sum(item => item.CEB_Above_2KG),
                        ECON_Credit_Booking = group.Sum(item => item.ECON_Credit_Booking),
                        OLE_CORP_Booking = group.Sum(item => item.OLE_CORP_Booking),
                        CEB_Upto_2KG_Exis = group.Sum(item => item.CEB_Upto_2KG_Exis),
                        CEB_Upto_2KG_New = group.Sum(item => item.CEB_Upto_2KG_New),
                        CEB_Above_2Kg_Exis = group.Sum(item => item.CEB_Above_2Kg_Exis),
                        CEB_Above_2Kg_New = group.Sum(item => item.CEB_Above_2Kg_New),
                        ECON_Credit_Booking_Exis = group.Sum(item => item.ECON_Credit_Booking_Exis),
                        ECON_Credit_Booking_New = group.Sum(item => item.ECON_Credit_Booking_New),
                        OLE_CORP_Booking_Exis = group.Sum(item => item.OLE_CORP_Booking_Exis),
                        OLE_CORP_Booking_New = group.Sum(item => item.OLE_CORP_Booking_New),
                        Project_Local_Exis = group.Sum(item => item.Project_Local_Exis),
                        Project_Local_New = group.Sum(item => item.Project_Local_New),
                        Project_Domestic_Exis = group.Sum(item => item.Project_Domestic_Exis),
                        Project_Domestic_New = group.Sum(item => item.Project_Domestic_New),
                        General_Light_Delivery = group.Sum(item => item.General_Light_Delivery),
                        General_Heavy_Delivery = group.Sum(item => item.General_Heavy_Delivery),
                        MTD_Delivery = group.Sum(item => item.MTD_Delivery),
                        Giftwifts_Delivery = group.Sum(item => item.Giftwifts_Delivery),
                        SOA = group.Sum(item => item.SOA),
                        Utility_Bill = group.Sum(item => item.Utility_Bill),
                        Ecom_overall_SR_Bonus = group.Sum(item => item.Ecom_overall_SR_Bonus)
                    };
                })
                .ToList();
        }

        /// <summary>
        /// Final deduplication safety gate — collapses all rows to exactly one row
        /// per Emp_no.  This runs AFTER MergeCashRowsIntoEmployees, ApplyOleVasFinalRows,
        /// and ApplySrBonusRows (all of which can .Add() new objects) and BEFORE DB insert.
        /// The payroll identity boundary is {Emp_No, CityCode, Year, Month}; CityCode/Year/Month
        /// are constant within one ProcessOleRecordsAsync call, so grouping by Emp_no alone
        /// guarantees one row per payroll identity.
        /// </summary>
        private static List<OLEEmpList> AggregateFinalCommissionByEmployee(IEnumerable<OLEEmpList> allEmployees)
        {
            return allEmployees
                .GroupBy(employee => (employee.Emp_no ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    // Pick the most informative Cour_id: prefer non-null, non-empty.
                    var bestCourId = group
                        .Select(item => item.Cour_id)
                        .FirstOrDefault(cid => !string.IsNullOrWhiteSpace(cid))
                        ?? first.Cour_id;

                    return new OLEEmpList
                    {
                        Emp_no = first.Emp_no,
                        Cour_id = bestCourId,
                        IsEligible = group.Any(item => item.IsEligible),
                        WD = group.Max(item => item.WD),
                        CodeType = first.CodeType,
                        CommissionId = first.CommissionId,
                        LocationId = first.LocationId,
                        OLEDispatchProperAmount = group.Sum(item => item.OLEDispatchProperAmount),
                        OLETransitDispatchAmount = group.Sum(item => item.OLETransitDispatchAmount),
                        OLEDeliveryOPSAmount = group.Sum(item => item.OLEDeliveryOPSAmount),
                        OLE_Delivery = group.Sum(item => item.OLE_Delivery),
                        OLE_Credit_Booking = group.Sum(item => item.OLE_Credit_Booking),
                        DOM_CREDIT = group.Sum(item => item.DOM_CREDIT),
                        LOCAL_CREDIT = group.Sum(item => item.LOCAL_CREDIT),
                        LOCAL_DLD = group.Sum(item => item.LOCAL_DLD),
                        PMCL = group.Sum(item => item.PMCL),
                        DomesticDlivery = group.Sum(item => item.DomesticDlivery),
                        INTL_CREDIT = group.Sum(item => item.INTL_CREDIT),
                        Porter = group.Sum(item => item.Porter),
                        COD = group.Sum(item => item.COD),
                        OVERNIGHT = group.Sum(item => item.OVERNIGHT),
                        YB1KG = group.Sum(item => item.YB1KG),
                        YB2KG = group.Sum(item => item.YB2KG),
                        YB5KG = group.Sum(item => item.YB5KG),
                        YB10KG = group.Sum(item => item.YB10KG),
                        YB15KG = group.Sum(item => item.YB15KG),
                        YB25KG = group.Sum(item => item.YB25KG),
                        FLAYER = group.Sum(item => item.FLAYER),
                        DETAIN = group.Sum(item => item.DETAIN),
                        OVERLAND = group.Sum(item => item.OVERLAND),
                        PREPAID = group.Sum(item => item.PREPAID),
                        LOVELINE = group.Sum(item => item.LOVELINE),
                        INTL_CASH = group.Sum(item => item.INTL_CASH),
                        MOFA_OTO = group.Sum(item => item.MOFA_OTO),
                        MOFA_OTD = group.Sum(item => item.MOFA_OTD),
                        RMS_COD = group.Sum(item => item.RMS_COD),
                        CEB_Upto_2KG = group.Sum(item => item.CEB_Upto_2KG),
                        CEB_Above_2KG = group.Sum(item => item.CEB_Above_2KG),
                        ECON_Credit_Booking = group.Sum(item => item.ECON_Credit_Booking),
                        OLE_CORP_Booking = group.Sum(item => item.OLE_CORP_Booking),
                        CASH_EXP_BKG_UpTo_2Kg = group.Sum(item => item.CASH_EXP_BKG_UpTo_2Kg),
                        CASH_EXP_BKG_Above_2Kg = group.Sum(item => item.CASH_EXP_BKG_Above_2Kg),
                        CASH_Leop_BOX_Above_2Kg = group.Sum(item => item.CASH_Leop_BOX_Above_2Kg),
                        CASH_Economy_Booking = group.Sum(item => item.CASH_Economy_Booking),
                        CASH_OLE_Booking = group.Sum(item => item.CASH_OLE_Booking),
                        RetailDeduction = group.Sum(item => item.RetailDeduction),
                        Insurance_Com = group.Sum(item => item.Insurance_Com),
                        CEB_Upto_2KG_Exis = group.Sum(item => item.CEB_Upto_2KG_Exis),
                        CEB_Upto_2KG_New = group.Sum(item => item.CEB_Upto_2KG_New),
                        CEB_Above_2Kg_Exis = group.Sum(item => item.CEB_Above_2Kg_Exis),
                        CEB_Above_2Kg_New = group.Sum(item => item.CEB_Above_2Kg_New),
                        ECON_Credit_Booking_Exis = group.Sum(item => item.ECON_Credit_Booking_Exis),
                        ECON_Credit_Booking_New = group.Sum(item => item.ECON_Credit_Booking_New),
                        OLE_CORP_Booking_Exis = group.Sum(item => item.OLE_CORP_Booking_Exis),
                        OLE_CORP_Booking_New = group.Sum(item => item.OLE_CORP_Booking_New),
                        Project_Local_Exis = group.Sum(item => item.Project_Local_Exis),
                        Project_Local_New = group.Sum(item => item.Project_Local_New),
                        Project_Domestic_Exis = group.Sum(item => item.Project_Domestic_Exis),
                        Project_Domestic_New = group.Sum(item => item.Project_Domestic_New),
                        AllInOne = group.Sum(item => item.AllInOne),
                        DocumnetCare = group.Sum(item => item.DocumnetCare),
                        MTD = group.Sum(item => item.MTD),
                        VAS = group.Sum(item => item.VAS),
                        IntlDox = group.Sum(item => item.IntlDox),
                        IntlEconomy = group.Sum(item => item.IntlEconomy),
                        IntlParcel = group.Sum(item => item.IntlParcel),
                        ONUpto1kg = group.Sum(item => item.ONUpto1kg),
                        ONAbove1kg = group.Sum(item => item.ONAbove1kg),
                        ONUpto1kgRetailCOD = group.Sum(item => item.ONUpto1kgRetailCOD),
                        ONAbove1kgRetailCOD = group.Sum(item => item.ONAbove1kgRetailCOD),
                        EconomyRetail = group.Sum(item => item.EconomyRetail),
                        YB1KGRetail = group.Sum(item => item.YB1KGRetail),
                        YB2KGRetail = group.Sum(item => item.YB2KGRetail),
                        YB5KGRetail = group.Sum(item => item.YB5KGRetail),
                        YB10KGRetail = group.Sum(item => item.YB10KGRetail),
                        YB15KGRetail = group.Sum(item => item.YB15KGRetail),
                        YB25KGRetail = group.Sum(item => item.YB25KGRetail),
                        MyCollect = group.Sum(item => item.MyCollect),
                        Attestation = group.Sum(item => item.Attestation),
                        Credit_Debit_Card = group.Sum(item => item.Credit_Debit_Card),
                        ECommerce_Zero_COD = group.Sum(item => item.ECommerce_Zero_COD),
                        Passport = group.Sum(item => item.Passport),
                        CNIC_Card = group.Sum(item => item.CNIC_Card),
                        Return_E_Com = group.Sum(item => item.Return_E_Com),
                        Pickup_Leopard = group.Sum(item => item.Pickup_Leopard),
                        COD_Bonus = group.Sum(item => item.COD_Bonus),
                        COD_Deduction = group.Sum(item => item.COD_Deduction),
                        SOA = group.Sum(item => item.SOA),
                        Utility_Bill = group.Sum(item => item.Utility_Bill),
                        General_Light_Delivery = group.Sum(item => item.General_Light_Delivery),
                        General_Heavy_Delivery = group.Sum(item => item.General_Heavy_Delivery),
                        MTD_Delivery = group.Sum(item => item.MTD_Delivery),
                        Giftwifts_Delivery = group.Sum(item => item.Giftwifts_Delivery),
                        Ecom_overall_SR_Bonus = group.Sum(item => item.Ecom_overall_SR_Bonus)
                    };
                })
                .ToList();
        }

        private static List<CommissionProcessPreviewEmployee> BuildCommissionPreviewEmployees(
            IEnumerable<OLEEmpList> allEmployees,
            IEnumerable<EmpCommAdjDtl> adjustments)
        {
            return allEmployees
                .GroupBy(employee => employee.Emp_no ?? string.Empty)
                .Select(group =>
                {
                    var rows = group.ToList();
                    var first = rows.FirstOrDefault();
                    return new CommissionProcessPreviewEmployee
                    {
                        EmpNo = group.Key,
                        RouteCode = rows.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Cour_id))?.Cour_id,
                        WorkingDays = first?.WD ?? 0,
                        IsEligible = first?.IsEligible ?? false,
                        TotalCommission = rows.Sum(CalculateCommissionTotal),
                        AdjustmentAmount = adjustments
                            .Where(adjustment => string.Equals(adjustment.Emp_No, group.Key, StringComparison.OrdinalIgnoreCase))
                            .Sum(adjustment => adjustment.Amount)
                    };
                })
                .OrderBy(employee => employee.EmpNo)
                .ToList();
        }

        private static void MergeCashRowsIntoEmployees(List<OLEEmpList> allEmployees, DataTable cashRows)
        {
            // ── PERF: Build dictionary indexes for O(1) employee lookups ──────────
            // Keyed by Emp_no for fast grouping, and composite Emp_no|Cour_id for exact match.
            var byEmpNo = new Dictionary<string, List<OLEEmpList>>(StringComparer.OrdinalIgnoreCase);
            var byEmpCour = new Dictionary<string, OLEEmpList>(StringComparer.OrdinalIgnoreCase);

            foreach (var emp in allEmployees)
            {
                var key = (emp.Emp_no ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!byEmpNo.TryGetValue(key, out var list))
                {
                    list = new List<OLEEmpList>();
                    byEmpNo[key] = list;
                }
                list.Add(emp);

                var courKey = (emp.Cour_id ?? string.Empty).Trim();
                byEmpCour.TryAdd(key + "|" + courKey, emp);
            }

            foreach (DataRow cashRow in cashRows.Rows)
            {
                var empNo = GetRowString(cashRow, "emp_no");
                var courId = GetRowString(cashRow, "Cour_id");
                var empNoTrimmed = empNo.Trim();
                var courIdTrimmed = courId.Trim();

                byEmpCour.TryGetValue(empNoTrimmed + "|" + courIdTrimmed, out var matchingEmployee);

                if (matchingEmployee != null)
                {
                    ApplyCashCommissionRow(matchingEmployee, cashRow);
                }
                else
                {
                    byEmpNo.TryGetValue(empNoTrimmed, out var employees);
                    var template = employees?.FirstOrDefault();
                    var employee = CreateCashCommissionEmployee(template, empNo, courId);
                    ApplyCashCommissionRow(employee, cashRow);
                    allEmployees.Add(employee);

                    // Update indexes with the newly added employee
                    if (!string.IsNullOrWhiteSpace(empNoTrimmed))
                    {
                        if (!byEmpNo.TryGetValue(empNoTrimmed, out var list2))
                        {
                            list2 = new List<OLEEmpList>();
                            byEmpNo[empNoTrimmed] = list2;
                        }
                        list2.Add(employee);
                        byEmpCour.TryAdd(empNoTrimmed + "|" + courIdTrimmed, employee);
                    }
                }
            }
        }

        private static OLEEmpList CreateCashCommissionEmployee(OLEEmpList? template, string? empNo, string? courId)
        {
            return new OLEEmpList
            {
                Emp_no = empNo,
                Cour_id = courId,
                LocationId = template?.LocationId ?? 0,
                WD = template?.WD ?? 0,
                IsEligible = template?.IsEligible ?? false,
                CodeType = template?.CodeType ?? 0,
                CommissionId = template?.CommissionId ?? 0
            };
        }

        private static void ApplyCashCommissionRow(OLEEmpList employee, DataRow row)
        {
            employee.Cour_id = GetRowString(row, "Cour_id");
            employee.DOM_CREDIT = GetRowDecimal(row, "DOM_CREDIT");
            employee.LOCAL_CREDIT = GetRowDecimal(row, "LOCAL_CREDIT");
            employee.LOCAL_DLD = GetRowDecimal(row, "LOCAL_DLD");
            employee.PMCL = GetRowDecimal(row, "PMCL");
            employee.INTL_CREDIT = GetRowDecimal(row, "INTL_CREDIT");
            employee.DomesticDlivery = GetRowDecimal(row, "DomesticDelivery");
            employee.Porter = GetRowDecimal(row, "Porter");
            employee.COD = GetRowDecimal(row, "COD");
            employee.OVERNIGHT = GetRowDecimal(row, "OVERNIGHT");
            employee.YB1KG = GetRowDecimal(row, "YB1KG");
            employee.YB2KG = GetRowDecimal(row, "YB2KG");
            employee.YB5KG = GetRowDecimal(row, "YB5KG");
            employee.YB10KG = GetRowDecimal(row, "YB10KG");
            employee.YB15KG = GetRowDecimal(row, "YB15KG");
            employee.YB25KG = GetRowDecimal(row, "YB25KG");
            employee.FLAYER = GetRowDecimal(row, "FLAYER");
            employee.DETAIN = GetRowDecimal(row, "DETAIN");
            employee.OVERLAND = GetRowDecimal(row, "OVERLAND");
            employee.PREPAID = GetRowDecimal(row, "PREPAID");
            employee.LOVELINE = GetRowDecimal(row, "LOVELINE");
            employee.INTL_CASH = GetRowDecimal(row, "INTL_CASH");
            employee.MOFA_OTO = GetRowDecimal(row, "MOFA_OTO");
            employee.MOFA_OTD = GetRowDecimal(row, "MOFA_OTD");
            employee.RMS_COD = GetRowDecimal(row, "Rms_Cod_Booking");
            employee.CASH_EXP_BKG_UpTo_2Kg = GetRowDecimal(row, "CASH_EXP_BKG_UpTo_2Kg");
            employee.CASH_EXP_BKG_Above_2Kg = GetRowDecimal(row, "CASH_EXP_BKG_Above_2Kg");
            employee.CASH_Leop_BOX_Above_2Kg = GetRowDecimal(row, "CASH_Leop_BOX_Above_2Kg");
            employee.CASH_Economy_Booking = GetRowDecimal(row, "CASH_Economy_Booking");
            employee.CASH_OLE_Booking = GetRowDecimal(row, "CASH_OLE_Booking");
            employee.RetailDeduction = GetRowDecimal(row, "Retail_Deduction");
            employee.Insurance_Com = GetRowDecimal(row, "Insurance_Com");
            employee.AllInOne = GetRowDecimal(row, "AllInOne");
            employee.DocumnetCare = GetRowDecimal(row, "DocumnetCare");
            employee.MTD = GetRowDecimal(row, "MTD");
            employee.VAS = GetRowDecimal(row, "VAS");
            employee.IntlDox = GetRowDecimal(row, "IntlDox");
            employee.IntlEconomy = GetRowDecimal(row, "IntlEconomy");
            employee.IntlParcel = GetRowDecimal(row, "IntlParcel");
            employee.ONUpto1kg = GetRowDecimal(row, "ONUpto1kg");
            employee.ONAbove1kg = GetRowDecimal(row, "ONAbove1kg");
            employee.ONUpto1kgRetailCOD = GetRowDecimal(row, "ONUpto1kgRetailCOD");
            employee.ONAbove1kgRetailCOD = GetRowDecimal(row, "ONAbove1kgRetailCOD");
            employee.EconomyRetail = GetRowDecimal(row, "EconomyRetail");
            employee.YB1KGRetail = GetRowDecimal(row, "YB1KGRetail");
            employee.YB2KGRetail = GetRowDecimal(row, "YB2KGRetail");
            employee.YB5KGRetail = GetRowDecimal(row, "YB5KGRetail");
            employee.YB10KGRetail = GetRowDecimal(row, "YB10KGRetail");
            employee.YB15KGRetail = GetRowDecimal(row, "YB15KGRetail");
            employee.YB25KGRetail = GetRowDecimal(row, "YB25KGRetail");
            employee.MyCollect = GetRowDecimal(row, "MyCollect");
            employee.Attestation = GetRowDecimal(row, "Attestation");
            employee.Credit_Debit_Card = GetRowDecimal(row, "Credit_Debit_Card");
            employee.ECommerce_Zero_COD = GetRowDecimal(row, "ECommerce_Zero_COD");
            employee.Passport = GetRowDecimal(row, "Passport");
            employee.CNIC_Card = GetRowDecimal(row, "CNIC_Card");
            employee.Return_E_Com = GetRowDecimal(row, "Return_E_Com");
            employee.Pickup_Leopard = GetRowDecimal(row, "Pickup_Leopard");
            employee.COD_Bonus = GetRowDecimal(row, "COD_Bonus");
            employee.COD_Deduction = GetRowDecimal(row, "COD_Deduction");
        }

        private static List<EmpCommAdjDtl> BuildCommissionAdjustments(
            IEnumerable<OLEEmpList> allEmployees,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            CommissionConfig cfg)
        {
            var adjustments = new List<EmpCommAdjDtl>();

            foreach (var group in allEmployees.GroupBy(employee => employee.Emp_no))
            {
                var rows = group.ToList();
                var first = rows.FirstOrDefault();
                if (first == null)
                {
                    continue;
                }

                decimal total = rows.Sum(CalculateCommissionTotal);
                decimal rbiTotal = rows.Sum(CalculateRbiTotal);

                if (total > 0m && total < cfg.MinGuaranteeAmount && first.IsEligible)
                {
                    int workingDays = first.WD;
                    if (workingDays > 0)
                    {
                        decimal fairAmount = cfg.MinGuaranteeAmount / cfg.MinGuaranteeWorkingDays * workingDays;
                        fairAmount = fairAmount > cfg.MinGuaranteeAmount ? cfg.MinGuaranteeAmount : fairAmount;

                        if (fairAmount > total)
                        {
                            adjustments.Add(new EmpCommAdjDtl
                            {
                                Emp_No = group.Key,
                                Amount = fairAmount - total,
                                Adjusment_Type_id = MinimumGuaranteeAdjustmentTypeId,
                                Year = year,
                                Month = month,
                                CreatedBy = currentUserId,
                                City_Code = cityCode
                            });
                        }
                    }
                }

                if (rbiTotal > cfg.RbiCap)
                {
                    adjustments.Add(new EmpCommAdjDtl
                    {
                        Emp_No = group.Key,
                        Amount = cfg.RbiCap - rbiTotal,
                        Adjusment_Type_id = MinimumGuaranteeAdjustmentTypeId,
                        Year = year,
                        Month = month,
                        CreatedBy = currentUserId,
                        City_Code = cityCode
                    });
                }

            }

            return adjustments;
        }

        private static decimal CalculateCommissionTotal(OLEEmpList employee)
        {
            return employee.DOM_CREDIT + employee.LOCAL_CREDIT + employee.OLE_Credit_Booking + employee.LOCAL_DLD
                 + employee.PMCL + employee.INTL_CREDIT + employee.DomesticDlivery + employee.Porter + employee.COD + employee.OVERNIGHT
                 + employee.YB1KG + employee.YB2KG + employee.YB5KG + employee.YB10KG + employee.YB15KG
                 + employee.YB25KG + employee.FLAYER + employee.DETAIN + employee.OVERLAND + employee.PREPAID
                 + employee.LOVELINE + employee.INTL_CASH + employee.OLEDeliveryOPSAmount + employee.OLEDispatchProperAmount
                 + employee.OLETransitDispatchAmount + employee.OLE_Delivery + employee.MOFA_OTO + employee.MOFA_OTD
                 + employee.RMS_COD + employee.CEB_Upto_2KG + employee.CEB_Above_2KG + employee.ECON_Credit_Booking
                 + employee.OLE_CORP_Booking + employee.AllInOne + employee.DocumnetCare + employee.MTD + employee.VAS
                 + employee.IntlDox + employee.IntlEconomy + employee.IntlParcel + employee.ONUpto1kg + employee.ONAbove1kg
                 + employee.ONUpto1kgRetailCOD + employee.ONAbove1kgRetailCOD + employee.EconomyRetail + employee.YB1KGRetail
                 + employee.YB2KGRetail + employee.YB5KGRetail + employee.YB10KGRetail + employee.YB15KGRetail
                 + employee.YB25KGRetail + employee.MyCollect + employee.Attestation + employee.Insurance_Com
                 + employee.CEB_Upto_2KG_Exis + employee.CEB_Upto_2KG_New + employee.CEB_Above_2Kg_Exis
                 + employee.CEB_Above_2Kg_New + employee.ECON_Credit_Booking_Exis + employee.ECON_Credit_Booking_New
                 + employee.OLE_CORP_Booking_Exis + employee.OLE_CORP_Booking_New + employee.Project_Local_Exis
                 + employee.Project_Local_New + employee.Project_Domestic_Exis + employee.Project_Domestic_New
                 + employee.CASH_EXP_BKG_UpTo_2Kg + employee.CASH_EXP_BKG_Above_2Kg + employee.CASH_Leop_BOX_Above_2Kg
                 + employee.CASH_Economy_Booking + employee.CASH_OLE_Booking + employee.Credit_Debit_Card
                 + employee.ECommerce_Zero_COD + employee.Passport + employee.CNIC_Card + employee.Return_E_Com
                 + employee.Pickup_Leopard + employee.COD_Bonus + employee.COD_Deduction
                 + employee.SOA + employee.Utility_Bill + employee.General_Light_Delivery
                 + employee.General_Heavy_Delivery + employee.MTD_Delivery + employee.Giftwifts_Delivery
                 + employee.Ecom_overall_SR_Bonus;
        }

        private static bool HasAnyFinancialValue(OLEEmpList employee)
        {
            return new[]
            {
                employee.DOM_CREDIT,
                employee.LOCAL_CREDIT,
                employee.LOCAL_DLD,
                employee.PMCL,
                employee.DomesticDlivery,
                employee.INTL_CREDIT,
                employee.Porter,
                employee.OLEDispatchProperAmount,
                employee.OLETransitDispatchAmount,
                employee.OLEDeliveryOPSAmount,
                employee.OLE_Credit_Booking,
                employee.OLE_Delivery,
                employee.COD,
                employee.OVERNIGHT,
                employee.YB1KG,
                employee.YB2KG,
                employee.YB5KG,
                employee.YB10KG,
                employee.YB15KG,
                employee.YB25KG,
                employee.FLAYER,
                employee.DETAIN,
                employee.OVERLAND,
                employee.PREPAID,
                employee.LOVELINE,
                employee.INTL_CASH,
                employee.MOFA_OTO,
                employee.MOFA_OTD,
                employee.RMS_COD,
                employee.CEB_Upto_2KG,
                employee.CEB_Above_2KG,
                employee.ECON_Credit_Booking,
                employee.OLE_CORP_Booking,
                employee.AllInOne,
                employee.DocumnetCare,
                employee.MTD,
                employee.VAS,
                employee.IntlDox,
                employee.IntlEconomy,
                employee.IntlParcel,
                employee.ONUpto1kg,
                employee.ONAbove1kg,
                employee.ONUpto1kgRetailCOD,
                employee.ONAbove1kgRetailCOD,
                employee.EconomyRetail,
                employee.YB1KGRetail,
                employee.YB2KGRetail,
                employee.YB5KGRetail,
                employee.YB10KGRetail,
                employee.YB15KGRetail,
                employee.YB25KGRetail,
                employee.MyCollect,
                employee.Attestation,
                employee.CEB_Upto_2KG_Exis,
                employee.CEB_Upto_2KG_New,
                employee.CEB_Above_2Kg_Exis,
                employee.CEB_Above_2Kg_New,
                employee.ECON_Credit_Booking_Exis,
                employee.ECON_Credit_Booking_New,
                employee.OLE_CORP_Booking_Exis,
                employee.OLE_CORP_Booking_New,
                employee.Project_Local_Exis,
                employee.Project_Local_New,
                employee.Project_Domestic_Exis,
                employee.Project_Domestic_New,
                employee.CASH_EXP_BKG_UpTo_2Kg,
                employee.CASH_EXP_BKG_Above_2Kg,
                employee.CASH_Leop_BOX_Above_2Kg,
                employee.CASH_Economy_Booking,
                employee.CASH_OLE_Booking,
                employee.Insurance_Com,
                employee.RetailDeduction,
                employee.Credit_Debit_Card,
                employee.ECommerce_Zero_COD,
                employee.Passport,
                employee.CNIC_Card,
                employee.Return_E_Com,
                employee.Pickup_Leopard,
                employee.SOA,
                employee.Utility_Bill,
                employee.General_Light_Delivery,
                employee.General_Heavy_Delivery,
                employee.MTD_Delivery,
                employee.Giftwifts_Delivery,
                employee.Ecom_overall_SR_Bonus,
                employee.COD_Bonus,
                employee.COD_Deduction
            }.Any(amount => amount != 0m);
        }

        private static decimal CalculateRbiTotal(OLEEmpList employee)
        {
            return employee.DOM_CREDIT + employee.LOCAL_CREDIT + employee.OLE_Credit_Booking
                 + employee.CEB_Upto_2KG + employee.CEB_Above_2KG + employee.ECON_Credit_Booking + employee.OLE_CORP_Booking
                 + employee.CEB_Upto_2KG_Exis + employee.CEB_Upto_2KG_New + employee.CEB_Above_2Kg_Exis + employee.CEB_Above_2Kg_New
                 + employee.ECON_Credit_Booking_Exis + employee.ECON_Credit_Booking_New
                 + employee.OLE_CORP_Booking_Exis + employee.OLE_CORP_Booking_New
                 + employee.Project_Local_Exis + employee.Project_Local_New
                 + employee.Project_Domestic_Exis + employee.Project_Domestic_New;
        }

        private static async Task InsertCommissionRowsAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            List<OLEEmpList> allEmployees,
            int year,
            int month,
            string cityCode,
            string currentUserId)
        {
            if (allEmployees.Count == 0)
            {
                return;
            }

            string query = $@"
                INSERT INTO {AcTestTableNames.T_CommissionProcess}
                (
                    Year, Month, citycode, Cour_id, emp_no, DOM_CREDIT, LOCAL_CREDIT, LOCAL_DLD, PMCL, DomesticDelivery, INTL_CREDIT, Porter,
                    `OLE_Dispatch_Proper`, `OLE_Transit_Dispatch`, `OLE_Delivery_OPS`, OLE_Credit_Booking, OLE_Delivery, COD, OVERNIGHT, YB1KG, YB2KG, YB5KG, YB10KG, YB15KG, YB25KG, FLAYER,
                    DETAIN, OVERLAND, PREPAID, LOVELINE, INTL_CASH, MOFA_OTO, MOFA_OTD, Rms_Cod_Booking, CEB_UpTo_2Kg, CEB_Above_2Kg, Cor_Economy_Booking, Cor_Ole_Booking, AllInOne, DocumnetCare, MTD, VAS, IntlDox, IntlEconomy, IntlParcel, ONUpto1kg, ONAbove1kg, ONUpto1kgRetailCOD, ONAbove1kgRetailCOD, EconomyRetail, YB1KGRetail, YB2KGRetail, YB5KGRetail, YB10KGRetail, YB15KGRetail, YB25KGRetail, MyCollect, Attestation,
                    CEB_Upto_2KG_Exis, CEB_Upto_2KG_New, CEB_Above_2Kg_Exis, CEB_Above_2Kg_New, ECON_Credit_Booking_Exis, ECON_Credit_Booking_New, OLE_CORP_Booking_Exis, OLE_CORP_Booking_New, Project_Local_Exis, Project_Local_New, Project_Domestic_Exis, Project_Domestic_New,
                    CASH_EXP_BKG_UpTo_2Kg, CASH_EXP_BKG_Above_2Kg, CASH_Leop_BOX_Above_2Kg, CASH_Economy_Booking, CASH_OLE_Booking, Insurance_Com, Retail_Deduction,
                    Credit_Debit_Card, ECommerce_Zero_COD, Passport, CNIC_Card, Return_E_Com, Pickup_Leopard,
                    SOA, Utility_Bill, General_Light_Delivery, General_Heavy_Delivery, MTD_Delivery, Giftwifts_Delivery, Ecom_overall_SR_Bonus,
                    COD_Bonus, COD_Deduction,
                    `CreatedBy`, CreatedDate
                )
                VALUES
                (
                    @Year, @Month, @CityCode, @Cour_id, @Emp_no, @DOM_CREDIT, @LOCAL_CREDIT, @LOCAL_DLD, @PMCL, @DomesticDelivery, @INTL_CREDIT, @Porter,
                    @OLE_Dispatch_Proper, @OLE_Transit_Dispatch, @OLE_Delivery_OPS, @OLE_Credit_Booking, @OLE_Delivery, @COD, @OVERNIGHT, @YB1KG, @YB2KG, @YB5KG, @YB10KG, @YB15KG, @YB25KG, @FLAYER,
                    @DETAIN, @OVERLAND, @PREPAID, @LOVELINE, @INTL_CASH, @MOFA_OTO, @MOFA_OTD, @Rms_Cod_Booking, @CEB_UpTo_2Kg, @CEB_Above_2Kg, @Cor_Economy_Booking, @Cor_Ole_Booking, @AllInOne, @DocumnetCare, @MTD, @VAS, @IntlDox, @IntlEconomy, @IntlParcel, @ONUpto1kg, @ONAbove1kg, @ONUpto1kgRetailCOD, @ONAbove1kgRetailCOD, @EconomyRetail, @YB1KGRetail, @YB2KGRetail, @YB5KGRetail, @YB10KGRetail, @YB15KGRetail, @YB25KGRetail, @MyCollect, @Attestation,
                    @CEB_Upto_2KG_Exis, @CEB_Upto_2KG_New, @CEB_Above_2Kg_Exis, @CEB_Above_2Kg_New, @ECON_Credit_Booking_Exis, @ECON_Credit_Booking_New, @OLE_CORP_Booking_Exis, @OLE_CORP_Booking_New, @Project_Local_Exis, @Project_Local_New, @Project_Domestic_Exis, @Project_Domestic_New,
                    @CASH_EXP_BKG_UpTo_2Kg, @CASH_EXP_BKG_Above_2Kg, @CASH_Leop_BOX_Above_2Kg, @CASH_Economy_Booking, @CASH_OLE_Booking, @Insurance_Com, @Retail_Deduction,
                    @Credit_Debit_Card, @ECommerce_Zero_COD, @Passport, @CNIC_Card, @Return_E_Com, @Pickup_Leopard,
                    @SOA, @Utility_Bill, @General_Light_Delivery, @General_Heavy_Delivery, @MTD_Delivery, @Giftwifts_Delivery, @Ecom_overall_SR_Bonus,
                    @COD_Bonus, @COD_Deduction,
                    @CreatedBy, @CreatedDate
                );";

            await connection.ExecuteAsync(query, allEmployees.Select(employee => new
            {
                Year = year,
                Month = month,
                CityCode = cityCode,
                employee.Cour_id,
                Emp_no = employee.Emp_no,
                employee.DOM_CREDIT,
                employee.LOCAL_CREDIT,
                employee.LOCAL_DLD,
                employee.PMCL,
                DomesticDelivery = employee.DomesticDlivery,
                employee.INTL_CREDIT,
                employee.Porter,
                OLE_Dispatch_Proper = employee.OLEDispatchProperAmount,
                OLE_Transit_Dispatch = employee.OLETransitDispatchAmount,
                OLE_Delivery_OPS = employee.OLEDeliveryOPSAmount,
                employee.OLE_Credit_Booking,
                OLE_Delivery = employee.OLE_Delivery,
                employee.COD,
                employee.OVERNIGHT,
                employee.YB1KG,
                employee.YB2KG,
                employee.YB5KG,
                employee.YB10KG,
                employee.YB15KG,
                employee.YB25KG,
                employee.FLAYER,
                employee.DETAIN,
                employee.OVERLAND,
                employee.PREPAID,
                employee.LOVELINE,
                employee.INTL_CASH,
                employee.MOFA_OTO,
                employee.MOFA_OTD,
                Rms_Cod_Booking = employee.RMS_COD,
                CEB_UpTo_2Kg = employee.CEB_Upto_2KG,
                CEB_Above_2Kg = employee.CEB_Above_2KG,
                Cor_Economy_Booking = employee.ECON_Credit_Booking,
                Cor_Ole_Booking = employee.OLE_CORP_Booking,
                employee.AllInOne,
                employee.DocumnetCare,
                employee.MTD,
                employee.VAS,
                employee.IntlDox,
                employee.IntlEconomy,
                employee.IntlParcel,
                employee.ONUpto1kg,
                employee.ONAbove1kg,
                employee.ONUpto1kgRetailCOD,
                employee.ONAbove1kgRetailCOD,
                employee.EconomyRetail,
                employee.YB1KGRetail,
                employee.YB2KGRetail,
                employee.YB5KGRetail,
                employee.YB10KGRetail,
                employee.YB15KGRetail,
                employee.YB25KGRetail,
                employee.MyCollect,
                employee.Attestation,
                employee.CEB_Upto_2KG_Exis,
                employee.CEB_Upto_2KG_New,
                employee.CEB_Above_2Kg_Exis,
                employee.CEB_Above_2Kg_New,
                employee.ECON_Credit_Booking_Exis,
                employee.ECON_Credit_Booking_New,
                employee.OLE_CORP_Booking_Exis,
                employee.OLE_CORP_Booking_New,
                employee.Project_Local_Exis,
                employee.Project_Local_New,
                employee.Project_Domestic_Exis,
                employee.Project_Domestic_New,
                employee.CASH_EXP_BKG_UpTo_2Kg,
                employee.CASH_EXP_BKG_Above_2Kg,
                employee.CASH_Leop_BOX_Above_2Kg,
                employee.CASH_Economy_Booking,
                employee.CASH_OLE_Booking,
                employee.Insurance_Com,
                Retail_Deduction = employee.RetailDeduction,
                employee.Credit_Debit_Card,
                employee.ECommerce_Zero_COD,
                employee.Passport,
                employee.CNIC_Card,
                employee.Return_E_Com,
                employee.Pickup_Leopard,
                employee.SOA,
                Utility_Bill = employee.Utility_Bill,
                General_Light_Delivery = employee.General_Light_Delivery,
                General_Heavy_Delivery = employee.General_Heavy_Delivery,
                MTD_Delivery = employee.MTD_Delivery,
                Giftwifts_Delivery = employee.Giftwifts_Delivery,
                Ecom_overall_SR_Bonus = employee.Ecom_overall_SR_Bonus,
                employee.COD_Bonus,
                employee.COD_Deduction,
                CreatedBy = currentUserId,
                CreatedDate = DateTime.Now
            }), transaction);
        }

        private static async Task ReplaceCommissionAdjustmentsAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            List<EmpCommAdjDtl> adjustments,
            int year,
            int month,
            string cityCode,
            CommissionConfig cfg)
        {
            if (AcTestTableNames.IsTestMode)
            {
                await connection.ExecuteAsync(
                    $@"DELETE FROM `lcs_hr`.`{AcTestTableNames.T_EmpCommAdjDtl}`
                      WHERE `Year` = @Year
                        AND `Month` = @Month
                        AND `City_Code` = @CityCode;",
                    new
                    {
                        Year = year,
                        Month = month,
                        CityCode = cityCode
                    },
                    transaction);

                if (cfg.AdjustmentPreserveTypeIds.Length > 0)
                {
                    await connection.ExecuteAsync(
                        $@"INSERT INTO `lcs_hr`.`{AcTestTableNames.T_EmpCommAdjDtl}`
                          (`Year`, `Month`, `Emp_No`, `Amount`, `CreatedBy`, `CreationDate`, `UpdatedBy`, `UpdatedDate`, `City_Code`, `comment`, `Adjusment_Type_id`)
                          SELECT
                              `Year`,
                              `Month`,
                              `Emp_No`,
                              `Amount`,
                              `CreatedBy`,
                              `CreationDate`,
                              `UpdatedBy`,
                              `UpdatedDate`,
                              `City_Code`,
                              `comment`,
                              `Adjusment_Type_id`
                          FROM `lcs_hr`.`{AcTestTableNames.EmpCommAdjDtl}`
                          WHERE `Year` = @Year
                            AND `Month` = @Month
                            AND `City_Code` = @CityCode
                            AND Adjusment_Type_id IN ({cfg.AdjustmentPreserveTypeIdsCsv});",
                        new
                        {
                            Year = year,
                            Month = month,
                            CityCode = cityCode
                        },
                        transaction);
                }
            }
            else
            {
                await connection.ExecuteAsync(
                    $@"DELETE FROM `lcs_hr`.`{AcTestTableNames.T_EmpCommAdjDtl}`
                      WHERE `Year` = @Year
                        AND `Month` = @Month
                        AND `City_Code` = @CityCode
                        AND Adjusment_Type_id NOT IN ({cfg.AdjustmentPreserveTypeIdsCsv});",
                    new
                    {
                        Year = year,
                        Month = month,
                        CityCode = cityCode
                    },
                    transaction);
            }

            if (adjustments.Count == 0)
            {
                return;
            }

            await connection.ExecuteAsync(
                $@"INSERT INTO `lcs_hr`.`{AcTestTableNames.T_EmpCommAdjDtl}`
                  (`Year`, `Month`, `Emp_No`, `Amount`, `CreatedBy`, `City_Code`, `comment`, `Adjusment_Type_id`)
                  VALUES
                  (@Year, @Month, @Emp_No, @Amount, @CreatedBy, @City_Code, @Comment, @Adjusment_Type_id);",
                adjustments,
                transaction);
        }

        // ── PERF: Indexed O(1) lookup variants — used by BuildCommissionProcessRows ────
        // The ILookup<string, DataRow> index is built once per DataTable, then all
        // route-code lookups resolve in O(1) instead of scanning every row.

        private static decimal FirstCommissionValueIndexed(
            Dictionary<string, ILookup<string, DataRow>> index,
            string tableName,
            string routeCode,
            int columnIndex)
        {
            if (!index.TryGetValue(tableName, out var lookup)) return 0m;
            var row = lookup[routeCode].FirstOrDefault();
            return row == null ? 0m : DecimalValue(row, columnIndex);
        }

        private static decimal SumCommissionValueIndexed(
            Dictionary<string, ILookup<string, DataRow>> index,
            string tableName,
            string routeCode,
            string columnName)
        {
            if (!index.TryGetValue(tableName, out var lookup)) return 0m;
            return lookup[routeCode].Sum(row => DecimalValue(row, columnName));
        }

        private static decimal DecimalValue(DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) ? DecimalValue(row, row.Table.Columns[columnName].Ordinal) : 0m;
        }

        private static decimal DecimalValue(DataRow row, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= row.ItemArray.Length)
            {
                return 0m;
            }

            return row[columnIndex] == DBNull.Value ? 0m : Convert.ToDecimal(row[columnIndex], CultureInfo.InvariantCulture);
        }

        private static void AppendCommissionSourceQueriesCore(StringBuilder query, CommissionProcessContext context)
        {
            query.Append($@"SELECT `GlLocationId`, CourierID AS cour_id, OleCommission FROM {OleCommissionProcessTable} WHERE RateID = 6 AND YEAR = {context.Year} AND MONTH = {context.Month} AND `GlLocationId` IN ({context.LocationIdsCsv}); ");
            query.Append($@"SELECT `GlLocationId`, CourierID AS cour_id, OleCommission FROM {OleCommissionProcessTable} WHERE RateID = 7 AND YEAR = {context.Year} AND MONTH = {context.Month} AND `GlLocationId` IN ({context.LocationIdsCsv}); ");
            query.Append($@"SELECT `GlLocationId`, CourierID AS cour_id, OleCommission FROM {OleCommissionProcessTable} WHERE RateID = 8 AND YEAR = {context.Year} AND MONTH = {context.Month} AND `GlLocationId` IN ({context.LocationIdsCsv}); ");
            query.Append("SELECT 1; ");
            query.Append($@"SELECT Cour_id AS cour_id, City, Commission FROM {CodCommissionTable} WHERE YEAR=@year AND MONTH=@month and city=@city; ");
            query.Append($@"SELECT hcc.cour_id, IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone','Other Zone')) AS Zone,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN (17,9,40,41) GROUP BY hcc.cour_id, Zone,Service ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 1001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN ('21','65','68','71') AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 2001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN ('22','66','69','72') AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 5001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN ('23','52','67','70','73') AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 10001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = '24' AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 25001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN ('25','53') AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN ('10', '20', '61') AND hcc.vendor_id = 0 GROUP BY hcc.cour_id, Zone, Service ; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 5001, 'KG', 'ADDKG') AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN (2,33,42,55,56,57,58,59,60) AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone,Service;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 10001, 'KG', 'ADDKG') AS Service,hcc.Overland_Zone zone,SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(3, 8) GROUP BY hcc.cour_id, Zone, Service ; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 251, '250GM', '500GM') AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local','Outstation') AS Zone,SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = '26' GROUP BY hcc.cour_id, Zone, Service ; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,SUM(hcc.Gross_Amount) amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = '4' GROUP BY hcc.cour_id; ");
            query.Append($@"SELECT hcc.cour_id,IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service,hcc.cn_number as cn_numbers,hcc.Gross_Amount AS amount,hcc.InsuranceCommission AS Ins_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN (6,7,34,47); ");
            query.Append($@"SELECT `GlLocationId`, CourierID AS cour_id,OleCommission FROM {OleCommissionProcessTable} WHERE RateID in (11) AND YEAR = {context.Year} AND MONTH = {context.Month} AND `GlLocationId` IN ({context.LocationIdsCsv});");
            query.Append($@"SELECT `GlLocationId`, CourierID AS cour_id,OleCommission FROM {OleCommissionProcessTable} WHERE RateID in (12) AND YEAR = {context.Year} AND MONTH = {context.Month} AND `GlLocationId` IN ({context.LocationIdsCsv});");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'MOFA OFFICE TO OFFICE' AS Service,IF(hcc.Station_id = hcc.dest_City_id,'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone','Other Zone')) AS Zone,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 27 AND hcc.vendor_id = 0 GROUP BY hcc.cour_id, Zone ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'MOFA OFFICE TO DOORSTEP' AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone','Other Zone')) AS Zone,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com,SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 28 AND hcc.vendor_id = 0 GROUP BY hcc.cour_id, Zone ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 15001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 32 AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT hcc.cour_id,IF(hcc.Weight < 5001, 'KG', 'ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id,'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) amount, SUM(hcc.InsuranceCommission) AS Ins_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 99 GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,(CASE WHEN(hcc.Shipment_id IN(20, 17, 18, 19, 9, 40, 41) AND(hcc.weight / 1000) <= 2) THEN 79 WHEN(hcc.Shipment_id IN(20, 17, 18, 19, 9, 40, 41) AND(hcc.weight / 1000) > 2) THEN 80 ELSE 0 END) AS RateID,SUM(hcc.Gross_Amount) amount,SUM(hcc.InsuranceCommission) AS Ins_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(20,17,18,19,9,40,41) GROUP BY hcc.cour_id, RateID; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id, 81 AS RateID,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN (23,24,25,32) GROUP BY hcc.cour_id, RateID ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id, 82 AS RateID,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN (2,33,39,42,43) GROUP BY hcc.cour_id, RateID ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id, 83 AS RateID,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN (3,8,38) GROUP BY hcc.cour_id, RateID ;");
        }

        private static void AppendCommissionSourceQueriesExtension(StringBuilder query, CommissionProcessContext context)
        {
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'ALL IN ONE' AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone','Other Zone')) AS Zone,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com,SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 48 GROUP BY hcc.cour_id, Zone ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'DOCUMENT CARE' AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone','Other Zone')) AS Zone,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com,SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 49 GROUP BY hcc.cour_id, Zone ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'MTD' AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone','Other Zone')) AS Zone,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.MTDCommission) AS MTD_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate GROUP BY hcc.cour_id, Zone ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'VAS' AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone','Other Zone')) AS Zone,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.VASCommission) AS Vas_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate GROUP BY hcc.cour_id, Zone;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service,hcc.cn_number AS cn_numbers,hcc.Gross_Amount AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com,SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(7,31,34,62) GROUP BY hcc.cour_id;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service,hcc.cn_number AS cn_numbers,hcc.Gross_Amount AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com,SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(47) GROUP BY hcc.cour_id;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service,hcc.cn_number AS cn_numbers,hcc.Gross_Amount AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com,SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(6,30,63,64) GROUP BY hcc.cour_id;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id, IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(1, 9, 17) AND hcc.weight_type = 'Dox' AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone, Service ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id, IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(1, 9, 17) AND hcc.weight_type = 'Non Dox' AND hcc.Billing_Type = 'Cash' GROUP BY hcc.cour_id, Zone, Service ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id, IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(1, 9, 17) AND hcc.weight_type = 'Dox' AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone, Service ; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id, IF(hcc.Weight < 501, '500GM', IF(hcc.Weight > 500 AND hcc.Weight < 1001, '1KG', 'ADDKG')) AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN(1, 9, 17) AND hcc.weight_type = 'Non Dox' AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone, Service ; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 5001, 'KG', 'ADDKG') AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN (2,33,42,55,56,57,58,59,60) AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone,Service;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 15001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 21 AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 15001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 22 AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 15001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN ('23','52') AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 15001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 24 AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 15001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 32 AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 15001, 'KG','ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id IN ('25','53') AND hcc.Billing_Type = 'Retail COD' GROUP BY hcc.cour_id, Zone,Service; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,IF(hcc.Weight < 5001, 'KG', 'ADDKG') AS Service,IF(hcc.Station_id = hcc.dest_City_id,'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = {context.StationId} AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.Shipment_id = 999 GROUP BY hcc.cour_id, Zone,Service;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'ATTESTATION' AS Service,IF(hcc.Station_id = hcc.dest_City_id,'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone','Other Zone')) AS Zone,SUM(hcc.Gross_Amount) AS amount,SUM(hcc.InsuranceCommission) AS Ins_Com, SUM(hcc.BaseCommission) AS Base_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id=@stationid AND hcc.billing_date BETWEEN @todate AND @fromdate AND hcc.vendor_id <> 0 GROUP BY hcc.cour_id, Zone ;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'Insurance' AS Service, IF(hcc.Station_id = hcc.dest_City_id, 'Local', IF(hcc.Overnight_Zone = @Overnight_Zone, 'Same Zone', 'Other Zone')) AS Zone, SUM(hcc.Gross_Amount) AS amount, SUM(hcc.InsuranceCommission) AS Ins_Com FROM {CashConsignmentsTable} hcc WHERE hcc.Station_id = @stationid AND hcc.billing_date BETWEEN @todate AND @fromdate GROUP BY hcc.cour_id, Zone;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,hcc.Shipment_type AS Service,hcc.IncentiveRate AS Rate,COUNT(hcc.cour_id) AS Total_Entries,SUM(hcc.Final_Incentive) AS Base_Com FROM {VasIncentiveDetailTable} hcc WHERE hcc.Station_id = {context.StationId} AND hcc.Delivery_Date BETWEEN @todate AND @fromdate AND hcc.RateID = 97 GROUP BY hcc.cour_id, Service, Rate;");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,hcc.Shipment_type AS Service,hcc.IncentiveRate AS Rate,COUNT(hcc.cour_id) AS Total_Entries,SUM(hcc.Final_Incentive) AS Base_Com FROM {VasIncentiveDetailTable} hcc WHERE hcc.Station_id = {context.StationId} AND hcc.Delivery_Date BETWEEN @todate AND @fromdate AND hcc.RateID = 98 GROUP BY hcc.cour_id, Service, Rate; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,hcc.Shipment_type AS Service,hcc.IncentiveRate AS Rate,COUNT(hcc.cour_id) AS Total_Entries,SUM(hcc.Final_Incentive) AS Base_Com FROM {VasIncentiveDetailTable} hcc WHERE hcc.Station_id = {context.StationId} AND hcc.Delivery_Date BETWEEN @todate AND @fromdate AND hcc.RateID = 96 GROUP BY hcc.cour_id, Service, Rate; ");
            query.Append($@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,hcc.Shipment_type AS Service,hcc.IncentiveRate AS Rate,COUNT(hcc.cour_id) AS Total_Entries,SUM(hcc.Final_Incentive) AS Base_Com FROM {VasIncentiveDetailTable} hcc WHERE hcc.Station_id = {context.StationId} AND hcc.Delivery_Date BETWEEN @todate AND @fromdate AND hcc.RateID = 99 GROUP BY hcc.cour_id, Service, Rate; ");
            query.Append($@"SELECT `GlLocationId`, CourierID AS cour_id,OleCommission FROM {CodReturnCommissionProcessTable} WHERE RateID IN (100,101,102,103) AND YEAR = {context.Year} AND MONTH = {context.Month} AND `GlLocationId` IN ({context.LocationIdsCsv}); ");
            query.Append(@"SELECT LPAD(hcc.cour_id, 5, '0') AS cour_id,'Pickup_Leopard' AS Service,SUM(hcc.Pickup_Leopard) AS Base_Com FROM lcs_hr.asif_data_sept_oct hcc WHERE hcc.origin_city_id = @stationid AND hcc.RC_date BETWEEN @todate AND @fromdate GROUP BY hcc.cour_id;");
            query.Append($@"SELECT a.Cour_id AS cour_id,'COD_Bonus' AS Service,a.CODBonus AS Base_Com FROM {CodCommissionTable} a WHERE a.year = @year AND a.month = @month and city=@city;");
            query.Append($@"SELECT a.Cour_id AS cour_id,'COD_Deduction' AS Service,a.CODDeduction AS Base_Com FROM {CodCommissionTable} a WHERE a.year = @year AND a.month = @month and city=@city ;");
        }

        private static async Task<CommissionProcessBaseline> CaptureCommissionPreviewBaselineAsync(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode,
            CommissionConfig cfg)
        {
            return await connection.QuerySingleAsync<CommissionProcessBaseline>(
                $@"SELECT
                      (
                          SELECT COUNT(*)
                          FROM {CommissionProcessTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND citycode = @CityCode
                      ) AS CommissionRowCount,
                      (
                          SELECT COUNT(*)
                          FROM {EmpCommAdjustmentTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND City_Code = @CityCode
                            AND Adjusment_Type_id NOT IN ({cfg.AdjustmentPreserveTypeIdsCsv})
                      ) AS AdjustmentRowCount,
                      (
                          SELECT COALESCE(SUM(Amount), 0)
                          FROM {EmpCommAdjustmentTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND City_Code = @CityCode
                            AND Adjusment_Type_id NOT IN ({cfg.AdjustmentPreserveTypeIdsCsv})
                      ) AS AdjustmentAmountTotal",
                new { Year = year, Month = month, CityCode = cityCode });
        }

        private static async Task<bool> VerifyCommissionPreviewBaselineAsync(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode,
            CommissionProcessBaseline baseline,
            CommissionConfig cfg)
        {
            var current = await CaptureCommissionPreviewBaselineAsync(connection, year, month, cityCode, cfg);
            return baseline.CommissionRowCount == current.CommissionRowCount
                && baseline.AdjustmentRowCount == current.AdjustmentRowCount
                && baseline.AdjustmentAmountTotal == current.AdjustmentAmountTotal;
        }

        private static (DateTime fromDate, DateTime toDate) GetCommissionPeriod(int year, int month, CommissionConfig cfg)
        {
            var startDate = new DateTime(year, month, cfg.CommissionStartDay).AddMonths(-1);
            var endDate = new DateTime(year, month, cfg.CommissionEndDay);
            return (startDate, endDate);
        }

        private static string GetRowString(DataRow row, string columnName)
        {
            return !row.Table.Columns.Contains(columnName) || row[columnName] == DBNull.Value
                ? string.Empty
                : Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static decimal GetRowDecimal(DataRow row, string columnName)
        {
            if (!row.Table.Columns.Contains(columnName) || row[columnName] == DBNull.Value)
            {
                return 0m;
            }

            return Convert.ToDecimal(row[columnName], CultureInfo.InvariantCulture);
        }

        private sealed class CommissionProcessContext
        {
            public int Year { get; set; }
            public int Month { get; set; }
            public string CityCode { get; set; } = string.Empty;
            public DateTime SalaryDate { get; set; }
            public string StationId { get; set; } = string.Empty;
            public string LocationIdsCsv { get; set; } = string.Empty;
            public string OverlandZone { get; set; } = string.Empty;
            public string OvernightZone { get; set; } = string.Empty;
            public DataTable EmployeeRoutes { get; set; } = new();
            public CommissionConfig Cfg { get; set; } = new CommissionConfig();
        }

        private sealed class CommissionProcessBaseline
        {
            public int CommissionRowCount { get; set; }
            public int AdjustmentRowCount { get; set; }
            public decimal AdjustmentAmountTotal { get; set; }
        }

        private sealed class OLECommission
        {
            public int GlLocationId { get; set; }
            public decimal OleCommission { get; set; }
            public int RateID { get; set; }
            public string? CourierID { get; set; }
            public string? Emp_No { get; set; }
        }

        private sealed class EmpCommAdjDtl
        {
            public int Year { get; set; }
            public int Month { get; set; }
            public string? Emp_No { get; set; }
            public decimal Amount { get; set; }
            public string CreatedBy { get; set; } = string.Empty;
            public string City_Code { get; set; } = string.Empty;
            public string? Comment { get; set; }
            public int Adjusment_Type_id { get; set; }
        }

        private sealed class OLEEmpList
        {
            public int LocationId { get; set; }
            public string? Emp_no { get; set; }
            public int WD { get; set; }
            public bool IsEligible { get; set; }
            public string? Cour_id { get; set; }
            public int CodeType { get; set; }
            public int CommissionId { get; set; }
            public decimal OLEDispatchProperAmount { get; set; }
            public decimal OLETransitDispatchAmount { get; set; }
            public decimal OLEDeliveryOPSAmount { get; set; }
            public decimal DOM_CREDIT { get; set; }
            public decimal LOCAL_CREDIT { get; set; }
            public decimal LOCAL_DLD { get; set; }
            public decimal PMCL { get; set; }
            public decimal DomesticDlivery { get; set; }
            public decimal INTL_CREDIT { get; set; }
            public decimal Porter { get; set; }
            public decimal COD { get; set; }
            public decimal OVERNIGHT { get; set; }
            public decimal YB1KG { get; set; }
            public decimal YB2KG { get; set; }
            public decimal YB5KG { get; set; }
            public decimal YB10KG { get; set; }
            public decimal YB15KG { get; set; }
            public decimal YB25KG { get; set; }
            public decimal FLAYER { get; set; }
            public decimal DETAIN { get; set; }
            public decimal OVERLAND { get; set; }
            public decimal PREPAID { get; set; }
            public decimal LOVELINE { get; set; }
            public decimal INTL_CASH { get; set; }
            public decimal MOFA_OTO { get; set; }
            public decimal MOFA_OTD { get; set; }
            public decimal RMS_COD { get; set; }
            public decimal OLE_Credit_Booking { get; set; }
            public decimal OLE_Delivery { get; set; }
            public decimal CEB_Upto_2KG { get; set; }
            public decimal CEB_Above_2KG { get; set; }
            public decimal ECON_Credit_Booking { get; set; }
            public decimal OLE_CORP_Booking { get; set; }
            public decimal CASH_EXP_BKG_UpTo_2Kg { get; set; }
            public decimal CASH_EXP_BKG_Above_2Kg { get; set; }
            public decimal CASH_Leop_BOX_Above_2Kg { get; set; }
            public decimal CASH_Economy_Booking { get; set; }
            public decimal CASH_OLE_Booking { get; set; }
            public decimal RetailDeduction { get; set; }
            public decimal Insurance_Com { get; set; }
            public decimal CEB_Upto_2KG_Exis { get; set; }
            public decimal CEB_Upto_2KG_New { get; set; }
            public decimal CEB_Above_2Kg_Exis { get; set; }
            public decimal CEB_Above_2Kg_New { get; set; }
            public decimal ECON_Credit_Booking_Exis { get; set; }
            public decimal ECON_Credit_Booking_New { get; set; }
            public decimal OLE_CORP_Booking_Exis { get; set; }
            public decimal OLE_CORP_Booking_New { get; set; }
            public decimal Project_Local_Exis { get; set; }
            public decimal Project_Local_New { get; set; }
            public decimal Project_Domestic_Exis { get; set; }
            public decimal Project_Domestic_New { get; set; }
            public decimal AllInOne { get; set; }
            public decimal DocumnetCare { get; set; }
            public decimal MTD { get; set; }
            public decimal VAS { get; set; }
            public decimal IntlDox { get; set; }
            public decimal IntlEconomy { get; set; }
            public decimal IntlParcel { get; set; }
            public decimal ONUpto1kg { get; set; }
            public decimal ONAbove1kg { get; set; }
            public decimal ONUpto1kgRetailCOD { get; set; }
            public decimal ONAbove1kgRetailCOD { get; set; }
            public decimal EconomyRetail { get; set; }
            public decimal YB1KGRetail { get; set; }
            public decimal YB2KGRetail { get; set; }
            public decimal YB5KGRetail { get; set; }
            public decimal YB10KGRetail { get; set; }
            public decimal YB15KGRetail { get; set; }
            public decimal YB25KGRetail { get; set; }
            public decimal MyCollect { get; set; }
            public decimal Attestation { get; set; }
            public decimal Credit_Debit_Card { get; set; }
            public decimal ECommerce_Zero_COD { get; set; }
            public decimal Passport { get; set; }
            public decimal CNIC_Card { get; set; }
            public decimal Return_E_Com { get; set; }
            public decimal Pickup_Leopard { get; set; }
            public decimal COD_Bonus { get; set; }
            public decimal COD_Deduction { get; set; }
            public decimal SOA { get; set; }
            public decimal Utility_Bill { get; set; }
            public decimal General_Light_Delivery { get; set; }
            public decimal General_Heavy_Delivery { get; set; }
            public decimal MTD_Delivery { get; set; }
            public decimal Giftwifts_Delivery { get; set; }
            public decimal Ecom_overall_SR_Bonus { get; set; }
        }

        private sealed class OleVasFinalRow
        {
            public string? Emp_No { get; set; }
            public string? SourceCour_id { get; set; }
            public string? Cour_id { get; set; }
            public int RateID { get; set; }
            public decimal Amount { get; set; }
        }

        private sealed class SrBonusFinalRow
        {
            public string? Emp_No { get; set; }
            public decimal BonusAmount { get; set; }
        }
    }
}
