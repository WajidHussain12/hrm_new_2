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
        private readonly MySqlConnection _connection;
        private readonly MySqlTransaction _transaction;
        private readonly SalariesProcessViewModel _model;
        private readonly string _currentUserId;
        private readonly string _currentUserRole;
        private readonly string _departmentId;
        private readonly IReadOnlyCollection<SalaryProcessEmployeeRow> _employees;
        private readonly DataTable _allowanceCatalog;
        private readonly DateTime _salaryDate;
        private readonly DateTime _from;
        private readonly DateTime _to;
        private readonly DateTime _tmpStartDate;
        private readonly DateTime _tmpEndDate;
        private readonly int _station;
        private readonly int _location;
        private readonly decimal _fuelAvrgPrice;
        private readonly decimal _fuelAvrgPrice1;
        private readonly List<TaxSlabList> _taxSlabList;
        private readonly string _cityCode;

        private DataSet _dataSet = new();
        private List<int> _holidaysCityWise = new();
        private int _loanCodeSeed;
        private int _loanDeductionRowsInserted;

        private int _workedDays;
        private int _absents;
        private int _ruleAbsents;
        private int _sundays;
        private int _holidays;
        private decimal _basicSalary;
        private decimal _currentSalary;
        private decimal _absentAmount;
        private decimal _cashAmount;
        private decimal _taxDeductOn;
        private decimal _fuelPerDay;
        private decimal _fuelAmount;
        private int _totalFuelDays;
        private int _fuelDays;
        private decimal _partTime;
        private decimal _commissionAmount;
        private decimal _grossPay;
        private decimal _extraDays;
        private decimal _extraDaysAmount;
        private decimal _extraHours;
        private decimal _extraHoursAmount;
        private decimal _extraFuel;
        private decimal _extraFuelAmount;
        private decimal _extraAmount;
        private decimal _extraAmountTaxable;
        private TimeSpan _shiftHours = TimeSpan.Zero;
        private decimal _allowance;
        private decimal _deduction;
        private decimal _penaltyDeduction;
        private decimal _fuelCardUsage;
        private decimal _fuelCardQtyUsage;
        private decimal _totalDeductions;
        private decimal _loan;
        private decimal _loanBalance;
        private decimal _advanceSalary;
        private decimal _tax;
        private decimal _amountBank;
        private decimal _amountCash;
        private string _loanDeductionCode = string.Empty;
        private string _paymentMode = string.Empty;
        private bool _fixedExtraDays;
        private int _fixedExtraDaysValue;
        private DateTime _appointmentDate;
        private decimal _codKpiBonus;
        private decimal _codKpiDeduction;
        private decimal _allowanceDeduction;

        public LegacySalaryProcessEngine(
            MySqlConnection connection,
            MySqlTransaction transaction,
            SalariesProcessViewModel model,
            string currentUserId,
            string currentUserRole,
            string departmentId,
            string departmentName,
            IReadOnlyCollection<SalaryProcessEmployeeRow> employees,
            DataTable allowanceCatalog,
            DateTime salaryDate,
            DateTime from,
            DateTime to,
            DateTime tmpStartDate,
            DateTime tmpEndDate,
            int station,
            int location,
            decimal fuelAvrgPrice,
            decimal fuelAvrgPrice1,
            List<TaxSlabList> taxSlabList,
            int loanCodeSeed,
            string cityCode)
        {
            _connection = connection;
            _transaction = transaction;
            _model = model;
            _currentUserId = currentUserId;
            _currentUserRole = currentUserRole;
            _departmentId = departmentId;
            _employees = employees;
            _allowanceCatalog = allowanceCatalog;
            _salaryDate = salaryDate;
            _from = from;
            _to = to;
            _tmpStartDate = tmpStartDate;
            _tmpEndDate = tmpEndDate;
            _station = station;
            _location = location;
            _fuelAvrgPrice = fuelAvrgPrice;
            _fuelAvrgPrice1 = fuelAvrgPrice1;
            _taxSlabList = taxSlabList;
            _loanCodeSeed = loanCodeSeed;
            _cityCode = cityCode;
        }

        public SalaryProcessPreparedData Execute()
        {
            _dataSet = LCS.GetDataTableSchema(_connection, "hr_salaryprocessed_hdr", "hr_salaryprocessed_dtl", "hr_penalty_fine");
            _holidaysCityWise = LCS.GetHolidaysCityWise(_connection, _model.Year, _model.Month, _cityCode, true);

            foreach (var employee in _employees)
            {
                _deduction = 0m;
                string empNo = employee.EmpNo;
                _appointmentDate = employee.AppointmentDate ?? DateTime.MinValue;

                if (!ProcessSalary(empNo))
                {
                    continue;
                }

                ProcessFixeddaysEncashment(_appointmentDate);
                Extras(empNo);
                ProcessPartTime(empNo);
                AllowanceDeduction(empNo);
                ProcessLoan(empNo);
                ProcessAdvanceSalary(empNo);
                ProcessPenaltyFine(empNo);
                ProcessCommission(empNo);
                ProcessFuel(empNo);
                ProcessFuelCardAmountPerDay(empNo);
                GetGrossPay(empNo);
                AddHeaderRow(empNo);
            }

            DataTable headerTable = _dataSet.Tables["hr_salaryprocessed_hdr"];
            var excludedEmpNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "00000000013203",
                "00000000013548",
                "00000000032787",
                "00000000032864",
                "00000000032837",
                "00000000032838",
                "00000000032857",
                "00000000032861",
                "00000000032865"
            };

            var taxScopeRows = headerTable.AsEnumerable()
                .Where(row => !excludedEmpNos.Contains(row.Field<string>("Emp_No") ?? string.Empty))
                .Select(row => new
                {
                    EmpNo = row.Field<string>("Emp_No") ?? string.Empty,
                    SalaryYear = row.Field<int>("SalaryYear"),
                    SalaryMonth = row.Field<int>("SalaryMonth"),
                    PaymentMode = row.Field<string>("Payment_Mode") ?? string.Empty
                })
                .Distinct()
                .ToList();

            return new SalaryProcessPreparedData
            {
                DataSet = _dataSet,
                PreparedHeaderRows = _dataSet.Tables["hr_salaryprocessed_hdr"].Rows.Count,
                PreparedDetailRows = _dataSet.Tables["hr_salaryprocessed_dtl"].Rows.Count,
                PreparedCarryForwardPenaltyRows = _dataSet.Tables["hr_penalty_fine"].Rows.Count,
                LoanDeductionRowsInserted = _loanDeductionRowsInserted,
                PreparedTaxableCashHeaderRows = taxScopeRows.Count(row => string.Equals(row.PaymentMode, "Cash", StringComparison.OrdinalIgnoreCase)),
                PreparedTaxableNonCashHeaderRows = taxScopeRows.Count(row => !string.Equals(row.PaymentMode, "Cash", StringComparison.OrdinalIgnoreCase))
            };
        }

        private void AddHeaderRow(string empNo)
        {
            string currentFlag = _paymentMode switch
            {
                "C" => "Cash",
                "Q" => "Cheuqe",
                _ => "Bank"
            };

            if (string.Equals(_paymentMode, "Q", StringComparison.OrdinalIgnoreCase))
            {
                _paymentMode = "B";
            }

            DataRow row = _dataSet.Tables["hr_salaryprocessed_hdr"].NewRow();
            row["SalaryYear"] = _model.Year;
            row["SalaryMonth"] = _model.Month;
            row["Emp_No"] = empNo;
            row["Dept"] = _departmentId;
            row["SalaryProcessedDate"] = DateTime.Now;
            row["BasicSalary"] = _basicSalary;
            row["WorkedDays"] = _workedDays;
            row["Total_Deduction"] = _workedDays <= 0 ? _grossPay : _totalDeductions;
            row["NetPay"] = _workedDays <= 0 ? 0 : _grossPay - _totalDeductions;
            row["currentsalary"] = _currentSalary;
            row["AbsentDays"] = _absents;
            row["RAbsentDays"] = _ruleAbsents;
            row["Absent_amt"] = _absentAmount;
            row["OT_Amount"] = 0m;
            row["PT_Amount"] = _partTime;
            row["extra_hours"] = _extraHours;
            row["extra_hours_amt"] = _extraHoursAmount;
            row["extra_days"] = _extraDays;
            row["extra_days_amt"] = _extraDaysAmount;
            row["extra_fuel"] = _extraFuel;
            row["extra_fuel_amt"] = _extraFuelAmount;
            row["Extra_amount"] = _extraAmount;
            row["Extra_Amount_Taxable"] = _extraAmountTaxable;
            row["Fuel_pday"] = _fuelPerDay;
            row["Fuel_days"] = _fuelDays > 0 ? _fuelDays : 0;
            row["Fuel_Amount"] = _fuelAmount > 0 ? _fuelAmount : 0m;
            row["Fuel_Card_Usage"] = _fuelCardUsage;
            row["FuelCard_Qty_Usage"] = _fuelCardQtyUsage;
            row["CommAmount"] = _commissionAmount;
            row["CODKPIBonus"] = _codKpiBonus;
            row["CODKPIDeduction"] = _codKpiDeduction;
            row["Allowances"] = _allowance;
            row["Deductions"] = _deduction + _penaltyDeduction;
            row["Loan"] = _loan;
            row["Loan_balance"] = _loanBalance;
            row["Advance"] = _advanceSalary;
            row["Tax"] = _tax;
            row["GrossPay"] = _grossPay;
            row["CashPayment"] = _cashAmount;
            row["amount_bank"] = _amountBank;
            row["amount_cash"] = _amountCash;
            row["Payment_mode"] = currentFlag;
            row["Comments"] = $"Salary of {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(_model.Month)} {_model.Year}";
            row["CreatedBy"] = _currentUserId;
            row["Created_Date"] = DateTime.Now;
            row["UpdatedBy"] = _currentUserId;
            row["Updated_Date"] = DateTime.Now;
            row["Type"] = _paymentMode;
            row["createdStation"] = _station;
            row["CreatedLocation"] = _location;
            row["IsPrint"] = 0;
            row["IsPaid"] = 0;
            row["IsViewable"] = 0;
            row["IsProcessed"] = 0;
            row["error"] = DBNull.Value;
            row["isShiftedtoarrears"] = 0;
            row["Arrears"] = DBNull.Value;
            row["dupCount"] = 0;
            row["IsReverse"] = 0;
            row["Pvtype"] = DBNull.Value;
            row["IsReprocessed"] = 0;
            row["IsCPVMade"] = 0;
            row["IsBPVMade"] = 0;
            row["CPVCount"] = 0;
            row["SalaryAdExtraFixed"] = GetSalaryAdjustmentExtraFixed(empNo);
            row["TotalFixGross"] = GetTotalFixedGross(empNo);
            row["NewBasic"] = GetNewBasicSalary(empNo);
            row["FixGross"] = GetFixGrossSalary(empNo);
            _dataSet.Tables["hr_salaryprocessed_hdr"].Rows.Add(row);
        }

        private bool ProcessSalary(string empNo)
        {
            _workedDays = 0;
            _absents = 0;
            _ruleAbsents = 0;
            _totalFuelDays = 0;
            _fuelDays = 0;
            _basicSalary = 0m;
            _taxDeductOn = 0m;
            _paymentMode = string.Empty;

            DataTable data = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT Absents, Sundays, Holidays, Late, Leaves, ruleAbsents, Notout, Early, adjustmentLate, adjustmentAbsent, adjustmentRAbsent
                  FROM hr_employeeattendanceprocess
                  WHERE YEAR = @Year
                    AND MONTH = @Month
                    AND emp_no = @EmpNo",
                new MySqlParameter("@Year", _model.Year),
                new MySqlParameter("@Month", _model.Month),
                new MySqlParameter("@EmpNo", empNo));

            if (data.Rows.Count == 0)
            {
                return false;
            }

            int adjustmentAbsent = data.Rows[0]["adjustmentAbsent"] == DBNull.Value ? 0 : Convert.ToInt32(data.Rows[0]["adjustmentAbsent"]);
            int adjustmentRuleAbsent = data.Rows[0]["adjustmentRAbsent"] == DBNull.Value ? 0 : Convert.ToInt32(data.Rows[0]["adjustmentRAbsent"]);
            int leaves = data.Rows[0]["Leaves"] == DBNull.Value ? 0 : Convert.ToInt32(data.Rows[0]["Leaves"]);

            _sundays = Convert.ToInt32(data.Rows[0]["Sundays"]);
            _holidays = Convert.ToInt32(data.Rows[0]["Holidays"]);

            int rawAbsents = Convert.ToInt32(data.Rows[0]["Absents"]);
            _absents = rawAbsents <= adjustmentAbsent ? 0 : Math.Max(0, rawAbsents - adjustmentAbsent);

            int rawRuleAbsents = Convert.ToInt32(data.Rows[0]["ruleAbsents"]);
            _ruleAbsents = rawRuleAbsents <= adjustmentRuleAbsent ? 0 : rawRuleAbsents - adjustmentRuleAbsent;

            if (_sundays + _holidays + _ruleAbsents + _absents >= 30)
            {
                _workedDays = 0;
                _totalFuelDays = 0;
            }
            else
            {
                _workedDays = 31 - (_absents + _ruleAbsents);
                _totalFuelDays = 31 - (_sundays + _holidays + _absents + _ruleAbsents + leaves);
            }

            _fuelDays = 31 - (_absents + _sundays + _holidays + _ruleAbsents + leaves);
            _fuelDays = _workedDays == 0 ? 0 : _fuelDays;
            _basicSalary = LCS.GetCurrentSalary(_connection, empNo, _salaryDate, out _taxDeductOn, out _paymentMode);
            return true;
        }

        private void ProcessFixeddaysEncashment(DateTime appointmentDate)
        {
            _fixedExtraDaysValue = 0;
            if (!_fixedExtraDays)
            {
                return;
            }

            if (appointmentDate.Year == _model.Year && appointmentDate.Month == _model.Month)
            {
                _fixedExtraDaysValue = _fuelDays > 10 ? 1 : 0;
                return;
            }

            _fixedExtraDaysValue = _fuelDays <= 15 ? 1 : 2;
        }

        private void Extras(string empNo)
        {
            _shiftHours = TimeSpan.Zero;
            _extraHours = 0m;
            _extraFuel = 0m;
            _extraDays = 0m;
            _extraAmount = 0m;
            _extraAmountTaxable = 0m;

            DataTable fixedExtras = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT SUM(IFNULL(f.Amount, 0)) AS Amount, f.Extra_TypeID, ex.GLID, ex.IsTaxable
                  FROM hr_employee_extras_fixed f
                  LEFT JOIN hr_extratype ex ON ex.ETId = f.Extra_TypeID
                  WHERE f.emp_no = @EmpNo
                    AND @SalaryDate BETWEEN f.fromdate AND IFNULL(f.todate, '2099-08-28')
                    AND f.Extra_TypeID <> 33
                  GROUP BY f.Extra_TypeID, ex.GLID, ex.IsTaxable;",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@SalaryDate", _salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)));

            foreach (DataRow row in fixedExtras.Rows)
            {
                decimal amount = Convert.ToDecimal(row["Amount"]);
                if (amount <= 0)
                {
                    continue;
                }

                if (Convert.ToBoolean(row["IsTaxable"]))
                {
                    _extraAmountTaxable += amount;
                }
                else
                {
                    _extraAmount += amount;
                }

                AddSalaryDetailRow(empNo, $"Extra: {((LCS.EExtraType)Convert.ToInt32(row["Extra_TypeID"])).ToString()}", amount, "E", row["Extra_TypeID"]?.ToString() ?? string.Empty, row["Extra_TypeID"]?.ToString() ?? string.Empty, row["GLID"]?.ToString() ?? string.Empty);
            }

            DataTable shifts = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT Start_Time, End_Time
                  FROM hr_shiftdetails
                  WHERE CODE = (
                      SELECT shiftcode
                      FROM hr_employeeshifttimings
                      WHERE emp_no = @EmpNo
                        AND @SalaryDate BETWEEN fromdate AND IFNULL(todate, '2099-08-28')
                      ORDER BY CODE DESC
                      LIMIT 1
                  )",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@SalaryDate", _salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)));

            if (shifts.Rows.Count == 0)
            {
                throw new ArgumentException("Shift not defined for Emp_no " + empNo);
            }

            TimeSpan startTime = TimeSpan.Parse(shifts.Rows[0]["Start_Time"].ToString() ?? "00:00:00", CultureInfo.InvariantCulture);
            TimeSpan endTime = TimeSpan.Parse(shifts.Rows[0]["End_Time"].ToString() ?? "00:00:00", CultureInfo.InvariantCulture);
            _shiftHours = endTime > startTime
                ? endTime - startTime
                : (new TimeSpan(24, 0, 0) - startTime) + (endTime - new TimeSpan(0, 0, 1)).Add(new TimeSpan(0, 0, 0, 1));

            DataTable monthlyExtras = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT ex.emp_no, ex.extra_type, SUM(ex.VALUE) amt, et.GLID, et.IsTaxable
                  FROM hr_employeeextras ex
                  INNER JOIN hr_extratype et ON et.ETId = ex.Extra_type
                  WHERE ex.emp_no = @EmpNo
                    AND YEAR = @Year
                    AND MONTH = @Month
                    AND Status = 2
                  GROUP BY ex.emp_no, YEAR, MONTH, ex.extra_type",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@Year", _model.Year),
                new MySqlParameter("@Month", _model.Month));

            foreach (DataRow row in monthlyExtras.Rows)
            {
                int extraType = Convert.ToInt32(row["extra_type"]);
                decimal amount = Convert.ToDecimal(row["amt"]);
                if (extraType == (int)LCS.EExtraType.Hour)
                {
                    _extraHours += amount;
                }
                else if (extraType == (int)LCS.EExtraType.Day)
                {
                    _extraDays += amount;
                }
                else if (extraType == (int)LCS.EExtraType.Fuel)
                {
                    _extraFuel += amount;
                }
                else
                {
                    if (Convert.ToBoolean(row["IsTaxable"]))
                    {
                        _extraAmountTaxable += amount;
                    }
                    else
                    {
                        _extraAmount += amount;
                    }

                    AddSalaryDetailRow(empNo, $"Extra: {((LCS.EExtraType)extraType).ToString()}", amount, "E", extraType.ToString(CultureInfo.InvariantCulture), extraType.ToString(CultureInfo.InvariantCulture), row["GLID"]?.ToString() ?? string.Empty);
                }
            }
        }

        private void ProcessPartTime(string empNo)
        {
            _partTime = 0m;
            DataTable dt = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT Amount
                  FROM hr_employee_parttime
                  WHERE emp_no = @EmpNo
                    AND @SalaryDate BETWEEN fromdate AND IFNULL(todate, '2099-08-28')
                  ORDER BY CODE DESC
                  LIMIT 1",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@SalaryDate", _salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)));

            if (dt.Rows.Count == 0)
            {
                return;
            }

            _partTime = Convert.ToDecimal(dt.Rows[0]["Amount"]);
            if ((_absents + _ruleAbsents) <= 0)
            {
                return;
            }

            decimal perDay = Convert.ToDecimal(dt.Rows[0]["Amount"]) / 31m;
            _deduction += (_absents + _ruleAbsents) * perDay;
            AddSalaryDetailRow(empNo, "Partime Absent(s) Deduction: ", (_absents + _ruleAbsents) * perDay, "D", "PA", string.Empty, "152");
        }

        private void AllowanceDeduction(string empNo)
        {
            _allowanceDeduction = 0m;
            _allowance = 0m;
            ApplyAllowanceDeductionTable(empNo, _allowanceCatalog);

            DataTable employeeAd = DAL.ExecuteDataTable(
                _connection,
                CommandType.Text,
                @"SELECT b.fullname, Emp_No, b.type, b.Pct_Amount, b.Fix_Amount, b.exclude_absent, b.Code, b.glcode
                  FROM hr_employeead_details a
                  INNER JOIN hr_allow_ded_details b ON a.ad_code = b.code
                  WHERE emp_no = @EmpNo
                    AND DATE(a.EffectiveFrom) <= @SalaryDate
                    AND a.EffectiveTo IS NULL
                  ORDER BY b.code;",
                new MySqlParameter("@EmpNo", empNo),
                new MySqlParameter("@SalaryDate", _salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)));

            ApplyAllowanceDeductionTable(empNo, employeeAd);
        }

        private void ApplyAllowanceDeductionTable(string empNo, DataTable table)
        {
            foreach (DataRow row in table.Rows)
            {
                decimal percentAmount = (_basicSalary + _cashAmount) * Convert.ToDecimal(row["Pct_Amount"]) / 100m;
                decimal amount = percentAmount + Convert.ToDecimal(row["Fix_Amount"]);

                if (string.Equals(row["Type"]?.ToString(), "A", StringComparison.OrdinalIgnoreCase))
                {
                    _allowance += amount;
                    if ((_absents + _ruleAbsents) > 0 && !string.Equals(row["exclude_absent"]?.ToString(), "N", StringComparison.OrdinalIgnoreCase))
                    {
                        int conditionCount = string.Equals(row["exclude_absent"]?.ToString(), "Q", StringComparison.OrdinalIgnoreCase) ? 4 : 30;
                        decimal perCount = amount / conditionCount;
                        decimal deductionCount;

                        if (string.Equals(row["exclude_absent"]?.ToString(), "Q", StringComparison.OrdinalIgnoreCase) && (_absents + _ruleAbsents) >= 4)
                        {
                            deductionCount = amount;
                        }
                        else if (_absents + _ruleAbsents + _holidays + _sundays >= 30 || _absents + _ruleAbsents + _holidays + _sundays > 28)
                        {
                            deductionCount = 30 * perCount;
                        }
                        else
                        {
                            deductionCount = (_absents + _ruleAbsents) * perCount;
                        }

                        _allowanceDeduction += deductionCount;
                        AddSalaryDetailRow(empNo, "Allowance Absent(s) Deduction: " + row["FullName"], deductionCount, "D", "AD", row["Code"]?.ToString() ?? string.Empty, row["glcode"]?.ToString() ?? string.Empty);
                    }
                }

                if (string.Equals(row["Type"]?.ToString(), "D", StringComparison.OrdinalIgnoreCase))
                {
                    _allowanceDeduction += amount;
                }

                AddSalaryDetailRow(empNo, (string.Equals(row["Type"]?.ToString(), "A", StringComparison.OrdinalIgnoreCase) ? "Allowance: " : "Deduction: ") + row["FullName"], amount, string.Equals(row["Type"]?.ToString(), "A", StringComparison.OrdinalIgnoreCase) ? "A" : "D", "AD", row["Code"]?.ToString() ?? string.Empty, row["glcode"]?.ToString() ?? string.Empty);
            }
        }

        private void AddSalaryDetailRow(
            string empNo,
            string description,
            decimal amount,
            string type,
            string deductionType,
            string allowCode,
            string glCode)
        {
            DataRow row = _dataSet.Tables["hr_salaryprocessed_dtl"].NewRow();
            row["SalaryYear"] = _model.Year;
            row["SalaryMonth"] = _model.Month;
            row["Emp_No"] = empNo;
            row["Description"] = description;
            row["Amount"] = amount;
            row["type"] = type;
            row["Deduction_Type"] = deductionType;
            row["Allow_Code"] = allowCode;
            row["glcode"] = glCode;
            _dataSet.Tables["hr_salaryprocessed_dtl"].Rows.Add(row);
        }
    }
}
