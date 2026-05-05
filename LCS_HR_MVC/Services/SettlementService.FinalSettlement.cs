using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Settlement;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class SettlementService
    {
        public async Task<IEnumerable<dynamic>> SearchSettlementEmployeesAsync(string term, string currentUserId)
        {
            var results = new List<dynamic>();
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return results;
            }

            await connection.OpenAsync();

            const string query = @"
SELECT DISTINCT
    e.EMP_NO AS EmpNo,
    e.NAME AS EmployeeName
FROM hr_employeepersonaldetail e
INNER JOIN hr_city c ON c.Code = e.P_CITY_CODE
INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
WHERE lul.userid = @UserId
  AND e.EMP_STATUS = 'S'
  AND (e.NAME LIKE @LikeTerm OR e.EMP_NO LIKE @LikeTerm)
ORDER BY e.NAME
LIMIT 100;";

            var rows = await connection.QueryAsync(query, new
            {
                UserId = currentUserId,
                LikeTerm = $"%{term}%"
            });

            foreach (var row in rows)
            {
                results.Add(new
                {
                    label = $"{row.EmpNo} - {row.EmployeeName}",
                    value = row.EmpNo?.ToString() ?? string.Empty,
                    desc = row.EmployeeName?.ToString() ?? string.Empty
                });
            }

            return results;
        }

        public async Task<FinalSettlementPreviewResult> PreviewFinalSettlementAsync(FinalSettlementModel model, string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();

            var preparation = await PrepareFinalSettlementAsync(connection, null, model);
            var baseline = await CapturePreviewBaselineAsync(connection, null, preparation);
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var preview = await ExecuteFinalSettlementAsync(connection, transaction as MySqlTransaction, preparation, currentUserId);
                await transaction.RollbackAsync();
                preview.RollbackIntegrityPreserved = await VerifyPreviewBaselineAsync(connection, null, preparation, baseline);
                return preview;
            }
            catch
            {
                if (transaction.Connection != null)
                {
                    await transaction.RollbackAsync();
                }

                throw;
            }
        }

        private async Task<(bool success, string message)> ProcessFinalSettlementInternalAsync(FinalSettlementModel model, string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return (false, "Database error");
            }

            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var preparation = await PrepareFinalSettlementAsync(connection, transaction as MySqlTransaction, model);
                var result = await ExecuteFinalSettlementAsync(connection, transaction as MySqlTransaction, preparation, currentUserId);
                await transaction.CommitAsync();
                return (true, $"{result.Months.Count} Pay Slip(s) generated.");
            }
            catch
            {
                if (transaction.Connection != null)
                {
                    await transaction.RollbackAsync();
                }

                throw;
            }
        }

        private async Task<FinalSettlementPreviewResult> ReplayFinalSettlementInternalAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            FinalSettlementModel model,
            string currentUserId)
        {
            var preparation = await PrepareFinalSettlementAsync(connection, transaction, model);
            StateHelper.userid = currentUserId;

            var fuelPrices = (await connection.QueryAsync<FuelPrice>(
                "SELECT TYPE, Price FROM hr_fuelprices ORDER BY code",
                transaction: transaction)).ToList();

            var globalAllowances = (await connection.QueryAsync<AllowanceDefinition>(
                "SELECT Code, FullName, TYPE, Pct_Amount, Fix_Amount, exclude_absent, glcode AS GlCode FROM hr_allow_ded_details WHERE EmpWise_Flag = 'All'",
                transaction: transaction)).ToList();

            int nextLoanDeductionNumber = await GetNextLoanDeductionNumberAsync(connection, transaction);

            var result = new FinalSettlementPreviewResult
            {
                EmpNo = preparation.Employee.EmpNo,
                EmployeeName = preparation.Employee.EmployeeName,
                ResignDate = preparation.Employee.TerminationDate
            };

            foreach (var request in preparation.MonthRequests)
            {
                bool existingSalaryRow = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM hr_salaryprocessed_hdr
                      WHERE Emp_No = @EmpNo
                        AND SalaryYear = @SalaryYear
                        AND SalaryMonth = @SalaryMonth",
                    new
                    {
                        preparation.Employee.EmpNo,
                        request.SalaryYear,
                        request.SalaryMonth
                    },
                    transaction) > 0;

                var computation = await BuildSettlementMonthComputationAsync(
                    connection,
                    transaction,
                    preparation.Employee,
                    request,
                    fuelPrices,
                    globalAllowances,
                    currentUserId,
                    nextLoanDeductionNumber);

                nextLoanDeductionNumber += computation.LoanDeductions.Count;
                computation.Preview.ExistingSalaryRowDetected = existingSalaryRow;
                result.Months.Add(computation.Preview);
            }

            return result;
        }

        private async Task<FinalSettlementPreviewResult> ExecuteFinalSettlementAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SettlementPreparation preparation,
            string currentUserId)
        {
            StateHelper.userid = currentUserId;

            var fuelPrices = (await connection.QueryAsync<FuelPrice>(
                "SELECT TYPE, Price FROM hr_fuelprices ORDER BY code",
                transaction: transaction)).ToList();

            var globalAllowances = (await connection.QueryAsync<AllowanceDefinition>(
                "SELECT FullName, TYPE, Pct_Amount, Fix_Amount, exclude_absent FROM hr_allow_ded_details WHERE EmpWise_Flag = 'All'",
                transaction: transaction)).ToList();

            int nextLoanDeductionNumber = await GetNextLoanDeductionNumberAsync(connection, transaction);

            var result = new FinalSettlementPreviewResult
            {
                EmpNo = preparation.Employee.EmpNo,
                EmployeeName = preparation.Employee.EmployeeName,
                ResignDate = preparation.Employee.TerminationDate
            };

            foreach (var request in preparation.MonthRequests)
            {
                bool existingSalaryRow = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM hr_salaryprocessed_hdr
                      WHERE Emp_No = @EmpNo
                        AND SalaryYear = @SalaryYear
                        AND SalaryMonth = @SalaryMonth",
                    new
                    {
                        preparation.Employee.EmpNo,
                        request.SalaryYear,
                        request.SalaryMonth
                    },
                    transaction) > 0;

                if (existingSalaryRow)
                {
                    throw new ArgumentException($"Final settlement is already processed for {request.MonthName} {request.SalaryYear}.");
                }

                var computation = await BuildSettlementMonthComputationAsync(
                    connection,
                    transaction,
                    preparation.Employee,
                    request,
                    fuelPrices,
                    globalAllowances,
                    currentUserId,
                    nextLoanDeductionNumber);

                nextLoanDeductionNumber += computation.LoanDeductions.Count;

                await InsertLoanDeductionsAsync(connection, transaction, computation.LoanDeductions);
                await InsertSettlementHeaderAsync(connection, transaction, computation.Header);
                await InsertSettlementDetailsAsync(connection, transaction, computation.Details);

                result.Months.Add(computation.Preview);
            }

            return result;
        }

        private async Task<SettlementPreparation> PrepareFinalSettlementAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            FinalSettlementModel model)
        {
            if (string.IsNullOrWhiteSpace(model.EmpNo))
            {
                throw new ArgumentException("Employee No is required.");
            }

            var employee = await GetSettlementEmployeeContextAsync(connection, transaction, model.EmpNo.Trim());

            if (!string.Equals(employee.SettlementFlag, "Y", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(employee.EmployeeStatus, "S", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Selected employee is not marked for final settlement.");
            }

            var monthRequests = new List<SettlementMonthRequest>
            {
                CreateMonthRequest(employee.TerminationDate, model.WorkingDays1),
                CreateMonthRequest(employee.TerminationDate.AddMonths(1), model.WorkingDays2)
            };

            return new SettlementPreparation(employee, monthRequests);
        }

        private async Task<SettlementEmployeeContext> GetSettlementEmployeeContextAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string empNo)
        {
            const string query = @"
SELECT
    t.Emp_No AS EmpNo,
    p.NAME AS EmployeeName,
    t.TerminationDate,
    t.Settlement AS SettlementFlag,
    p.EMP_STATUS AS EmployeeStatus,
    p.P_CITY_CODE AS CityCode,
    (
        SELECT d.DeptCode
        FROM hr_employeedepartmentdetails d
        WHERE d.Emp_No = p.EMP_NO
        ORDER BY IFNULL(d.ToDate, '2099-08-28') DESC, d.FromDate DESC, d.Code DESC
        LIMIT 1
    ) AS DeptCode,
    (
        SELECT TRIM(LEADING '0' FROM c.station_id)
        FROM hr_city c
        WHERE c.Code = p.P_CITY_CODE
        LIMIT 1
    ) AS StationId,
    (
        SELECT TRIM(LEADING '0' FROM c.BranchLocation)
        FROM hr_city c
        WHERE c.Code = p.P_CITY_CODE
        LIMIT 1
    ) AS BranchLocation
FROM hr_employeeterminationdetails t
INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = t.Emp_No
WHERE t.Emp_No = @EmpNo
ORDER BY t.Code DESC
LIMIT 1;";

            var employee = await connection.QueryFirstOrDefaultAsync<SettlementEmployeeContext>(
                query,
                new { EmpNo = empNo },
                transaction);

            if (employee == null)
            {
                throw new ArgumentException("Employee has no termination record.");
            }

            if (string.IsNullOrWhiteSpace(employee.DeptCode))
            {
                throw new ArgumentException($"Department is not defined for employee {empNo}.");
            }

            return employee;
        }

        private async Task<SettlementMonthComputation> BuildSettlementMonthComputationAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SettlementEmployeeContext employee,
            SettlementMonthRequest request,
            List<FuelPrice> fuelPrices,
            List<AllowanceDefinition> globalAllowances,
            string currentUserId,
            int nextLoanDeductionNumber)
        {
            var periodDays = GetPayrollPeriodDayCount(request.SalaryYear, request.SalaryMonth);
            var attendance = await GetAttendanceSnapshotAsync(connection, transaction, employee.EmpNo, request.SalaryYear, request.SalaryMonth);
            int adjustedAbsents = attendance == null ? 0 : Math.Max(0, attendance.Absents - attendance.AdjustmentAbsent);
            int adjustedRuleAbsents = attendance == null ? 0 : Math.Max(0, attendance.RuleAbsents - attendance.AdjustmentRuleAbsent);

            int workeddays;
            int absents;
            int ruleAbsents;
            int totalFuelDays;
            int fuelDays;
            string attendanceSource;

            if (request.InputWorkingDays > 0)
            {
                workeddays = request.InputWorkingDays;
                ruleAbsents = adjustedRuleAbsents;
                absents = Math.Max(0, periodDays - workeddays - ruleAbsents);

                if (attendance != null)
                {
                    totalFuelDays = Math.Max(0, periodDays - (attendance.Sundays + attendance.Holidays + absents + ruleAbsents + attendance.Leaves));
                    fuelDays = Math.Max(0, periodDays - (absents + attendance.Sundays + attendance.Holidays + ruleAbsents + attendance.Leaves));
                    attendanceSource = "manual-input+attendance";
                }
                else
                {
                    totalFuelDays = Math.Max(0, periodDays);
                    fuelDays = Math.Max(0, workeddays);
                    attendanceSource = "manual-input";
                }
                fuelDays = workeddays == 0 ? 0 : fuelDays;
            }
            else if (attendance != null)
            {
                absents = adjustedAbsents;
                ruleAbsents = adjustedRuleAbsents;

                if (attendance.Sundays + attendance.Holidays + ruleAbsents + absents >= 30)
                {
                    workeddays = 0;
                    totalFuelDays = 0;
                }
                else
                {
                    workeddays = Math.Max(0, periodDays - (absents + ruleAbsents));
                    totalFuelDays = Math.Max(0, periodDays - (attendance.Sundays + attendance.Holidays + absents + ruleAbsents + attendance.Leaves));
                }

                fuelDays = Math.Max(0, periodDays - (absents + attendance.Sundays + attendance.Holidays + ruleAbsents + attendance.Leaves));
                fuelDays = workeddays == 0 ? 0 : fuelDays;
                attendanceSource = "adjusted-attendanceprocess";
            }
            else
            {
                throw new ArgumentException($"No employee to process in {request.MonthName} {request.SalaryYear}.");
            }

            var salarySnapshot = await GetSalarySnapshotAsync(connection, transaction, employee.EmpNo, request.SalaryDate);
            var shifthours = await GetShiftHoursAsync(connection, transaction, employee.EmpNo, request.SalaryDate, employee.TerminationDate);

            decimal basicsalary = salarySnapshot.BasicSalary;
            decimal taxDeductOn = salarySnapshot.CashAmount;
            string paymentmode = salarySnapshot.PaymentMode;
            decimal cashamount = 0m;
            decimal currentsalary = 0m;
            decimal absentamount = 0m;
            decimal fuelperday = 0m;
            decimal fuelamount = 0m;
            decimal partTime = 0m;
            decimal commamount = 0m;
            decimal grosspay = 0m;
            decimal extraDays = 0m;
            decimal extraDaysAmount = 0m;
            decimal extraHours = 0m;
            decimal extraHoursAmount = 0m;
            decimal extraFuel = 0m;
            decimal extraFuelAmount = 0m;
            decimal extraAmount = 0m;
            decimal allowance = 0m;
            decimal deduction = 0m;
            decimal totalDeductions = 0m;
            decimal loan = 0m;
            decimal loanBalance = 0m;
            decimal advanceSalary = 0m;
            decimal tax = 0m;
            decimal amountBank = 0m;
            decimal amountCash = 0m;
            int fixedExtraDaysCityWise = 0;
            var details = new List<SettlementDetailRow>();
            var loanDeductions = new List<LoanDeductionRow>();

            decimal fixedExtraAmount = await connection.ExecuteScalarAsync<decimal>(
                @"SELECT IFNULL(SUM(Amount), 0)
                  FROM hr_employee_extras_fixed
                  WHERE emp_no = @EmpNo
                    AND FromDate <= @SalaryDate",
                new
                {
                    EmpNo = employee.EmpNo,
                    request.SalaryDate
                },
                transaction);

            extraAmount += fixedExtraAmount;
            if (fixedExtraAmount > 0 && (absents + ruleAbsents) > 0)
            {
                decimal noOfAbsents = absents + ruleAbsents;
                decimal perDay = fixedExtraAmount / 30m;
                decimal extrasAbsentDeduction = noOfAbsents * perDay;
                deduction += extrasAbsentDeduction;
                details.Add(CreateDetailRow(
                    request,
                    employee.EmpNo,
                    "Extras Absent(s) Deduction: ",
                    extrasAbsentDeduction,
                    "ED",
                    "D"));
            }

            var monthlyExtras = (await connection.QueryAsync<MonthlyExtraRow>(
                @"SELECT
                      ex.extra_type AS ExtraTypeId,
                      SUM(ex.VALUE) AS Amount,
                      et.GLID AS GlCode,
                      et.IsTaxable
                  FROM hr_employeeextras ex
                  INNER JOIN hr_extratype et ON et.ETId = ex.extra_type
                  WHERE ex.emp_no = @EmpNo
                    AND YEAR = @Year
                    AND MONTH = @Month
                    AND ex.Status = 2
                  GROUP BY ex.emp_no, YEAR, MONTH, ex.extra_type, et.GLID, et.IsTaxable",
                new
                {
                    EmpNo = employee.EmpNo,
                    Year = request.SalaryYear,
                    Month = request.SalaryMonth
                },
                transaction)).ToList();

            foreach (var extra in monthlyExtras)
            {
                switch (extra.ExtraTypeId)
                {
                    case 2:
                        extraHours += extra.Amount;
                        break;
                    case 1:
                        extraDays += extra.Amount;
                        break;
                    case 3:
                        extraFuel += extra.Amount;
                        break;
                    default:
                        extraAmount += extra.Amount;
                        details.Add(new SettlementDetailRow
                        {
                            SalaryYear = request.SalaryYear,
                            SalaryMonth = request.SalaryMonth,
                            EmpNo = employee.EmpNo,
                            Description = $"Extra: {((LCS.EExtraType)extra.ExtraTypeId).ToString()}",
                            Amount = extra.Amount,
                            DeductionType = "E",
                            Type = "A",
                            AllowCode = extra.ExtraTypeId.ToString(CultureInfo.InvariantCulture),
                            GlCode = extra.GlCode
                        });
                        break;
                }
            }

            partTime = await connection.ExecuteScalarAsync<decimal>(
                @"SELECT COALESCE(Amount, 0)
                  FROM hr_employee_parttime
                  WHERE emp_no = @EmpNo
                    AND @SalaryDate BETWEEN fromdate AND IFNULL(todate, '2099-08-28')
                  ORDER BY CODE DESC
                  LIMIT 1",
                new
                {
                    EmpNo = employee.EmpNo,
                    request.SalaryDate
                },
                transaction);

            if (partTime > 0 && (absents + ruleAbsents) > 0)
            {
                decimal noOfAbsents = absents + ruleAbsents;
                decimal perDay = partTime / 30m;
                decimal partTimeAbsentDeduction = noOfAbsents * perDay;
                deduction += partTimeAbsentDeduction;
                details.Add(CreateDetailRow(
                    request,
                    employee.EmpNo,
                    "Partime Absent(s) Deduction: ",
                    partTimeAbsentDeduction,
                    "PA",
                    "D"));
            }

            ApplyAllowanceDefinitions(
                globalAllowances,
                details,
                request,
                employee.EmpNo,
                basicsalary + cashamount,
                absents,
                ruleAbsents,
                attendance?.Holidays ?? 0,
                attendance?.Sundays ?? 0,
                ref allowance,
                ref deduction);

            var employeeAllowances = (await connection.QueryAsync<AllowanceDefinition>(
                @"SELECT
                      b.Code,
                      b.FullName,
                      b.type AS Type,
                      b.Pct_Amount,
                      b.Fix_Amount,
                      b.exclude_absent,
                      b.glcode AS GlCode
                  FROM hr_employeead_details a
                  INNER JOIN hr_allow_ded_details b ON a.ad_code = b.code
                  WHERE a.emp_no = @EmpNo
                    AND @SalaryDate BETWEEN a.EffectiveFrom AND IFNULL(a.EffectiveTo, '2099-08-28')
                  ORDER BY b.code",
                new
                {
                    EmpNo = employee.EmpNo,
                    request.SalaryDate
                },
                transaction)).ToList();

            ApplyAllowanceDefinitions(
                employeeAllowances,
                details,
                request,
                employee.EmpNo,
                basicsalary + cashamount,
                absents,
                ruleAbsents,
                attendance?.Holidays ?? 0,
                attendance?.Sundays ?? 0,
                ref allowance,
                ref deduction);

            commamount = await GetSettlementCommissionAmountAsync(
                connection,
                transaction,
                employee.EmpNo,
                request.SalaryYear,
                request.SalaryMonth);

            if (commamount > 0)
            {
                details.Add(new SettlementDetailRow
                {
                    SalaryYear = request.SalaryYear,
                    SalaryMonth = request.SalaryMonth,
                    EmpNo = employee.EmpNo,
                    Description = "Commission",
                    Amount = commamount,
                    DeductionType = @"\N",
                    Type = "A",
                    GlCode = "172"
                });
            }

            var loanRows = (await connection.QueryAsync<LoanDisbursement>(
                @"SELECT
                      a.LD_No AS LoanDisbursedId,
                      a.loancode AS LoanCode,
                      l.fullname AS FullName,
                      a.DisbursedDate,
                      a.DisbursedAmount,
                      a.DeductionInstallments,
                      a.DeductionStartDate,
                      (
                          SELECT IFNULL(SUM(b.DeductionAmount), 0)
                          FROM hr_employeeloandeduction b
                          WHERE b.emp_no = a.Emp_No
                            AND b.ld_no = a.LD_No
                      ) AS Paid
                  FROM hr_employeeloandisbursed a
                  INNER JOIN hr_loantypes l ON a.LoanCode = l.Code
                  WHERE a.emp_no = @EmpNo
                    AND a.DeductionStartDate <= @SalaryDate",
                new
                {
                    EmpNo = employee.EmpNo,
                    request.SalaryDate
                },
                transaction)).ToList();

            foreach (var loanRow in loanRows)
            {
                if (loanRow.DeductionInstallments <= 0 || loanRow.DisbursedAmount <= loanRow.Paid)
                {
                    continue;
                }

                decimal installmentAmount = loanRow.DisbursedAmount / loanRow.DeductionInstallments;
                decimal balance = loanRow.DisbursedAmount - loanRow.Paid;
                if (installmentAmount > balance)
                {
                    installmentAmount = balance;
                }

                loan += installmentAmount;
                loanBalance += loanRow.DisbursedAmount - (loanRow.Paid + installmentAmount);

                if (installmentAmount > 0)
                {
                    loanDeductions.Add(new LoanDeductionRow
                    {
                        LoanDeductionCode = nextLoanDeductionNumber.ToString("D10"),
                        LoanDisbursedId = loanRow.LoanDisbursedId,
                        EmpNo = employee.EmpNo,
                        LoanCode = loanRow.LoanCode,
                        DeductionDate = DateTime.Now,
                        DeductionAmount = installmentAmount,
                        Balance = loanRow.DisbursedAmount - (loanRow.Paid + installmentAmount),
                        Comments = $"From Salary of {request.MonthName} {request.SalaryYear}",
                        CreatedBy = currentUserId,
                        CreatedDate = DateTime.Now,
                        UpdatedBy = currentUserId,
                        UpdatedDate = DateTime.Now
                    });
                    nextLoanDeductionNumber++;
                }
            }

            advanceSalary = await connection.ExecuteScalarAsync<decimal>(
                @"SELECT IFNULL(SUM(Amount), 0)
                  FROM hr_advance_salary
                  WHERE emp_no = @EmpNo
                    AND YEAR = @Year
                    AND MONTH = @Month
                    AND STATUS = 'A'",
                new
                {
                    EmpNo = employee.EmpNo,
                    Year = request.SalaryYear,
                    Month = request.SalaryMonth
                },
                transaction);

            var penalties = (await connection.QueryAsync<PenaltyFineRow>(
                @"SELECT amount, Remarks, Type
                  FROM hr_penalty_fine
                  WHERE MONTH(FineDate) = @Month
                    AND YEAR(FineDate) = @Year
                    AND code = @EmpNo",
                new
                {
                    Month = request.SalaryMonth,
                    Year = request.SalaryYear,
                    EmpNo = employee.EmpNo
                },
                transaction)).ToList();

            foreach (var penalty in penalties)
            {
                deduction += penalty.Amount;
                details.Add(CreateDetailRow(
                    request,
                    employee.EmpNo,
                    "Penalty/Fine: " + penalty.Remarks,
                    penalty.Amount,
                    penalty.Type,
                    "D"));
            }

            var fuelDefinition = await GetFuelDefinitionAsync(connection, transaction, employee.EmpNo, request.SalaryDate, employee.TerminationDate);
            if (fuelDefinition != null)
            {
                decimal fPerDay = fuelDefinition.FuelPerDay;
                decimal fAmount = GetFuelPrice(fuelPrices, fuelDefinition.FCode);

                if (fuelDefinition.FuelMode == "PD")
                {
                    fuelperday += fPerDay;
                    fuelamount += (fuelDays * fPerDay) * fAmount;
                }
                else if (fuelDefinition.FuelMode == "FL")
                {
                    fPerDay = totalFuelDays == 0 ? 0 : fPerDay / totalFuelDays;
                    fuelperday += fPerDay;
                    fuelamount += (fuelDays * fPerDay) * fAmount;
                }
                else
                {
                    if (totalFuelDays != 0)
                    {
                        fuelamount += (fPerDay / totalFuelDays) * fuelDays;
                        fPerDay = fAmount == 0 ? 0 : (fPerDay / totalFuelDays) / fAmount;
                    }
                    else
                    {
                        fPerDay = 0;
                    }

                    fuelperday += fPerDay;
                }

                extraFuel += extraDays * fPerDay;
                extraFuelAmount = extraFuel * fAmount;
            }
            else if (extraFuel > 0)
            {
                extraFuelAmount = extraFuel * GetFuelPrice(fuelPrices, "002");
            }

            decimal perDayAmount = (basicsalary + cashamount) / 30m;
            decimal perHourAmount = perDayAmount / Convert.ToDecimal(shifthours.TotalHours == 0 ? 8 : shifthours.TotalHours);

            if (fixedExtraDaysCityWise > 0)
            {
                extraDays += fixedExtraDaysCityWise;
            }

            extraDaysAmount = perDayAmount * extraDays;
            extraHoursAmount = perHourAmount * extraHours;
            currentsalary = basicsalary + cashamount;
            absentamount = (absents + ruleAbsents) * perDayAmount;

            grosspay += basicsalary;
            grosspay += cashamount;
            grosspay += fuelamount;
            grosspay += commamount;
            grosspay += extraDaysAmount;
            grosspay += extraFuelAmount;
            grosspay += extraHoursAmount;
            grosspay += extraAmount;
            grosspay += allowance;
            grosspay += partTime;

            totalDeductions += absentamount;
            totalDeductions += deduction;
            totalDeductions += loan;
            totalDeductions += advanceSalary;
            totalDeductions += tax;

            decimal monthlyDeduction = absentamount + deduction + loan + advanceSalary;
            if (!string.Equals(paymentmode, "C", StringComparison.OrdinalIgnoreCase))
            {
                if (taxDeductOn > 0)
                {
                    decimal totalCash = cashamount + fuelamount + commamount + extraDaysAmount + extraFuelAmount + extraHoursAmount + extraAmount + allowance + partTime;
                    amountBank = basicsalary - tax;
                    if (monthlyDeduction >= totalCash)
                    {
                        monthlyDeduction -= totalCash;
                        amountCash = 0;
                        amountBank -= monthlyDeduction;
                    }
                    else
                    {
                        amountCash = totalCash - monthlyDeduction;
                    }
                }
                else
                {
                    amountBank = grosspay - totalDeductions;
                }
            }
            else
            {
                amountCash = grosspay - totalDeductions;
            }

            string currentFlag;
            if (string.Equals(paymentmode, "C", StringComparison.OrdinalIgnoreCase))
            {
                currentFlag = "Cash";
            }
            else if (string.Equals(paymentmode, "Q", StringComparison.OrdinalIgnoreCase))
            {
                currentFlag = "Cheuqe";
                paymentmode = "B";
            }
            else
            {
                currentFlag = "Bank";
            }

            var header = new SettlementHeaderRow
            {
                SalaryYear = request.SalaryYear,
                SalaryMonth = request.SalaryMonth,
                EmpNo = employee.EmpNo,
                Dept = employee.DeptCode,
                SalaryProcessedDate = DateTime.Now,
                BasicSalary = basicsalary,
                WorkedDays = workeddays,
                CurrentSalary = currentsalary,
                AbsentDays = absents,
                RuleAbsentDays = ruleAbsents,
                AbsentAmount = absentamount,
                OTAmount = 0m,
                PTAmount = partTime,
                ExtraHours = extraHours,
                ExtraHoursAmount = extraHoursAmount,
                ExtraDays = extraDays,
                ExtraDaysAmount = extraDaysAmount,
                ExtraFuel = extraFuel,
                ExtraFuelAmount = extraFuelAmount,
                ExtraAmount = extraAmount,
                FuelPerDay = fuelperday,
                FuelDays = fuelDays,
                FuelAmount = fuelamount,
                CommAmount = commamount,
                Allowances = allowance,
                Deductions = deduction,
                Loan = loan,
                LoanBalance = loanBalance,
                Advance = advanceSalary,
                Tax = tax,
                GrossPay = grosspay,
                TotalDeduction = totalDeductions,
                NetPay = Math.Round(grosspay - totalDeductions),
                CashPayment = cashamount,
                AmountBank = Math.Round(amountBank),
                AmountCash = Math.Round(amountCash),
                PaymentMode = currentFlag,
                Comments = $"Salary of {request.MonthName} {request.SalaryYear}",
                CreatedBy = currentUserId,
                CreatedDate = DateTime.Now,
                UpdatedBy = currentUserId,
                UpdatedDate = DateTime.Now,
                CreatedStation = employee.StationId,
                CreatedLocation = employee.BranchLocation,
                Type = string.IsNullOrWhiteSpace(paymentmode) ? "C" : paymentmode
            };

            var preview = new FinalSettlementMonthPreview
            {
                SalaryYear = request.SalaryYear,
                SalaryMonth = request.SalaryMonth,
                MonthName = request.MonthName,
                AttendanceSource = attendanceSource,
                InputWorkingDays = request.InputWorkingDays,
                WorkedDays = workeddays,
                AbsentDays = absents,
                RuleAbsentDays = ruleAbsents,
                FuelDays = fuelDays,
                BasicSalary = basicsalary,
                Allowances = allowance,
                Deductions = deduction,
                Loan = loan,
                Advance = advanceSalary,
                Tax = tax,
                AbsentAmount = absentamount,
                FuelAmount = fuelamount,
                AmountBank = header.AmountBank,
                AmountCash = header.AmountCash,
                PaymentMode = header.PaymentMode,
                GrossPay = grosspay,
                TotalDeduction = totalDeductions,
                NetPay = header.NetPay,
                DetailRowsGenerated = details.Count,
                LoanDeductionRowsInserted = loanDeductions.Count,
                ExistingSalaryRowDetected = false
            };

            return new SettlementMonthComputation(header, details, loanDeductions, preview);
        }

        private async Task<SalarySnapshot> GetSalarySnapshotAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string empNo,
            DateTime salaryDate)
        {
            var row = await connection.QueryFirstOrDefaultAsync<SalaryBaseRow>(
                @"SELECT
                      CASE
                          WHEN @SalaryDate <= he.EffectiveFrom THEN he.SalaryAmount
                          WHEN @SalaryDate > he.EffectiveFrom THEN he.IncrementAmount
                          ELSE NULL
                      END AS BasicSalary,
                      IFNULL(he.AmtCash, 0) AS CashAmount,
                      IFNULL(he.Current_Flag, '') AS PaymentMode
                  FROM hr_employeesalarydetails he
                  WHERE he.emp_no = @EmpNo
                  ORDER BY he.Code DESC
                  LIMIT 1",
                new
                {
                    SalaryDate = salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                    EmpNo = empNo
                },
                transaction);

            if (row == null)
            {
                throw new ArgumentException($"Employee No. {empNo} record not found in PayStructure.");
            }

            var increments = (await connection.QueryAsync<IncrementSummaryRow>(
                @"SELECT inc.Type, SUM(IFNULL(inc.Amount, 0)) AS Amount
                  FROM hr_increment inc
                  WHERE inc.ID = @EmpNo
                    AND inc.IncStatusID = 2
                    AND inc.FromDate <= @SalaryDate
                  GROUP BY inc.Type",
                new
                {
                    EmpNo = empNo,
                    SalaryDate = salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                },
                transaction)).ToList();

            decimal oldIncrements = increments.Where(x => x.Type == "I").Sum(x => x.Amount);
            decimal oldDecrements = increments.Where(x => x.Type != "I").Sum(x => x.Amount);

            return new SalarySnapshot
            {
                BasicSalary = (row.BasicSalary + oldIncrements) - oldDecrements,
                CashAmount = row.CashAmount,
                PaymentMode = row.PaymentMode ?? string.Empty
            };
        }

        private async Task<TimeSpan> GetShiftHoursAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string empNo,
            DateTime salaryDate,
            DateTime resignDate)
        {
            ShiftTiming? shift = await connection.QueryFirstOrDefaultAsync<ShiftTiming>(
                @"SELECT sh.Start_Time AS StartTime, sh.End_Time AS EndTime
                  FROM hr_shiftdetails sh
                  WHERE sh.CODE = (
                      SELECT et.shiftcode
                      FROM hr_employeeshifttimings et
                      WHERE et.emp_no = @EmpNo
                        AND @SalaryDate BETWEEN et.fromdate AND IFNULL(et.todate, '2099-08-28')
                      ORDER BY et.ID DESC
                      LIMIT 1
                  )",
                new
                {
                    EmpNo = empNo,
                    SalaryDate = salaryDate
                },
                transaction);

            shift ??= await connection.QueryFirstOrDefaultAsync<ShiftTiming>(
                @"SELECT sh.Start_Time AS StartTime, sh.End_Time AS EndTime
                  FROM hr_shiftdetails sh
                  WHERE sh.CODE = (
                      SELECT et.shiftcode
                      FROM hr_employeeshifttimings et
                      WHERE et.emp_no = @EmpNo
                        AND @ResignDate BETWEEN et.fromdate AND IFNULL(et.todate, '2099-08-28')
                      ORDER BY et.ID DESC
                      LIMIT 1
                  )",
                new
                {
                    EmpNo = empNo,
                    ResignDate = resignDate
                },
                transaction);

            if (shift?.StartTime == null || shift.EndTime == null)
            {
                return TimeSpan.Zero;
            }

            var start = shift.StartTime.Value;
            var end = shift.EndTime.Value;

            if (end > start)
            {
                return end - start;
            }

            TimeSpan dayEnd = new(24, 0, 0);
            TimeSpan dayStart = new(0, 0, 1);
            var hours = dayEnd - start;
            return (end - dayStart + hours).Add(new TimeSpan(0, 0, 0, 1));
        }

        private async Task<SettlementAttendanceSnapshot?> GetAttendanceSnapshotAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string empNo,
            int year,
            int month)
        {
            return await connection.QueryFirstOrDefaultAsync<SettlementAttendanceSnapshot>(
                @"SELECT
                      Absents,
                      Sundays,
                      Holidays,
                      Leaves,
                      ruleAbsents AS RuleAbsents,
                      adjustmentAbsent AS AdjustmentAbsent,
                      adjustmentRAbsent AS AdjustmentRuleAbsent
                  FROM hr_employeeattendanceprocess
                  WHERE Year = @Year
                    AND Month = @Month
                    AND emp_no = @EmpNo
                  LIMIT 1",
                new
                {
                    Year = year,
                    Month = month,
                    EmpNo = empNo
                },
                transaction);
        }

        private async Task<FuelDefinition?> GetFuelDefinitionAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string empNo,
            DateTime salaryDate,
            DateTime resignDate)
        {
            var fuel = await connection.QueryFirstOrDefaultAsync<FuelDefinition>(
                @"SELECT Fcode AS FCode, Fuel_Pday AS FuelPerDay, Fuel_mode AS FuelMode
                  FROM hr_employeefueldetails
                  WHERE FromDate <= @SalaryDate
                    AND emp_no = @EmpNo
                  ORDER BY CODE DESC
                  LIMIT 1",
                new
                {
                    SalaryDate = salaryDate,
                    EmpNo = empNo
                },
                transaction);

            if (fuel != null)
            {
                return fuel;
            }

            return await connection.QueryFirstOrDefaultAsync<FuelDefinition>(
                @"SELECT Fcode AS FCode, Fuel_Pday AS FuelPerDay, Fuel_mode AS FuelMode
                  FROM hr_employeefueldetails
                  WHERE FromDate <= @ResignDate
                    AND emp_no = @EmpNo
                  ORDER BY CODE DESC
                  LIMIT 1",
                new
                {
                    ResignDate = resignDate,
                    EmpNo = empNo
                },
                transaction);
        }

        private async Task<int> GetNextLoanDeductionNumberAsync(MySqlConnection connection, MySqlTransaction? transaction)
        {
            int next = await connection.ExecuteScalarAsync<int>(
                "SELECT COALESCE(MAX(CAST(LDed_No AS UNSIGNED)), 0) + 1 FROM hr_employeeloandeduction",
                transaction: transaction);

            return next <= 0 ? 1 : next;
        }

        private static void ApplyAllowanceDefinitions(
            IEnumerable<AllowanceDefinition> definitions,
            List<SettlementDetailRow> details,
            SettlementMonthRequest request,
            string empNo,
            decimal salaryBase,
            int absents,
            int ruleAbsents,
            int holidays,
            int sundays,
            ref decimal allowance,
            ref decimal deduction)
        {
            foreach (var definition in definitions)
            {
                decimal percentAmount = salaryBase * definition.PctAmount / 100m;
                decimal allowanceDeductionAmount = percentAmount + definition.FixAmount;

                if (string.Equals(definition.Type, "A", StringComparison.OrdinalIgnoreCase))
                {
                    allowance += allowanceDeductionAmount;
                    if ((absents + ruleAbsents) > 0 &&
                        !string.Equals(definition.ExcludeAbsent, "N", StringComparison.OrdinalIgnoreCase))
                    {
                        int totalAbsents = absents + ruleAbsents;
                        int conditionCount = string.Equals(definition.ExcludeAbsent, "Q", StringComparison.OrdinalIgnoreCase) ? 4 : 30;
                        decimal perCount = allowanceDeductionAmount / conditionCount;
                        decimal dedCount;

                        if (string.Equals(definition.ExcludeAbsent, "Q", StringComparison.OrdinalIgnoreCase) && totalAbsents >= 4)
                        {
                            dedCount = allowanceDeductionAmount;
                        }
                        else if (totalAbsents + holidays + sundays >= 30 || totalAbsents + holidays + sundays > 28)
                        {
                            dedCount = 30m * perCount;
                        }
                        else
                        {
                            dedCount = totalAbsents * perCount;
                        }

                        deduction += dedCount;
                        details.Add(new SettlementDetailRow
                        {
                            SalaryYear = request.SalaryYear,
                            SalaryMonth = request.SalaryMonth,
                            EmpNo = empNo,
                            Description = "Allowance Absent(s) Deduction: " + definition.FullName,
                            Amount = dedCount,
                            DeductionType = "AD",
                            Type = "D",
                            AllowCode = definition.Code,
                            GlCode = definition.GlCode
                        });
                    }
                }

                if (string.Equals(definition.Type, "D", StringComparison.OrdinalIgnoreCase))
                {
                    deduction += allowanceDeductionAmount;
                }

                details.Add(new SettlementDetailRow
                {
                    SalaryYear = request.SalaryYear,
                    SalaryMonth = request.SalaryMonth,
                    EmpNo = empNo,
                    Description = (string.Equals(definition.Type, "A", StringComparison.OrdinalIgnoreCase) ? "Allowance: " : "Deduction: ") + definition.FullName,
                    Amount = allowanceDeductionAmount,
                    DeductionType = "AD",
                    Type = string.Equals(definition.Type, "A", StringComparison.OrdinalIgnoreCase) ? "A" : "D",
                    AllowCode = definition.Code,
                    GlCode = definition.GlCode
                });
            }
        }

        private async Task<decimal> GetSettlementCommissionAmountAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string empNo,
            int year,
            int month)
        {
            return await connection.ExecuteScalarAsync<decimal>(
                @"SELECT
                      IFNULL(SUM((
                          IFNULL(DOM_CREDIT, 0)
                        + IFNULL(LOCAL_CREDIT, 0)
                        + IFNULL(LOCAL_DLD, 0)
                        + IFNULL(DomesticDelivery, 0)
                        + IFNULL(INTL_CREDIT, 0)
                        + IFNULL(COD, 0)
                        + IFNULL(OVERNIGHT, 0)
                        + IFNULL(YB1KG, 0)
                        + IFNULL(YB2KG, 0)
                        + IFNULL(YB5KG, 0)
                        + IFNULL(YB10KG, 0)
                        + IFNULL(YB15KG, 0)
                        + IFNULL(YB25KG, 0)
                        + IFNULL(FLAYER, 0)
                        + IFNULL(DETAIN, 0)
                        + IFNULL(OVERLAND, 0)
                        + IFNULL(PREPAID, 0)
                        + IFNULL(LOVELINE, 0)
                        + IFNULL(INTL_CASH, 0)
                        + IFNULL(OLE_Credit_Booking, 0)
                        + IFNULL(OLE_Dispatch_Proper, 0)
                        + IFNULL(OLE_Transit_Dispatch, 0)
                        + IFNULL(OLE_Delivery_OPS, 0)
                        + IFNULL(OLE_Delivery, 0)
                        + IFNULL(MOFA_OTO, 0)
                        + IFNULL(MOFA_OTD, 0)
                        + IFNULL(Rms_Cod_Booking, 0)
                        + IFNULL(AllInOne, 0)
                        + IFNULL(DocumnetCare, 0)
                        + IFNULL(MTD, 0)
                        + IFNULL(VAS, 0)
                        + IFNULL(IntlDox, 0)
                        + IFNULL(IntlEconomy, 0)
                        + IFNULL(IntlParcel, 0)
                        + IFNULL(ONUpto1kg, 0)
                        + IFNULL(ONAbove1kg, 0)
                        + IFNULL(ONUpto1kgRetailCOD, 0)
                        + IFNULL(ONAbove1kgRetailCOD, 0)
                        + IFNULL(EconomyRetail, 0)
                        + IFNULL(YB1KGRetail, 0)
                        + IFNULL(YB2KGRetail, 0)
                        + IFNULL(YB5KGRetail, 0)
                        + IFNULL(YB10KGRetail, 0)
                        + IFNULL(YB15KGRetail, 0)
                        + IFNULL(YB25KGRetail, 0)
                        + IFNULL(MyCollect, 0)
                        + IFNULL(Attestation, 0)
                        + IFNULL(CEB_UpTo_2Kg, 0)
                        + IFNULL(CEB_Above_2Kg, 0)
                        + IFNULL(Cor_Economy_Booking, 0)
                        + IFNULL(Cor_Ole_Booking, 0)
                        + IFNULL(CEB_Upto_2KG_Exis, 0)
                        + IFNULL(CEB_Upto_2KG_New, 0)
                        + IFNULL(CEB_Above_2Kg_Exis, 0)
                        + IFNULL(CEB_Above_2Kg_New, 0)
                        + IFNULL(ECON_Credit_Booking_Exis, 0)
                        + IFNULL(ECON_Credit_Booking_New, 0)
                        + IFNULL(OLE_CORP_Booking_Exis, 0)
                        + IFNULL(OLE_CORP_Booking_New, 0)
                        + IFNULL(Project_Local_Exis, 0)
                        + IFNULL(Project_Local_New, 0)
                        + IFNULL(Project_Domestic_Exis, 0)
                        + IFNULL(Project_Domestic_New, 0)
                        + IFNULL(CASH_EXP_BKG_UpTo_2Kg, 0)
                        + IFNULL(CASH_EXP_BKG_Above_2Kg, 0)
                        + IFNULL(CASH_Leop_BOX_Above_2Kg, 0)
                        + IFNULL(CASH_Economy_Booking, 0)
                        + IFNULL(CASH_OLE_Booking, 0)
                        + IFNULL(Cod_Bonus, 0)
                        - IFNULL(Cod_Deduction, 0)
                        + IFNULL(Insurance_Com, 0)
                        + IFNULL(Credit_Debit_Card, 0)
                        + IFNULL(ECommerce_Zero_COD, 0)
                        + IFNULL(Passport, 0)
                        + IFNULL(CNIC_Card, 0)
                        + IFNULL(Return_E_Com, 0)
                        + IFNULL(Pickup_Leopard, 0)
                      )), 0)
                    + (
                        SELECT IFNULL(SUM(Amount), 0)
                        FROM hr_empcommadjdtl
                        WHERE Year = @Year
                          AND Month = @Month
                          AND Emp_No = @EmpNo
                      )
                    - IFNULL(SUM(Retail_Deduction), 0)
                  FROM hr_commissionprocess
                  WHERE Year = @Year
                    AND Month = @Month
                    AND emp_no = @EmpNo",
                new { Year = year, Month = month, EmpNo = empNo },
                transaction);
        }

        private static SettlementDetailRow CreateDetailRow(
            SettlementMonthRequest request,
            string empNo,
            string description,
            decimal amount,
            string? deductionType,
            string type)
        {
            return new SettlementDetailRow
            {
                SalaryYear = request.SalaryYear,
                SalaryMonth = request.SalaryMonth,
                EmpNo = empNo,
                Description = description,
                Amount = amount,
                DeductionType = deductionType,
                Type = type,
                AllowCode = null,
                GlCode = null
            };
        }

        private async Task InsertLoanDeductionsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            List<LoanDeductionRow> loanDeductions)
        {
            if (loanDeductions.Count == 0)
            {
                return;
            }

            const string query = @"
INSERT INTO hr_employeeloandeduction
(
    LDed_No,
    LD_No,
    Emp_No,
    LoanCode,
    DeductionDate,
    DeductionAmount,
    Balance,
    Comments,
    CreatedBy,
    Created_Date,
    UpdatedBy,
    Updated_Date
)
VALUES
(
    @LoanDeductionCode,
    @LoanDisbursedId,
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
);";

            await connection.ExecuteAsync(query, loanDeductions, transaction);
        }

        private async Task InsertSettlementHeaderAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SettlementHeaderRow header)
        {
            const string query = @"
INSERT INTO hr_salaryprocessed_hdr
(
    SalaryYear,
    SalaryMonth,
    Emp_No,
    Dept,
    SalaryProcessedDate,
    BasicSalary,
    WorkedDays,
    currentsalary,
    AbsentDays,
    RAbsentDays,
    Absent_amt,
    OT_Amount,
    PT_Amount,
    extra_hours,
    extra_hours_amt,
    extra_days,
    extra_days_amt,
    extra_fuel,
    extra_fuel_amt,
    Extra_amount,
    Fuel_pday,
    Fuel_days,
    Fuel_Amount,
    CommAmount,
    Allowances,
    deductions,
    Loan,
    loan_balance,
    Advance,
    Tax,
    GrossPay,
    Total_Deduction,
    NetPay,
    CashPayment,
    amount_bank,
    amount_cash,
    Payment_Mode,
    Comments,
    CreatedBy,
    Created_Date,
    UpdatedBy,
    Updated_Date,
    createdStation,
    CreatedLocation,
    Type
)
VALUES
(
    @SalaryYear,
    @SalaryMonth,
    @EmpNo,
    @Dept,
    @SalaryProcessedDate,
    @BasicSalary,
    @WorkedDays,
    @CurrentSalary,
    @AbsentDays,
    @RuleAbsentDays,
    @AbsentAmount,
    @OTAmount,
    @PTAmount,
    @ExtraHours,
    @ExtraHoursAmount,
    @ExtraDays,
    @ExtraDaysAmount,
    @ExtraFuel,
    @ExtraFuelAmount,
    @ExtraAmount,
    @FuelPerDay,
    @FuelDays,
    @FuelAmount,
    @CommAmount,
    @Allowances,
    @Deductions,
    @Loan,
    @LoanBalance,
    @Advance,
    @Tax,
    @GrossPay,
    @TotalDeduction,
    @NetPay,
    @CashPayment,
    @AmountBank,
    @AmountCash,
    @PaymentMode,
    @Comments,
    @CreatedBy,
    @CreatedDate,
    @UpdatedBy,
    @UpdatedDate,
    @CreatedStation,
    @CreatedLocation,
    @Type
);";

            await connection.ExecuteAsync(query, header, transaction);
        }

        private async Task InsertSettlementDetailsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            List<SettlementDetailRow> details)
        {
            if (details.Count == 0)
            {
                return;
            }

            const string query = @"
INSERT INTO hr_salaryprocessed_dtl
(
    SalaryYear,
    SalaryMonth,
    emp_no,
    Description,
    Amount,
    Deduction_Type,
    type,
    Allow_Code,
    glcode
)
VALUES
(
    @SalaryYear,
    @SalaryMonth,
    @EmpNo,
    @Description,
    @Amount,
    @DeductionType,
    @Type,
    @AllowCode,
    @GlCode
);";

            await connection.ExecuteAsync(query, details, transaction);
        }

        private async Task<FinalSettlementPreviewBaseline> CapturePreviewBaselineAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SettlementPreparation preparation)
        {
            var baseline = new FinalSettlementPreviewBaseline();

            foreach (var request in preparation.MonthRequests)
            {
                string loanComment = $"From Salary of {request.MonthName} {request.SalaryYear}";
                baseline.Months.Add(new FinalSettlementPreviewBaselineMonth
                {
                    SalaryYear = request.SalaryYear,
                    SalaryMonth = request.SalaryMonth,
                    SalaryHeaderCount = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*)
                          FROM hr_salaryprocessed_hdr
                          WHERE Emp_No = @EmpNo
                            AND SalaryYear = @SalaryYear
                            AND SalaryMonth = @SalaryMonth",
                        new
                        {
                            preparation.Employee.EmpNo,
                            request.SalaryYear,
                            request.SalaryMonth
                        },
                        transaction),
                    SalaryDetailCount = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*)
                          FROM hr_salaryprocessed_dtl
                          WHERE Emp_No = @EmpNo
                            AND SalaryYear = @SalaryYear
                            AND SalaryMonth = @SalaryMonth",
                        new
                        {
                            preparation.Employee.EmpNo,
                            request.SalaryYear,
                            request.SalaryMonth
                        },
                        transaction),
                    LoanDeductionCount = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*)
                          FROM hr_employeeloandeduction
                          WHERE Emp_No = @EmpNo
                            AND Comments = @Comments",
                        new
                        {
                            preparation.Employee.EmpNo,
                            Comments = loanComment
                        },
                        transaction)
                });
            }

            return baseline;
        }

        private async Task<bool> VerifyPreviewBaselineAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SettlementPreparation preparation,
            FinalSettlementPreviewBaseline baseline)
        {
            foreach (var month in baseline.Months)
            {
                string monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month.SalaryMonth);
                string loanComment = $"From Salary of {monthName} {month.SalaryYear}";

                int salaryHeaderCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM hr_salaryprocessed_hdr
                      WHERE Emp_No = @EmpNo
                        AND SalaryYear = @SalaryYear
                        AND SalaryMonth = @SalaryMonth",
                    new
                    {
                        preparation.Employee.EmpNo,
                        month.SalaryYear,
                        month.SalaryMonth
                    },
                    transaction);

                int salaryDetailCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM hr_salaryprocessed_dtl
                      WHERE Emp_No = @EmpNo
                        AND SalaryYear = @SalaryYear
                        AND SalaryMonth = @SalaryMonth",
                    new
                    {
                        preparation.Employee.EmpNo,
                        month.SalaryYear,
                        month.SalaryMonth
                    },
                    transaction);

                int loanDeductionCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM hr_employeeloandeduction
                      WHERE Emp_No = @EmpNo
                        AND Comments = @Comments",
                    new
                    {
                        preparation.Employee.EmpNo,
                        Comments = loanComment
                    },
                    transaction);

                if (salaryHeaderCount != month.SalaryHeaderCount ||
                    salaryDetailCount != month.SalaryDetailCount ||
                    loanDeductionCount != month.LoanDeductionCount)
                {
                    return false;
                }
            }

            return true;
        }

        private static SettlementMonthRequest CreateMonthRequest(DateTime monthDate, int inputWorkingDays)
        {
            return new SettlementMonthRequest(
                monthDate.Year,
                monthDate.Month,
                monthDate.ToString("MMMM", CultureInfo.InvariantCulture),
                new DateTime(monthDate.Year, monthDate.Month, DateTime.DaysInMonth(monthDate.Year, monthDate.Month)),
                inputWorkingDays);
        }

        private static int GetPayrollPeriodDayCount(int year, int month)
        {
            var (fromDate, toDate) = GetPayrollPeriod(year, month);
            return (toDate - fromDate).Days + 1;
        }

        private static (DateTime fromDate, DateTime toDate) GetPayrollPeriod(int year, int month)
        {
            return month - 1 == 0
                ? (new DateTime(year - 1, 12, 26), new DateTime(year, month, 25))
                : (new DateTime(year, month - 1, 26), new DateTime(year, month, 25));
        }

        private static decimal GetFuelPrice(IEnumerable<FuelPrice> fuelPrices, string? fuelCode)
        {
            if (string.IsNullOrWhiteSpace(fuelCode))
            {
                return 0m;
            }

            return fuelPrices.LastOrDefault(x => string.Equals(x.Type, fuelCode, StringComparison.OrdinalIgnoreCase))?.Price ?? 0m;
        }

        private sealed record SettlementPreparation(SettlementEmployeeContext Employee, List<SettlementMonthRequest> MonthRequests);
        private sealed record SettlementMonthRequest(int SalaryYear, int SalaryMonth, string MonthName, DateTime SalaryDate, int InputWorkingDays);
        private sealed record SettlementMonthComputation(SettlementHeaderRow Header, List<SettlementDetailRow> Details, List<LoanDeductionRow> LoanDeductions, FinalSettlementMonthPreview Preview);

        private sealed class SettlementEmployeeContext
        {
            public string EmpNo { get; set; } = string.Empty;
            public string EmployeeName { get; set; } = string.Empty;
            public DateTime TerminationDate { get; set; }
            public string SettlementFlag { get; set; } = string.Empty;
            public string EmployeeStatus { get; set; } = string.Empty;
            public string CityCode { get; set; } = string.Empty;
            public string DeptCode { get; set; } = string.Empty;
            public int? StationId { get; set; }
            public int? BranchLocation { get; set; }
        }

        private sealed class SalaryBaseRow
        {
            public decimal BasicSalary { get; set; }
            public decimal CashAmount { get; set; }
            public string? PaymentMode { get; set; }
        }

        private sealed class SalarySnapshot
        {
            public decimal BasicSalary { get; set; }
            public decimal CashAmount { get; set; }
            public string PaymentMode { get; set; } = string.Empty;
        }

        private sealed class IncrementSummaryRow
        {
            public string Type { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        private sealed class SettlementAttendanceSnapshot
        {
            public int Absents { get; set; }
            public int Sundays { get; set; }
            public int Holidays { get; set; }
            public int Leaves { get; set; }
            public int RuleAbsents { get; set; }
            public int AdjustmentAbsent { get; set; }
            public int AdjustmentRuleAbsent { get; set; }
        }

        private sealed class ShiftTiming
        {
            public TimeSpan? StartTime { get; set; }
            public TimeSpan? EndTime { get; set; }
        }

        private sealed class FuelPrice
        {
            public string Type { get; set; } = string.Empty;
            public decimal Price { get; set; }
        }

        private sealed class FuelDefinition
        {
            public string FCode { get; set; } = string.Empty;
            public decimal FuelPerDay { get; set; }
            public string FuelMode { get; set; } = string.Empty;
        }

        private sealed class MonthlyExtraRow
        {
            public int ExtraTypeId { get; set; }
            public decimal Amount { get; set; }
            public string GlCode { get; set; } = string.Empty;
            public bool IsTaxable { get; set; }
        }

        private sealed class AllowanceDefinition
        {
            public string Code { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public decimal PctAmount { get; set; }
            public decimal FixAmount { get; set; }
            public string ExcludeAbsent { get; set; } = string.Empty;
            public string GlCode { get; set; } = string.Empty;

            public decimal Pct_Amount
            {
                get => PctAmount;
                set => PctAmount = value;
            }

            public decimal Fix_Amount
            {
                get => FixAmount;
                set => FixAmount = value;
            }

            public string exclude_absent
            {
                get => ExcludeAbsent;
                set => ExcludeAbsent = value;
            }
        }

        private sealed class LoanDisbursement
        {
            public string LoanDisbursedId { get; set; } = string.Empty;
            public string LoanCode { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public DateTime DisbursedDate { get; set; }
            public decimal DisbursedAmount { get; set; }
            public decimal DeductionInstallments { get; set; }
            public DateTime DeductionStartDate { get; set; }
            public decimal Paid { get; set; }
        }

        private sealed class PenaltyFineRow
        {
            public decimal Amount { get; set; }
            public string Remarks { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
        }

        private sealed class LoanDeductionRow
        {
            public string LoanDeductionCode { get; set; } = string.Empty;
            public string LoanDisbursedId { get; set; } = string.Empty;
            public string EmpNo { get; set; } = string.Empty;
            public string LoanCode { get; set; } = string.Empty;
            public DateTime DeductionDate { get; set; }
            public decimal DeductionAmount { get; set; }
            public decimal Balance { get; set; }
            public string Comments { get; set; } = string.Empty;
            public string CreatedBy { get; set; } = string.Empty;
            public DateTime CreatedDate { get; set; }
            public string UpdatedBy { get; set; } = string.Empty;
            public DateTime UpdatedDate { get; set; }
        }

        private sealed class SettlementHeaderRow
        {
            public int SalaryYear { get; set; }
            public int SalaryMonth { get; set; }
            public string EmpNo { get; set; } = string.Empty;
            public string Dept { get; set; } = string.Empty;
            public DateTime SalaryProcessedDate { get; set; }
            public decimal BasicSalary { get; set; }
            public int WorkedDays { get; set; }
            public decimal CurrentSalary { get; set; }
            public int AbsentDays { get; set; }
            public int RuleAbsentDays { get; set; }
            public decimal AbsentAmount { get; set; }
            public decimal OTAmount { get; set; }
            public decimal PTAmount { get; set; }
            public decimal ExtraHours { get; set; }
            public decimal ExtraHoursAmount { get; set; }
            public decimal ExtraDays { get; set; }
            public decimal ExtraDaysAmount { get; set; }
            public decimal ExtraFuel { get; set; }
            public decimal ExtraFuelAmount { get; set; }
            public decimal ExtraAmount { get; set; }
            public decimal FuelPerDay { get; set; }
            public int FuelDays { get; set; }
            public decimal FuelAmount { get; set; }
            public decimal CommAmount { get; set; }
            public decimal Allowances { get; set; }
            public decimal Deductions { get; set; }
            public decimal Loan { get; set; }
            public decimal LoanBalance { get; set; }
            public decimal Advance { get; set; }
            public decimal Tax { get; set; }
            public decimal GrossPay { get; set; }
            public decimal TotalDeduction { get; set; }
            public decimal NetPay { get; set; }
            public decimal CashPayment { get; set; }
            public decimal AmountBank { get; set; }
            public decimal AmountCash { get; set; }
            public string PaymentMode { get; set; } = string.Empty;
            public string Comments { get; set; } = string.Empty;
            public string CreatedBy { get; set; } = string.Empty;
            public DateTime CreatedDate { get; set; }
            public string UpdatedBy { get; set; } = string.Empty;
            public DateTime UpdatedDate { get; set; }
            public int? CreatedStation { get; set; }
            public int? CreatedLocation { get; set; }
            public string Type { get; set; } = string.Empty;
        }

        private sealed class SettlementDetailRow
        {
            public int SalaryYear { get; set; }
            public int SalaryMonth { get; set; }
            public string EmpNo { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public string? DeductionType { get; set; }
            public string Type { get; set; } = string.Empty;
            public string? AllowCode { get; set; }
            public string? GlCode { get; set; }
        }

        private sealed class FinalSettlementPreviewBaseline
        {
            public List<FinalSettlementPreviewBaselineMonth> Months { get; set; } = new();
        }

        private sealed class FinalSettlementPreviewBaselineMonth
        {
            public int SalaryYear { get; set; }
            public int SalaryMonth { get; set; }
            public int SalaryHeaderCount { get; set; }
            public int SalaryDetailCount { get; set; }
            public int LoanDeductionCount { get; set; }
        }
    }
}
