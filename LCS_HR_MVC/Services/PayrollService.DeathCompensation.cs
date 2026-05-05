using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        private int _deathCompensationLoanSequence;

        public async Task<DeathCompensationViewModel> GetDeathCompensationPageAsync(
            DateTime workingDate,
            string currentUserId,
            string? defaultLocationId,
            DeathCompensationViewModel? existingModel = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();

            var model = existingModel ?? new DeathCompensationViewModel();
            if (model.Year <= 0)
            {
                model.Year = workingDate.Year;
            }

            if (model.Month <= 0)
            {
                model.Month = Math.Min(workingDate.Month, 12);
            }

            model.Years = BuildYearSelectList(workingDate);
            model.Months = BuildMonthSelectList(workingDate, model.Year);
            model.Zones = await BuildUserZoneSelectItemsAsync(connection, currentUserId, "All Zones", "00");
            model.Cities = await BuildUserCitySelectItemsAsync(connection, currentUserId, model.ZoneId, "All Cities", "0", includeAllCity: false);

            if (string.IsNullOrWhiteSpace(model.CityCode) && !string.IsNullOrWhiteSpace(defaultLocationId))
            {
                model.CityCode = defaultLocationId.Trim();
            }

            return model;
        }

        public async Task<DeathCompensationProcessResult> ProcessDeathCompensationAsync(
            DeathCompensationViewModel model,
            string currentUserId,
            string? defaultLocationId)
        {
            var result = new DeathCompensationProcessResult();

            if (model.Year <= 0 || model.Month <= 0)
            {
                result.Message = "Year and month are required.";
                return result;
            }

            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                result.Message = "Database error";
                return result;
            }

            await connection.OpenAsync();

            string effectiveLocationCityCode;
            try
            {
                effectiveLocationCityCode = await ResolveDeathCompensationContextCityCodeAsync(
                    connection,
                    currentUserId,
                    model.CityCode,
                    defaultLocationId);
            }
            catch (ArgumentException ex)
            {
                result.Message = ex.Message;
                return result;
            }

            IReadOnlyList<DeathCompensationCandidate> candidates = await LoadDeathCompensationCandidatesAsync(
                connection,
                model,
                currentUserId);

            if (candidates.Count == 0)
            {
                result.Success = true;
                result.Message = "0 Pay Slip(s) generated.";
                return result;
            }

            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                StateHelper.userid = currentUserId;
                DAL.ConnectionString = _connectionFactory.ConnectionString;

                DateTime salaryDate = new DateTime(model.Year, model.Month, DateTime.DaysInMonth(model.Year, model.Month));
                int createdStation = await GetSalaryProcessStationAsync(connection, transaction as MySqlTransaction, effectiveLocationCityCode);
                int createdLocation = await GetSalaryProcessLocationAsync(connection, transaction as MySqlTransaction, effectiveLocationCityCode);
                _deathCompensationLoanSequence = await GetNextLoanDeductionSequenceAsync(connection, transaction as MySqlTransaction);

                foreach (var candidate in candidates)
                {
                    await DeleteDeathCompensationRecordsAsync(connection, transaction as MySqlTransaction, candidate.EmpNo, model.Year, model.Month);

                    DataSet dataSet = await PrepareDeathCompensationDataSetAsync(
                        connection,
                        transaction as MySqlTransaction,
                        candidate,
                        model,
                        currentUserId,
                        salaryDate,
                        createdStation,
                        createdLocation);

                    var persistResult = await MasterDetailEntryAsync(dataSet, connection, transaction as MySqlTransaction);
                    result.PayslipCount += persistResult.HeaderRowsInserted;
                }

                IReadOnlyList<string> employeeIds = candidates.Select(static candidate => candidate.EmpNo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var voucherStatusResult = await UpdateDeathCompensationVoucherStatusesAsync(
                    connection,
                    transaction as MySqlTransaction,
                    model.Year,
                    model.Month,
                    employeeIds,
                    currentUserId);

                result.VoucherStatusInserted = voucherStatusResult.Inserted;
                result.VoucherStatusUpdated = voucherStatusResult.Updated;
                result.ProcessedEmployees = (await LoadDeathCompensationProcessedEmployeesAsync(
                    connection,
                    transaction as MySqlTransaction,
                    model.Year,
                    model.Month,
                    employeeIds)).ToList();

                await transaction.CommitAsync();
                result.Success = true;
                result.Message = $"{result.PayslipCount} Pay Slip(s) generated.";
                return result;
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();
                result.Message = ex.Message;
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static List<SelectListItem> BuildMonthSelectList(DateTime workingDate, int selectedYear)
        {
            int maxMonth = selectedYear >= workingDate.Year ? workingDate.Month : 12;
            if (maxMonth <= 0)
            {
                maxMonth = 12;
            }

            return Enumerable.Range(1, maxMonth)
                .Select(month => new SelectListItem
                {
                    Value = month.ToString(CultureInfo.InvariantCulture),
                    Text = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
                })
                .ToList();
        }

        private async Task<DataSet> PrepareDeathCompensationDataSetAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            DeathCompensationCandidate candidate,
            DeathCompensationViewModel model,
            string currentUserId,
            DateTime salaryDate,
            int createdStation,
            int createdLocation)
        {
            decimal taxDeductOn;
            string paymentMode;
            decimal basicSalary = LCS.GetCurrentSalary(connection, candidate.EmpNo, salaryDate, out taxDeductOn, out paymentMode);
            decimal currentSalary = basicSalary;
            decimal grossPay = basicSalary;
            decimal amountCash = basicSalary;

            var loanResult = await ProcessDeathCompensationLoanAsync(
                connection,
                transaction,
                candidate.EmpNo,
                salaryDate,
                model.Year,
                model.Month,
                currentUserId);

            decimal advanceSalary = await GetDeathCompensationAdvanceSalaryAsync(
                connection,
                transaction,
                candidate.EmpNo,
                model.Year,
                model.Month);

            decimal totalDeductions = loanResult.Loan + advanceSalary;
            string currentFlag = paymentMode switch
            {
                "C" => "Cash",
                "Q" => "Cheuqe",
                _ => "Bank"
            };

            string type = paymentMode;
            if (string.Equals(paymentMode, "Q", StringComparison.OrdinalIgnoreCase))
            {
                type = "B";
            }

            DataSet dataSet = LCS.GetDataTableSchema(connection, "hr_salaryprocessed_hdr", "hr_salaryprocessed_dtl");
            DataRow row = dataSet.Tables["hr_salaryprocessed_hdr"].NewRow();

            row["SalaryYear"] = model.Year;
            row["SalaryMonth"] = model.Month;
            row["Emp_No"] = candidate.EmpNo;
            row["Dept"] = candidate.DeptCode;
            row["SalaryProcessedDate"] = DateTime.Now;
            row["BasicSalary"] = basicSalary;
            row["WorkedDays"] = 0;
            row["currentsalary"] = currentSalary;
            row["AbsentDays"] = 0;
            row["RAbsentDays"] = 0;
            row["Absent_amt"] = 0m;
            row["OT_Amount"] = 0m;
            row["PT_Amount"] = 0m;
            row["extra_hours"] = 0m;
            row["extra_hours_amt"] = 0m;
            row["extra_days"] = 0m;
            row["extra_days_amt"] = 0m;
            row["extra_fuel"] = 0m;
            row["extra_fuel_amt"] = 0m;
            row["Extra_amount"] = 0m;
            SetIfColumnExists(row, "Extra_Amount_Taxable", 0m);
            row["Fuel_days"] = 0;
            row["Fuel_pday"] = 0m;
            row["Fuel_Amount"] = 0m;
            SetIfColumnExists(row, "Fuel_Card_Usage", 0m);
            SetIfColumnExists(row, "FuelCard_Qty_Usage", 0m);
            row["CommAmount"] = 0m;
            SetIfColumnExists(row, "CODKPIBonus", 0m);
            SetIfColumnExists(row, "CODKPIDeduction", 0m);
            row["Allowances"] = 0m;
            row["Deductions"] = 0m;
            row["Loan"] = loanResult.Loan;
            row["Loan_balance"] = loanResult.LoanBalance;
            row["Advance"] = advanceSalary;
            row["Tax"] = 0m;
            row["GrossPay"] = grossPay;
            row["Total_Deduction"] = totalDeductions;
            row["NetPay"] = Math.Round(grossPay - totalDeductions);
            row["CashPayment"] = 0m;
            row["amount_bank"] = 0m;
            row["amount_cash"] = amountCash;
            row["Payment_mode"] = currentFlag;
            row["Comments"] = $"Salary of {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(model.Month)} {model.Year}";
            row["CreatedBy"] = currentUserId;
            row["Created_Date"] = DateTime.Now;
            row["UpdatedBy"] = currentUserId;
            row["Updated_Date"] = DateTime.Now;
            row["Type"] = type;
            row["createdStation"] = createdStation;
            row["CreatedLocation"] = createdLocation;
            SetIfColumnExists(row, "SalaryAdExtraFixed", 0m);
            SetIfColumnExists(row, "TotalFixGross", 0m);
            SetIfColumnExists(row, "NewBasic", 0m);
            SetIfColumnExists(row, "FixGross", 0m);

            dataSet.Tables["hr_salaryprocessed_hdr"].Rows.Add(row);
            return dataSet;
        }

        private static void SetIfColumnExists(DataRow row, string columnName, object value)
        {
            if (row.Table.Columns.Contains(columnName))
            {
                row[columnName] = value;
            }
        }

        private async Task<IReadOnlyList<DeathCompensationCandidate>> LoadDeathCompensationCandidatesAsync(
            MySqlConnection connection,
            DeathCompensationViewModel model,
            string currentUserId)
        {
            string query = @"
SELECT *
FROM (
    SELECT
        s.Emp_No AS EmpNo,
        s.Dept AS DeptCode,
        DATE(he.APPOINT_DATE) AS AppDate,
        COUNT(s.Emp_No) AS TotalVouchers
    FROM hr_salaryprocessed_hdr s
    INNER JOIN hr_employeeterminationdetails ter ON ter.Emp_No = s.Emp_No
    INNER JOIN hr_employeepersonaldetail he ON s.Emp_No = he.EMP_NO
    INNER JOIN hr_city c ON c.Code = he.P_CITY_CODE
    WHERE ter.LeavingReason = 'Death'
      AND DATE(CONCAT(s.SalaryYear, '-', s.SalaryMonth, '-01')) >= DATE(CONCAT(YEAR(he.LEFT_DATE), '-', MONTH(he.LEFT_DATE), '-01'))
      AND ter.Settlement = 'N'";

            var parameters = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(model.ZoneId) && !string.Equals(model.ZoneId, "00", StringComparison.OrdinalIgnoreCase))
            {
                query += " AND c.RZoneCode = @ZoneId";
                parameters.Add("@ZoneId", model.ZoneId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(model.CityCode) && !string.Equals(model.CityCode, "0", StringComparison.OrdinalIgnoreCase))
            {
                query += " AND c.Code = @CityCode";
                parameters.Add("@CityCode", model.CityCode.Trim());
            }
            else
            {
                query += " AND c.Code IN (SELECT city_code FROM lcs_user_location WHERE Userid = @UserId)";
                parameters.Add("@UserId", currentUserId);
            }

            query += @"
    GROUP BY he.Emp_No
    ORDER BY s.Emp_No
) xb
WHERE xb.TotalVouchers < 6;";

            return (await connection.QueryAsync<DeathCompensationCandidate>(query, parameters)).ToList();
        }

        private async Task<string> ResolveDeathCompensationContextCityCodeAsync(
            MySqlConnection connection,
            string currentUserId,
            string? selectedCityCode,
            string? defaultLocationId)
        {
            string normalizedSelectedCity = selectedCityCode?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedSelectedCity) && normalizedSelectedCity != "0")
            {
                int selectedCityAllowed = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM lcs_user_location
                      WHERE userid = @UserId
                        AND city_code = @CityCode",
                    new
                    {
                        UserId = currentUserId,
                        CityCode = normalizedSelectedCity
                    });

                if (selectedCityAllowed == 0)
                {
                    throw new ArgumentException("You are not allowed to process death compensation for the selected city.");
                }

                return normalizedSelectedCity;
            }

            string normalizedDefaultLocation = defaultLocationId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedDefaultLocation))
            {
                int defaultCityAllowed = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM lcs_user_location
                      WHERE userid = @UserId
                        AND city_code = @CityCode",
                    new
                    {
                        UserId = currentUserId,
                        CityCode = normalizedDefaultLocation
                    });

                if (defaultCityAllowed > 0)
                {
                    return normalizedDefaultLocation;
                }
            }

            string? fallbackCityCode = await connection.ExecuteScalarAsync<string>(
                @"SELECT ul.city_code
                  FROM lcs_user_location ul
                  INNER JOIN hr_city c ON c.Code = ul.city_code
                  WHERE ul.userid = @UserId
                  ORDER BY c.FullName ASC
                  LIMIT 1",
                new { UserId = currentUserId });

            if (string.IsNullOrWhiteSpace(fallbackCityCode))
            {
                throw new ArgumentException("No authorized city found for the current user.");
            }

            return fallbackCityCode.Trim();
        }

        private async Task DeleteDeathCompensationRecordsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string employeeId,
            int year,
            int month)
        {
            await connection.ExecuteAsync(
                @"DELETE FROM hr_salaryprocessed_dtl
                  WHERE SalaryYear = @Year
                    AND SalaryMonth = @Month
                    AND emp_no = @EmpNo",
                new
                {
                    Year = year,
                    Month = month,
                    EmpNo = employeeId
                },
                transaction);

            await connection.ExecuteAsync(
                @"DELETE FROM hr_salaryprocessed_hdr
                  WHERE SalaryYear = @Year
                    AND SalaryMonth = @Month
                    AND Emp_No = @EmpNo",
                new
                {
                    Year = year,
                    Month = month,
                    EmpNo = employeeId
                },
                transaction);

            await connection.ExecuteAsync(
                @"DELETE FROM hr_employeeloandeduction
                  WHERE emp_no = @EmpNo
                    AND comments = @Comments",
                new
                {
                    EmpNo = employeeId,
                    Comments = GetSalaryLoanComment(year, month)
                },
                transaction);
        }

        private async Task<DeathCompensationLoanResult> ProcessDeathCompensationLoanAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string employeeId,
            DateTime salaryDate,
            int year,
            int month,
            string currentUserId)
        {
            decimal totalLoan = 0m;
            decimal loanBalance = 0m;

            var rows = (await connection.QueryAsync<DeathCompensationLoanRow>(
                @"SELECT
                      a.LD_No,
                      a.Emp_No AS EmpNo,
                      a.loancode AS LoanCode,
                      l.fullname AS LoanName,
                      a.DisbursedAmount,
                      a.DeductionInstallments,
                      (
                          SELECT IFNULL(SUM(b.DeductionAmount), 0)
                          FROM hr_employeeloandeduction b
                          WHERE b.emp_no = a.Emp_No
                            AND b.ld_no = a.LD_No
                          LIMIT 1
                      ) AS Paid
                  FROM hr_employeeloandisbursed a
                  INNER JOIN hr_loantypes l ON a.LoanCode = l.Code
                  WHERE a.emp_no = @EmpNo
                    AND a.DeductionStartDate <= @SalaryDate",
                new
                {
                    EmpNo = employeeId,
                    SalaryDate = salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                },
                transaction)).ToList();

            foreach (var row in rows)
            {
                if (row.DisbursedAmount <= row.Paid || row.DeductionInstallments <= 0)
                {
                    continue;
                }

                decimal installmentAmount = row.DisbursedAmount / row.DeductionInstallments;
                decimal balance = row.DisbursedAmount - row.Paid;
                if (installmentAmount > balance)
                {
                    installmentAmount = balance;
                }

                if (installmentAmount <= 0)
                {
                    continue;
                }

                totalLoan += installmentAmount;
                loanBalance += row.DisbursedAmount - (row.Paid + installmentAmount);

                string loanDeductionCode = _deathCompensationLoanSequence.ToString("0000000000", CultureInfo.InvariantCulture);
                _deathCompensationLoanSequence++;

                await connection.ExecuteAsync(
                    @"INSERT INTO hr_employeeloandeduction
                      VALUES
                      (
                          @LoanDeductionNo,
                          @LoanDisbursedNo,
                          @EmpNo,
                          @LoanCode,
                          @DeductionDate,
                          @DeductionAmount,
                          @Balance,
                          @Comments,
                          @CreatedBy,
                          @CreatedDate,
                          @UpdatedBy,
                          @UpdatedDate
                      )",
                    new
                    {
                        LoanDeductionNo = loanDeductionCode,
                        LoanDisbursedNo = row.LD_No,
                        EmpNo = employeeId,
                        LoanCode = row.LoanCode,
                        DeductionDate = DateTime.Now,
                        DeductionAmount = installmentAmount,
                        Balance = row.DisbursedAmount - (row.Paid + installmentAmount),
                        Comments = GetSalaryLoanComment(year, month),
                        CreatedBy = currentUserId,
                        CreatedDate = DateTime.Now,
                        UpdatedBy = currentUserId,
                        UpdatedDate = DateTime.Now
                    },
                    transaction);
            }

            return new DeathCompensationLoanResult
            {
                Loan = totalLoan,
                LoanBalance = loanBalance
            };
        }

        private static async Task<decimal> GetDeathCompensationAdvanceSalaryAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string employeeId,
            int year,
            int month)
        {
            return await connection.ExecuteScalarAsync<decimal>(
                @"SELECT IFNULL(SUM(Amount), 0)
                  FROM hr_advance_salary
                  WHERE emp_no = @EmpNo
                    AND Year = @Year
                    AND Month = @Month
                    AND STATUS = 'A'",
                new
                {
                    EmpNo = employeeId,
                    Year = year,
                    Month = month
                },
                transaction);
        }

        private async Task<DeathCompensationVoucherStatusResult> UpdateDeathCompensationVoucherStatusesAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            IReadOnlyList<string> employeeIds,
            string currentUserId)
        {
            var result = new DeathCompensationVoucherStatusResult();
            if (employeeIds.Count == 0)
            {
                return result;
            }

            var statuses = (await connection.QueryAsync<DeathCompensationVoucherStatusRow>(
                @"SELECT
                      hdr.Emp_No AS EmpNo,
                      IFNULL(hss.status, 'ORIGINAL') AS Status
                  FROM hr_salaryprocessed_hdr hdr
                  LEFT JOIN hr_salaryvouchers_status hss
                    ON hss.emp_no = hdr.Emp_No
                   AND hss.year = hdr.SalaryYear
                   AND hss.month = hdr.SalaryMonth
                  WHERE hdr.SalaryYear = @Year
                    AND hdr.SalaryMonth = @Month
                    AND hdr.amount_cash <> 0
                    AND hdr.Emp_No IN @EmpNos",
                new
                {
                    Year = year,
                    Month = month,
                    EmpNos = employeeIds
                },
                transaction)).ToList();

            foreach (var status in statuses)
            {
                if (string.Equals(status.Status, "ORIGINAL", StringComparison.OrdinalIgnoreCase))
                {
                    result.Inserted += await connection.ExecuteAsync(
                        @"INSERT INTO hr_salaryvouchers_status
                          VALUES (@Year, @Month, @EmpNo, 'DUPLICATE', @CreatedBy, NOW())",
                        new
                        {
                            Year = year,
                            Month = month,
                            EmpNo = status.EmpNo,
                            CreatedBy = currentUserId
                        },
                        transaction);
                }
                else
                {
                    result.Updated += await connection.ExecuteAsync(
                        @"UPDATE hr_salaryvouchers_status
                          SET Created_Date = CONCAT(Created_Date, '~', NOW())
                          WHERE emp_no = @EmpNo
                            AND year = @Year
                            AND month = @Month",
                        new
                        {
                            EmpNo = status.EmpNo,
                            Year = year,
                            Month = month
                        },
                        transaction);
                }
            }

            return result;
        }

        private static async Task<IEnumerable<DeathCompensationProcessedEmployee>> LoadDeathCompensationProcessedEmployeesAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            IReadOnlyList<string> employeeIds)
        {
            if (employeeIds.Count == 0)
            {
                return Enumerable.Empty<DeathCompensationProcessedEmployee>();
            }

            return await connection.QueryAsync<DeathCompensationProcessedEmployee>(
                @"SELECT
                      hdr.Emp_No AS EmpNo,
                      IFNULL(GetEmpCode(hdr.Emp_No), hdr.Emp_No) AS EmployeeCode,
                      IFNULL(GetRouteCodeWithName(hdr.Emp_No), hdr.Emp_No) AS EmployeeName,
                      hdr.Dept AS DepartmentId,
                      hdr.BasicSalary,
                      hdr.Loan,
                      hdr.Advance,
                      hdr.Total_Deduction AS TotalDeduction,
                      hdr.NetPay,
                      hdr.Payment_Mode AS PaymentMode,
                      IFNULL(hss.status, 'ORIGINAL') AS VoucherStatus
                  FROM hr_salaryprocessed_hdr hdr
                  LEFT JOIN hr_salaryvouchers_status hss
                    ON hss.emp_no = hdr.Emp_No
                   AND hss.year = hdr.SalaryYear
                   AND hss.month = hdr.SalaryMonth
                  WHERE hdr.SalaryYear = @Year
                    AND hdr.SalaryMonth = @Month
                    AND hdr.Emp_No IN @EmpNos
                  ORDER BY hdr.Emp_No",
                new
                {
                    Year = year,
                    Month = month,
                    EmpNos = employeeIds
                },
                transaction);
        }

        private sealed class DeathCompensationCandidate
        {
            public string EmpNo { get; set; } = string.Empty;
            public string DeptCode { get; set; } = string.Empty;
            public DateTime? AppDate { get; set; }
            public int TotalVouchers { get; set; }
        }

        private sealed class DeathCompensationLoanRow
        {
            public string LD_No { get; set; } = string.Empty;
            public string EmpNo { get; set; } = string.Empty;
            public string LoanCode { get; set; } = string.Empty;
            public string LoanName { get; set; } = string.Empty;
            public decimal DisbursedAmount { get; set; }
            public decimal DeductionInstallments { get; set; }
            public decimal Paid { get; set; }
        }

        private sealed class DeathCompensationLoanResult
        {
            public decimal Loan { get; set; }
            public decimal LoanBalance { get; set; }
        }

        private sealed class DeathCompensationVoucherStatusRow
        {
            public string EmpNo { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }

        private sealed class DeathCompensationVoucherStatusResult
        {
            public int Inserted { get; set; }
            public int Updated { get; set; }
        }
    }
}
