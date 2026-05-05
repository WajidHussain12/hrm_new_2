using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    internal sealed partial class LegacySalaryProcessEngine
    {
        private void ProcessLoan(string empNo)
        {
            _loan = 0m;
            _loanBalance = 0m;

            DataTable loans = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT LD_No, Emp_No, loancode, l.fullname, DisbursedDate, DisbursedAmount, DeductionInstallments, DeductionStartDate,
                         (SELECT IFNULL(SUM(DeductionAmount),0)
                          FROM hr_employeeloandeduction b
                          WHERE b.emp_no = a.Emp_No
                            AND b.ld_no = a.LD_No) paid
                  FROM hr_employeeloandisbursed a
                  INNER JOIN hr_loantypes l ON a.LoanCode = l.Code
                  WHERE a.emp_no = @EmpNo
                    AND a.LoanCode <> '009'
                    AND a.DeductionStartDate <= @SalaryDate",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@SalaryDate", _salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)));

            foreach (DataRow row in loans.Rows)
            {
                decimal disbursedAmount = Convert.ToDecimal(row["DisbursedAmount"]);
                decimal paid = Convert.ToDecimal(row["paid"]);
                if (disbursedAmount <= paid)
                {
                    continue;
                }

                _loanDeductionCode = _loanCodeSeed.ToString("0000000000", CultureInfo.InvariantCulture);
                decimal installmentAmount = Convert.ToInt32(row["DeductionInstallments"]) > 0
                    ? disbursedAmount / Convert.ToDecimal(row["DeductionInstallments"])
                    : disbursedAmount;

                decimal balance = disbursedAmount - paid;
                if (installmentAmount > balance)
                {
                    installmentAmount = balance;
                }

                _loan += installmentAmount;
                _loanBalance += disbursedAmount - (paid + installmentAmount);

                _connection.Execute(
                    @"INSERT INTO hr_employeeloandeduction
                      VALUES (
                          @LDedNo, @LDNo, @EmpNo, @LoanCode, @DeductionDate, @DeductionAmount, @Balance, @Comments,
                          @CreatedBy, @CreatedDate, @UpdatedBy, @UpdatedDate
                      );",
                    new
                    {
                        LDedNo = _loanDeductionCode,
                        LDNo = row["LD_No"],
                        EmpNo = empNo,
                        LoanCode = row["loancode"],
                        DeductionDate = DateTime.Now,
                        DeductionAmount = installmentAmount,
                        Balance = disbursedAmount - (paid + installmentAmount),
                        Comments = GetLoanComment(),
                        CreatedBy = _currentUserId,
                        CreatedDate = DateTime.Now,
                        UpdatedBy = _currentUserId,
                        UpdatedDate = DateTime.Now
                    },
                    _transaction);

                _loanDeductionRowsInserted++;
                _loanCodeSeed++;
            }
        }

        private void ProcessAdvanceSalary(string empNo)
        {
            _advanceSalary = 0m;
            DataTable advances = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT Amount
                  FROM hr_advance_salary
                  WHERE emp_no = @EmpNo
                    AND YEAR = @Year
                    AND MONTH = @Month
                    AND STATUS = 'A'",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@Year", _model.Year),
                new MySqlParameter("@Month", _model.Month));

            foreach (DataRow row in advances.Rows)
            {
                _advanceSalary += Convert.ToDecimal(row["Amount"]);
            }
        }

        private void ProcessPenaltyFine(string empNo)
        {
            _penaltyDeduction = 0m;
            DataTable penalties = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT hpf.amount, hpf.Remarks, hpf.Type
                  FROM hr_penalty_fine hpf
                  WHERE DATE(hpf.FineDate) BETWEEN @FromDate AND @ToDate
                    AND hpf.code = @EmpNo",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@FromDate", _from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new MySqlParameter("@ToDate", _to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

            foreach (DataRow row in penalties.Rows)
            {
                _penaltyDeduction += Convert.ToDecimal(row["amount"]);
                AddSalaryDetailRow(empNo, "Penalty/Fine: " + row["Remarks"], Convert.ToDecimal(row["amount"]), "D", row["Type"]?.ToString() ?? string.Empty, string.Empty, "149");
            }
        }

        private void ProcessCommission(string empNo)
        {
            _commissionAmount = 0m;
            _codKpiBonus = 0m;
            _codKpiDeduction = 0m;

            DataTable stickerRows = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT CommType, SUM(Amount) Amount
                  FROM hr_employeecommissiondetails
                  WHERE Emp_No = @EmpNo
                    AND year = @Year
                    AND MONTH = @Month
                  GROUP BY CommType",
                new MySqlParameter("@Year", _model.Year),
                new MySqlParameter("@Month", _model.Month),
                new MySqlParameter("@EmpNo", empNo));

            foreach (DataRow row in stickerRows.Rows)
            {
                decimal amount = Convert.ToDecimal(row["Amount"]);
                _commissionAmount += amount;
                AddSalaryDetailRow(
                    empNo,
                    string.Equals(row["CommType"]?.ToString(), "D", StringComparison.OrdinalIgnoreCase) ? "Stickers-Domestic" : "Stickers-Local",
                    amount,
                    "A",
                    @"\N",
                    string.Empty,
                    "172");
            }

            List<string> routeIds = _connection.Query<string>(
                @"SELECT r.RouteCode
                  FROM hr_employeeroutecode r
                  WHERE r.Emp_No = @EmpNo
                    AND r.citycode = @CityCode
                    AND r.ToDate IS NULL
                    AND r.Codetype NOT IN (3);",
                new { EmpNo = empNo, CityCode = _cityCode },
                _transaction,
                commandTimeout: 300).ToList();

            if (routeIds.Count > 0)
            {
                var codKpiRows = _connection.Query<CODKPIModel>(
                    @"SELECT cd.Cour_id, cd.CODBonus AS CODBonus, cd.CODDeduction AS CODDeduction
                      FROM hr_codcommission cd
                      WHERE cd.month = @Month
                        AND cd.year = @Year
                        AND cd.City = @CityCode
                        AND cd.Cour_id IN @RouteIds;",
                    new { Month = _model.Month, Year = _model.Year, CityCode = _cityCode, RouteIds = routeIds },
                    _transaction,
                    commandTimeout: 300).ToList();

                foreach (var row in codKpiRows)
                {
                    if (row.CODBonus <= 0 && row.CODDeduction <= 0)
                    {
                        continue;
                    }

                    bool isBonus = row.CODBonus > 0;
                    _codKpiBonus += row.CODBonus;
                    _codKpiDeduction += row.CODDeduction;
                    AddSalaryDetailRow(empNo, isBonus ? "COD KPI Incentive" : "COD KPI Deduction", isBonus ? row.CODBonus : row.CODDeduction, isBonus ? "A" : "D", isBonus ? "23" : "8", string.Empty, isBonus ? "499" : "149");
                }
            }

            decimal processedCommission = _connection.ExecuteScalar<decimal>(
                CommissionSql,
                new { Year = _model.Year, Month = _model.Month, EmpNo = empNo },
                _transaction);

            _commissionAmount += processedCommission;
            if (processedCommission != 0)
            {
                AddSalaryDetailRow(empNo, "Commission", _commissionAmount, "A", @"\N", string.Empty, "172");
            }
        }

        private void ProcessFuel(string empNo)
        {
            decimal fuelPerDay = 0m;
            _fuelPerDay = 0m;
            _fuelAmount = 0m;
            _extraFuelAmount = 0m;
            string fuelMode = string.Empty;

            DataTable fuelRows = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT Fcode, Emp_No, Fuel_Pday, Fuel_mode, FromDate
                  FROM hr_employeefueldetails
                  WHERE @SalaryDate BETWEEN fromdate AND IFNULL(todate,'2099-08-28')
                    AND emp_no = @EmpNo
                  ORDER BY CODE DESC
                  LIMIT 1",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@SalaryDate", _salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)));

            if (fuelRows.Rows.Count == 0)
            {
                if (_extraFuel > 0)
                {
                    _extraFuelAmount = _extraFuel * _fuelAvrgPrice1;
                }

                return;
            }

            foreach (DataRow row in fuelRows.Rows)
            {
                fuelPerDay = Convert.ToDecimal(row["Fuel_Pday"]);
                fuelMode = row["Fuel_mode"]?.ToString() ?? string.Empty;

                int[] presentDays = GetPresentDays(empNo);
                int fuelDayCounter = 0;
                foreach (int presentDay in presentDays)
                {
                    int tempMonth = presentDay is 26 or 27 or 28 or 29 or 30 or 31 ? (_model.Month - 1 == 0 ? 12 : _model.Month - 1) : _model.Month;
                    int tempYear = presentDay is 26 or 27 or 28 or 29 or 30 or 31 ? (_model.Month - 1 == 0 ? _model.Year - 1 : _model.Year) : _model.Year;
                    DateTime day = new(tempYear, tempMonth, presentDay);

                    if (day.DayOfWeek == DayOfWeek.Sunday || !presentDays.Contains(day.Day))
                    {
                        continue;
                    }

                    if (string.Equals(fuelMode, "PD", StringComparison.OrdinalIgnoreCase))
                    {
                        DataTable dayFuel = DAL.ExecuteDataTable(
                            _connection,
                            CommandType.Text,
                            $@"SELECT DATE(FromDate) AS FromDate, IF(ToDate IS NULL,'{_to:yyyy-MM-dd}',DATE(ToDate)) AS ToDate, Price
                               FROM hr_fuelprices
                               WHERE (fromdate <= '{day:yyyy-MM-dd}' AND Todate >= '{day:yyyy-MM-dd}') OR ToDate IS NULL AND TYPE='002'
                               ORDER BY CODE
                               LIMIT 1;");

                        if (dayFuel.Rows.Count > 0)
                        {
                            _fuelAmount += fuelPerDay * Convert.ToDecimal(dayFuel.Rows[0]["Price"]);
                        }
                    }

                    fuelDayCounter++;
                }

                _totalFuelDays = fuelDayCounter;
                _fuelDays = fuelDayCounter;

                if (string.Equals(fuelMode, "PD", StringComparison.OrdinalIgnoreCase))
                {
                    _fuelPerDay += fuelPerDay;
                }
                else if (string.Equals(fuelMode, "FL", StringComparison.OrdinalIgnoreCase))
                {
                    int[] daysInMonth = _appointmentDate.Month == _model.Month && _appointmentDate.Year == _model.Year
                        ? LCS.GetListOfDayss(_to, _appointmentDate)
                        : LCS.GetListOfDayss(_to, _from);

                    decimal monthlyFuel = fuelPerDay / 31m;
                    foreach (int dayNumber in daysInMonth)
                    {
                        int tempMonth = dayNumber is 26 or 27 or 28 or 29 or 30 or 31 ? (_model.Month - 1 == 0 ? 12 : _model.Month - 1) : _model.Month;
                        int tempYear = dayNumber is 26 or 27 or 28 or 29 or 30 or 31 ? (_model.Month - 1 == 0 ? _model.Year - 1 : _model.Year) : _model.Year;
                        DateTime currentDay = new(tempYear, tempMonth, dayNumber);

                        DataTable fuelPrice = DAL.ExecuteDataTable(
                            _connection,
                            CommandType.Text,
                            $@"SELECT DATE(FromDate) AS FromDate, IF(ToDate IS NULL,'{_to:yyyy-MM-dd}',DATE(ToDate)) AS ToDate, Price
                               FROM hr_fuelprices
                               WHERE (fromdate <= '{currentDay:yyyy-MM-dd}' AND Todate >= '{currentDay:yyyy-MM-dd}') OR ToDate IS NULL AND TYPE='002'
                               ORDER BY CODE
                               LIMIT 1;");

                        if (fuelPrice.Rows.Count > 0)
                        {
                            _fuelAmount += monthlyFuel * Convert.ToDecimal(fuelPrice.Rows[0]["Price"]);
                        }
                    }
                }
                else if (string.Equals(fuelMode, "FA", StringComparison.OrdinalIgnoreCase))
                {
                    if (_absents + _holidays + _sundays + _ruleAbsents >= 30 || _absents + _holidays + _sundays + _ruleAbsents > 28)
                    {
                        _fuelDays = 0;
                    }
                    else
                    {
                        _fuelDays += _sundays + _holidays;
                        _fuelDays = _fuelDays > 30 ? 31 : _fuelDays;
                        _totalFuelDays = _fuelDays;
                    }

                    _fuelAmount = (fuelPerDay / 31m) * 31m;
                }
                else
                {
                    if (_absents + _holidays + _sundays + _ruleAbsents >= 30 || _absents + _holidays + _sundays + _ruleAbsents > 28)
                    {
                        _fuelDays = 0;
                    }
                    else
                    {
                        _fuelDays += _sundays + _holidays;
                    }

                    try
                    {
                        _fuelAmount += (fuelPerDay / _totalFuelDays) * _fuelDays;
                        fuelPerDay = (fuelPerDay / _totalFuelDays) / _fuelAvrgPrice;
                    }
                    catch (DivideByZeroException)
                    {
                        fuelPerDay = 0m;
                    }

                    _fuelPerDay += fuelPerDay;
                }
            }

            if (_extraFuel > 0)
            {
                _extraFuelAmount = _extraFuel * _fuelAvrgPrice1;
            }

            if (_extraDays > 0)
            {
                _extraFuelAmount = _extraFuelAmount > 0 && _extraFuel > 0 ? _extraFuelAmount : 0m;

                if (string.Equals(fuelMode, "FA", StringComparison.OrdinalIgnoreCase))
                {
                    _extraFuelAmount += _extraDays * (fuelPerDay / 31m);
                }
                else if (string.Equals(fuelMode, "PD", StringComparison.OrdinalIgnoreCase))
                {
                    _extraFuelAmount += (_extraDays * fuelPerDay) * _fuelAvrgPrice1;
                }
                else if (string.Equals(fuelMode, "FL", StringComparison.OrdinalIgnoreCase))
                {
                    _extraFuelAmount += _extraDays * fuelPerDay * _fuelAvrgPrice1;
                }
            }
        }

        private int[] GetPresentDays(string empNo)
        {
            var presentDays = new List<int>();
            DataTable attendanceDays = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT DISTINCT att.Day
                  FROM hr_employeeattendance att
                  WHERE DATE(att.CHECKTIME) BETWEEN @FromDate AND @ToDate
                    AND att.emp_no = @EmpNo
                    AND att.Status = 'N'
                  UNION
                  SELECT DISTINCT DAY(adju.adjustmentDate) DAY
                  FROM hr_employeeattandenceadjust adju
                  WHERE DATE(adju.adjustmentDate) BETWEEN @FromDate AND @ToDate
                    AND adju.emp_no = @EmpNo;",
                new MySqlParameter("@FromDate", _from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new MySqlParameter("@ToDate", _to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new MySqlParameter("@EmpNo", empNo));

            foreach (DataRow day in attendanceDays.Rows)
            {
                presentDays.Add(Convert.ToInt32(day["Day"]));
            }

            return presentDays.ToArray();
        }

        private void ProcessFuelCardAmountPerDay(string empNo)
        {
            _fuelCardUsage = 0m;
            _fuelCardQtyUsage = 0m;

            DataTable cardRows = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                $@"SELECT f.Emp_No, f.FuelCardNo, f.FromDate, f.ToDate
                   FROM hr_fuelcarddetail f
                   WHERE f.emp_no = @EmpNo
                     AND (f.FromDate BETWEEN '2020-05-26' AND '{_tmpEndDate:yyyy-MM-dd}' AND MONTH(f.ToDate) = @Month AND YEAR(f.ToDate) = @Year)
                   UNION
                   SELECT f.Emp_No, f.FuelCardNo, f.FromDate, f.ToDate
                   FROM hr_fuelcarddetail f
                   WHERE f.emp_no = @EmpNo
                     AND (f.FromDate BETWEEN '2020-05-26' AND '{_tmpEndDate:yyyy-MM-dd}' OR f.ToDate IS NULL)",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@Year", _model.Year),
                new MySqlParameter("@Month", _model.Month));

            foreach (DataRow row in cardRows.Rows)
            {
                string cardNo = row["FuelCardNo"]?.ToString() ?? string.Empty;
                string cardFromDate = Convert.ToDateTime(row["FromDate"]) < _tmpStartDate ? _tmpStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Convert.ToDateTime(row["FromDate"]).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string cardToDate = row["ToDate"] == DBNull.Value ? _tmpEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Convert.ToDateTime(row["ToDate"]).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                DataTable usage = DAL.ExecuteDataTable(
                    _connection,
                    CommandType.Text,
                    $@"SELECT IFNULL(SUM(ft.Quantity),0) AS GrossLiters, IFNULL(SUM(GrossAmount),0) AS GrossAmount
                       FROM hr_fuel_transaction ft
                       WHERE ft.CardNumber = '{cardNo}'
                         AND DeliveryDate BETWEEN '{cardFromDate}' AND '{cardToDate}';");

                if (usage.Rows.Count > 0)
                {
                    _fuelCardUsage += Convert.ToDecimal(usage.Rows[0]["GrossAmount"]);
                    _fuelCardQtyUsage += Convert.ToDecimal(usage.Rows[0]["GrossLiters"]);
                }
            }
        }

        private const string CommissionSql = @"
SELECT
COALESCE(SUM((
IFNULL(DOM_CREDIT,0)+IFNULL(LOCAL_CREDIT,0)+IFNULL(LOCAL_DLD,0)+IFNULL(DomesticDelivery,0)+IFNULL(INTL_CREDIT,0)+IFNULL(COD,0)+IFNULL(OVERNIGHT,0)+IFNULL(YB1KG,0)+IFNULL(YB2KG,0)+IFNULL(YB5KG,0)+IFNULL(YB10KG,0)+IFNULL(YB15KG,0)+IFNULL(YB25KG,0)+IFNULL(FLAYER,0)+IFNULL(DETAIN,0)+IFNULL(OVERLAND,0)+IFNULL(PREPAID,0)+IFNULL(LOVELINE,0)+IFNULL(INTL_CASH,0)+IFNULL(OLE_Credit_Booking,0)+IFNULL(OLE_Dispatch_Proper,0)+IFNULL(OLE_Transit_Dispatch,0)+IFNULL(OLE_Delivery_OPS,0)+IFNULL(OLE_Delivery,0)+IFNULL(MOFA_OTO,0)+IFNULL(MOFA_OTD,0)+IFNULL(Rms_Cod_Booking,0)+IFNULL(AllInOne,0)+IFNULL(DocumnetCare,0)+IFNULL(MTD,0)+IFNULL(VAS,0)+IFNULL(IntlDox,0)+IFNULL(IntlEconomy,0)+IFNULL(IntlParcel,0)+IFNULL(ONUpto1kg,0)+IFNULL(ONAbove1kg,0)+IFNULL(ONUpto1kgRetailCOD,0)+IFNULL(ONAbove1kgRetailCOD,0)+IFNULL(EconomyRetail,0)+IFNULL(YB1KGRetail,0)+IFNULL(YB2KGRetail,0)+IFNULL(YB5KGRetail,0)+IFNULL(YB10KGRetail,0)+IFNULL(YB15KGRetail,0)+IFNULL(YB25KGRetail,0)+IFNULL(MyCollect,0)+IFNULL(Attestation,0)+IFNULL(CEB_UpTo_2Kg,0)+IFNULL(CEB_Above_2Kg,0)+IFNULL(Cor_Economy_Booking,0)+IFNULL(Cor_Ole_Booking,0)+IFNULL(CEB_Upto_2KG_Exis,0)+IFNULL(CEB_Upto_2KG_New,0)+IFNULL(CEB_Above_2Kg_Exis,0)+IFNULL(CEB_Above_2Kg_New,0)+IFNULL(ECON_Credit_Booking_Exis,0)+IFNULL(ECON_Credit_Booking_New,0)+IFNULL(OLE_CORP_Booking_Exis,0)+IFNULL(OLE_CORP_Booking_New,0)+IFNULL(Project_Local_Exis,0)+IFNULL(Project_Local_New,0)+IFNULL(Project_Domestic_Exis,0)+IFNULL(Project_Domestic_New,0)+IFNULL(CASH_EXP_BKG_UpTo_2Kg,0)+IFNULL(CASH_EXP_BKG_Above_2Kg,0)+IFNULL(CASH_Leop_BOX_Above_2Kg,0)+IFNULL(CASH_Economy_Booking,0)+IFNULL(CASH_OLE_Booking,0)+IFNULL(Cod_Bonus,0)-IFNULL(Cod_Deduction,0)+IFNULL(Insurance_Com,0)+IFNULL(Credit_Debit_Card,0)+IFNULL(ECommerce_Zero_COD,0)+IFNULL(Passport,0)+IFNULL(CNIC_Card,0)+IFNULL(Return_E_Com,0)+IFNULL(Pickup_Leopard,0)
)) + (SELECT IFNULL(SUM(Amount),0) FROM hr_empcommadjdtl WHERE YEAR = @Year AND MONTH = @Month AND Emp_no = @EmpNo), 0)
- IFNULL(SUM(Retail_Deduction),0) AS Commission
FROM hr_commissionprocess
WHERE YEAR = @Year
  AND MONTH = @Month
  AND emp_no = @EmpNo;";
    }

    internal sealed class CODKPIModel
    {
        public string Cour_id { get; set; } = string.Empty;
        public decimal CODBonus { get; set; }
        public decimal CODDeduction { get; set; }
    }
}
