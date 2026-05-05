using System.Data;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    /// <summary>
    /// Single-employee commission correction / reprocessing.
    ///
    /// Mirrors the behaviour of the old project's EmployeeCommissionProcess.aspx.cs:
    ///   1. Resolves the employee's city code from hr_employeelocationdetails
    ///   2. Loads city-wide context (rates, zone, location IDs) — reuses LoadCommissionProcessContextAsync
    ///   3. Loads ALL employees in the city (needed for correct proportional OLE 2/3/4 distribution)
    ///   4. Distributes OLE commissions proportionally across all employees
    ///   5. Merges cash commission data for the target employee
    ///   6. Filters the result list to ONLY the target employee before any write
    ///   7. DELETE commission-process rows WHERE emp_no = @EmpNo AND month = @Month AND year = @Year
    ///   8. INSERT only the target employee's rows
    ///   9. DELETE employee adjustment rows for this employee (scoped — does NOT wipe other employees)
    ///  10. INSERT adjustment rows for the target employee
    ///  11. INSERT acknowledgment row (type = 3) — same as old EmployeeCommissionProcess
    ///  12. Commit
    ///
    /// Key difference from old code: DELETE and INSERT are scoped to emp_no only,
    /// so other employees' commission rows are never touched.
    /// </summary>
    public partial class PayrollService
    {
        public async Task<SingleEmployeeCommissionResult> ProcessSingleEmployeeCommissionAsync(
            string empNo,
            int year,
            int month,
            string currentUserId)
        {
            empNo = empNo.Trim();

            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot create main database connection.");
            await connection.OpenAsync();

            // 1. Resolve the employee's city code
            var cityCode = await connection.ExecuteScalarAsync<string>(
                @"SELECT hc.Code
                  FROM hr_city hc
                  INNER JOIN hr_employeelocationdetails eld
                    ON hc.station_id = eld.LocationId
                   AND eld.ToDate IS NULL
                  WHERE eld.Emp_No = @EmpNo
                  LIMIT 1",
                new { EmpNo = empNo });

            if (string.IsNullOrEmpty(cityCode))
                throw new ArgumentException($"Employee '{empNo}' not found or no city is assigned in hr_employeelocationdetails.");

            // 2. Load city context (rates, zone, station ID, location IDs, all route codes for city)
            //    This also validates the logged-in user has access to this city.
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var singleEmpCfg = await CommissionConfig.LoadAsync(connection, transaction);

                var context = await LoadCommissionProcessContextAsync(
                    connection, transaction, year, month, cityCode, currentUserId, singleEmpCfg);
                context.Cfg = singleEmpCfg;
                var fromDate = new DateTime(year, month, singleEmpCfg.CommissionStartDay).AddMonths(-1);
                var toDate   = new DateTime(year, month, singleEmpCfg.CommissionEndDay);

                // 3. Load ALL employees in the city
                //    This is required so that the OLE 2/3/4 proportional distribution uses
                //    the correct denominator (total working days across all employees).
                int days = DateTime.DaysInMonth(year, month);
                var allEmployees = await LoadCommissionEmployeePopulationAsync(
                    connection, transaction, year, month, days, context.SalaryDate, context.LocationIdsCsv, context.Cfg);

                // 4. Load OLE data and distribute (same logic as ProcessOleRecordsAsync,
                //    but OLE_Credit_Booking / OLE_Delivery / CorporateRBI are scoped
                //    to this employee's route codes via the join on hr_employeeroutecode)
                await AppendOleDataForSingleEmployeeAsync(
                    connection, transaction, year, month, empNo, context, allEmployees);

                // 5. Aggregate all OLE components by (emp_no, cour_id)
                allEmployees = AggregateEmployeeCommission(allEmployees);

                // 6. Build cash rows for the city (read from the cash commission output table,
                //    which was already populated by the Cash Commission step).
                //    MergeCashRowsIntoEmployees matches by emp_no — after filtering to
                //    the target employee, only their cash data will be present.
                var cashRows = BuildCommissionProcessRows(
                    connection, year, month, cityCode, currentUserId, context,
                    new CommissionProcessPreviewResult { Year = year, Month = month, CityCode = cityCode });
                MergeCashRowsIntoEmployees(allEmployees, cashRows);

                // 7. Filter to ONLY the target employee before any write operation
                var targetRows = allEmployees
                    .Where(e => string.Equals(e.Emp_no, empNo, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (targetRows.Count == 0)
                    throw new ArgumentException(
                        $"No commission data found for employee '{empNo}' in {cityCode} for {month}/{year}. "
                        + "Ensure Cash Commission, COD Commission, and OverLand Commission steps have been run first.");

                // 8. Delete ONLY this employee's existing commission-process rows
                await connection.ExecuteAsync(
                    $@"DELETE FROM {CommissionProcessTable}
                      WHERE month = @Month AND year = @Year AND emp_no = @EmpNo",
                    new { Month = month, Year = year, EmpNo = empNo },
                    transaction,
                    commandTimeout: 60);

                // 9. Insert only the target employee's rows
                await InsertCommissionRowsAsync(
                    connection, transaction, targetRows, year, month, cityCode, currentUserId);

                // 10. Rebuild adjustments for target employee only
                //     Delete scoped to this emp_no — does NOT wipe other employees' adjustments
                var targetAdjustments = BuildCommissionAdjustments(
                    targetRows, year, month, cityCode, currentUserId, singleEmpCfg);

                await connection.ExecuteAsync(
                    $@"DELETE FROM `lcs_hr`.`{AcTestTableNames.T_EmpCommAdjDtl}`
                      WHERE `Year`      = @Year
                        AND `Month`     = @Month
                        AND `City_Code` = @CityCode
                        AND `Emp_No`    = @EmpNo
                        AND Adjusment_Type_id NOT IN ({singleEmpCfg.AdjustmentPreserveTypeIdsCsv})",
                    new { Year = year, Month = month, CityCode = cityCode, EmpNo = empNo },
                    transaction,
                    commandTimeout: 30);

                if (targetAdjustments.Count > 0)
                {
                    await connection.ExecuteAsync(
                        $@"INSERT INTO {EmpCommAdjustmentTable}
                          (`Emp_No`, `Amount`, `Year`, `Month`, `City_Code`, `CreatedBy`, `Adjusment_Type_id`)
                          VALUES
                          (@Emp_No, @Amount, @Year, @Month, @City_Code, @CreatedBy, 3)",
                        targetAdjustments,
                        transaction,
                        commandTimeout: 30);
                }

                // 11. Acknowledgment insert — type = 3 matches old EmployeeCommissionProcess
                await connection.ExecuteAsync(
                    $@"INSERT INTO {AcknowledgmentTable}
                      VALUES (3, @UserId, NOW(), 1, 1, 1, NULL)",
                    new { UserId = currentUserId },
                    transaction,
                    commandTimeout: 30);

                await transaction.CommitAsync();

                return new SingleEmployeeCommissionResult
                {
                    Success            = true,
                    Message            = $"{targetRows.Count} commission row(s) reprocessed successfully for employee {empNo}.",
                    EmpNo              = empNo,
                    CityCode           = cityCode,
                    RowsInserted       = targetRows.Count,
                    AdjustmentsInserted = targetAdjustments.Count
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ── Private: load OLE components, keeping full population for proportional distribution ──

        private async Task AppendOleDataForSingleEmployeeAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            string empNo,
            CommissionProcessContext context,
            List<OLEEmpList> allEmployees)
        {
            // RateID 2,3,4 — location-pool amounts (no emp filter; proportional distribution
            // across ALL employees in allEmployees is intentional and correct)
            var oleDispatchProper = (await connection.QueryAsync<OLECommission>(
                $@"SELECT GlLocationId, SUM(OleCommission) AS OleCommission
                   FROM {OleCommissionProcessTable}
                   WHERE YEAR = @Year AND MONTH = @Month AND RateId = 2
                     AND GlLocationId IN ({context.LocationIdsCsv})
                   GROUP BY GlLocationId",
                new { Year = year, Month = month },
                transaction, commandTimeout: 120)).ToList();

            var oleTransitDispatch = (await connection.QueryAsync<OLECommission>(
                $@"SELECT GlLocationId, SUM(OleCommission) AS OleCommission
                   FROM {OleCommissionProcessTable}
                   WHERE YEAR = @Year AND MONTH = @Month AND RateId = 3
                     AND GlLocationId IN ({context.LocationIdsCsv})
                   GROUP BY GlLocationId",
                new { Year = year, Month = month },
                transaction, commandTimeout: 120)).ToList();

            var oleDeliveryOps = (await connection.QueryAsync<OLECommission>(
                $@"SELECT GlLocationId, SUM(OleCommission) AS OleCommission
                   FROM {OleCommissionProcessTable}
                   WHERE YEAR = @Year AND MONTH = @Month AND RateId = 4
                     AND GlLocationId IN ({context.LocationIdsCsv})
                   GROUP BY GlLocationId",
                new { Year = year, Month = month },
                transaction, commandTimeout: 120)).ToList();

            // RateID 1 — OLE_Credit_Booking: scoped to this employee's routes
            var oleCreditBooking = (await connection.QueryAsync<OLECommission>(
                $@"SELECT RateID, cp.CourierID, rt.Emp_No, cp.GlLocationId, cp.OleCommission
                   FROM {OleCommissionProcessTable} cp
                   INNER JOIN hr_employeeroutecode rt
                     ON rt.RouteCode = cp.CourierID
                    AND rt.ToDate IS NULL
                    AND cp.GlLocationId = rt.LocationId
                   WHERE IFNULL(rt.CodeType, 0) IN (9)
                     AND YEAR = @Year AND MONTH = @Month AND RateId = 1
                     AND GlLocationId IN ({context.LocationIdsCsv})
                     AND rt.Emp_No = @EmpNo",
                new { Year = year, Month = month, EmpNo = empNo },
                transaction, commandTimeout: 120)).ToList();

            // RateID 5 — OLE_Delivery: scoped to this employee's routes
            var oleDelivery = (await connection.QueryAsync<OLECommission>(
                $@"SELECT RateID, cp.CourierID, rt.Emp_No, cp.GlLocationId, cp.OleCommission
                   FROM {OleCommissionProcessTable} cp
                   INNER JOIN hr_employeeroutecode rt
                     ON rt.RouteCode = cp.CourierID
                    AND rt.ToDate IS NULL
                    AND cp.GlLocationId = rt.LocationId
                   WHERE IFNULL(rt.CodeType, 0) NOT IN (3, 10, 11)
                     AND YEAR = @Year AND MONTH = @Month AND RateId = 5
                     AND GlLocationId IN ({context.LocationIdsCsv})
                     AND rt.Emp_No = @EmpNo",
                new { Year = year, Month = month, EmpNo = empNo },
                transaction, commandTimeout: 120)).ToList();

            // RateID 8, 84–95 — CorporateRBI: scoped to this employee's routes
            var corporateRbi = (await connection.QueryAsync<OLECommission>(
                $@"SELECT DISTINCT RateID, cp.CourierID, rt.Emp_No, cp.GlLocationId, cp.OleCommission
                   FROM {OleCommissionProcessTable} cp
                   INNER JOIN hr_employeeroutecode rt
                     ON rt.RouteCode = cp.CourierID
                    AND rt.ToDate IS NULL
                    AND cp.GlLocationId = rt.LocationId
                   WHERE IFNULL(rt.CodeType, 0) NOT IN (3, 10, 11)
                     AND YEAR = @Year AND MONTH = @Month
                     AND RateId IN (8, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95)
                     AND GlLocationId IN ({context.LocationIdsCsv})
                     AND rt.Emp_No = @EmpNo",
                new { Year = year, Month = month, EmpNo = empNo },
                transaction, commandTimeout: 120)).ToList();

            // Append per-employee OLE entries (RateID 1 and 5)
            foreach (var item in oleCreditBooking)
                allEmployees.Add(new OLEEmpList
                {
                    Emp_no           = item.Emp_No,
                    Cour_id          = item.CourierID,
                    OLE_Credit_Booking = item.OleCommission
                });

            foreach (var item in oleDelivery)
                allEmployees.Add(new OLEEmpList
                {
                    Emp_no      = item.Emp_No,
                    Cour_id     = item.CourierID,
                    OLE_Delivery = item.OleCommission
                });

            // Proportional distribution of location-pool commissions (RateID 2, 3, 4)
            // All employees are included so the denominator (total WD) is correct.
            DistributeOleLocationCommission(allEmployees, oleDispatchProper,  2,
                (emp, amount) => emp.OLEDispatchProperAmount  = amount);
            DistributeOleLocationCommission(allEmployees, oleTransitDispatch, 3,
                (emp, amount) => emp.OLETransitDispatchAmount = amount);
            DistributeOleLocationCommission(allEmployees, oleDeliveryOps,     4,
                (emp, amount) => emp.OLEDeliveryOPSAmount     = amount);

            // Append CorporateRBI rows for this employee
            AppendCorporateRbiRows(allEmployees, corporateRbi, context.Cfg);
        }
    }
}
