using System;
using System.Data;
using System.Globalization;
using Dapper;
using LCS_HR_MVC.Models.Payroll;

namespace LCS_HR_MVC.Services
{
    internal sealed partial class LegacySalaryProcessEngine
    {
        private void GetGrossPay(string empNo)
        {
            decimal otherExtras = GetOtherFixedExtras(empNo);
            decimal deductionsOnAbsent = GetAbsentDayDeduction(empNo);
            decimal newBasicForDayHour = GetNewBasicSalary(empNo);
            decimal totalFixedGross = GetTotalFixedGross(empNo);

            _grossPay = 0m;
            _totalDeductions = 0m;
            _cashAmount = 0m;
            _tax = 0m;

            decimal perDay = (newBasicForDayHour + _cashAmount) / 31m;
            if (perDay <= 500m)
            {
                perDay = 500m;
            }

            decimal divisor = (decimal)(_shiftHours.TotalHours == 0 || _shiftHours.TotalHours > 8 || (_shiftHours.TotalHours > 4 && _shiftHours.TotalHours < 8) ? 8 : _shiftHours.TotalHours);
            decimal perHour = divisor == 0 ? 0 : perDay / divisor;

            if (_fixedExtraDays)
            {
                _extraDays += _fixedExtraDaysValue;
            }

            _extraDaysAmount = perDay * _extraDays;
            _extraHoursAmount = perHour * _extraHours;
            _currentSalary = _basicSalary + _cashAmount;
            _absentAmount = (_absents + _ruleAbsents) * (totalFixedGross + otherExtras) / 31m;

            _grossPay += _basicSalary;
            _grossPay += _cashAmount;
            _grossPay += _fuelAmount;
            _grossPay += _commissionAmount;
            _grossPay += _extraDaysAmount;
            _grossPay += _extraFuelAmount;
            _grossPay += _extraHoursAmount;
            _grossPay += _extraAmount;
            _grossPay += _allowance;
            _grossPay += _partTime;
            _grossPay += _codKpiBonus;

            if (_workedDays == 31)
            {
                _deduction += _allowanceDeduction;
            }
            else
            {
                _deduction += deductionsOnAbsent;
            }

            _totalDeductions += _absentAmount;
            _totalDeductions += _deduction;
            _totalDeductions += _loan;
            _totalDeductions += _advanceSalary;

            _amountBank = 0m;
            _amountCash = 0m;
            var employeeType = LCS.GetEmployeeType(empNo, _connection);

            if (!string.Equals(_paymentMode, "C", StringComparison.OrdinalIgnoreCase))
            {
                if (_taxDeductOn <= 0)
                {
                    _cashAmount = 0m;
                    _amountCash += _fuelAmount > 0 ? _fuelAmount : 0m;
                    _amountCash += _commissionAmount;
                    _amountCash += _extraDaysAmount;
                    _amountCash += _extraFuelAmount;
                    _amountCash += _extraHoursAmount;
                    _amountCash += _extraAmount;
                    _amountCash += _codKpiBonus;

                    DateTime fiscalStartDate = LCS.StartDdateFiscalDate(_model.Month, _model.Year);
                    DateTime fiscalEndDate = LCS.EndDdateFiscalDate(_model.Month, _model.Year);
                    Array monthsBeforeCurrent = LCS.GetMonths(fiscalStartDate, Convert.ToDateTime($"{_model.Month}/01/{_model.Year}", CultureInfo.InvariantCulture));
                    string fromDate = monthsBeforeCurrent.Length > 0 ? monthsBeforeCurrent.GetValue(0)?.ToString() ?? fiscalStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : fiscalStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string toDate = monthsBeforeCurrent.Length > 0 ? monthsBeforeCurrent.GetValue(monthsBeforeCurrent.Length - 1)?.ToString() ?? fiscalEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : fiscalEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Array forwardMonths = LCS.GetMonths(Convert.ToDateTime($"{_model.Month}/09/{_model.Year}", CultureInfo.InvariantCulture), fiscalEndDate);

                    decimal previousExtraAmountTaxable = 0m;
                    decimal previousTax = 0m;
                    decimal previousSalary = 0m;
                    decimal medicalAllowance = _connection.QueryFirstOrDefault<decimal>(
                        @"SELECT IFNULL(al.Fix_Amount,0)
                          FROM hr_employeead_details ad
                          INNER JOIN hr_allow_ded_details al ON al.Code = ad.AD_Code
                          WHERE ad.EffectiveTo IS NULL
                            AND al.FullName LIKE '%MEDICAL%'
                            AND al.Type = 'A'
                            AND al.ShortName = 'MD'
                            AND ad.Emp_No = @EmpNo",
                        new { EmpNo = empNo },
                        _transaction);

                    if (forwardMonths.Length < 12)
                    {
                        DataTable previousTaxAndSalary = DAL.ExecuteDataTable(
                            _connection,
                            CommandType.Text,
                            $@"SELECT IFNULL(SUM(tax),0) AS PaidTax,
                                      IFNULL(SUM(Extra_Amount_Taxable),0) AS Paid_Extra_Amount,
                                      IFNULL(SUM(IF(s.CashPayment = 0, (s.currentsalary + s.PT_Amount + (s.Allowances - {medicalAllowance})), (s.amount_bank + s.Tax))),0) AS PaidSalary
                               FROM hr_salaryprocessed_hdr s
                               WHERE s.Payment_Mode <> 'Cash'
                                 AND s.Emp_No = '{empNo}'
                                 AND DATE(CONCAT(s.SalaryYear, '-', s.SalaryMonth, '-01')) BETWEEN '{fromDate}' AND '{toDate}';");

                        if (previousTaxAndSalary.Rows.Count > 0)
                        {
                            previousExtraAmountTaxable = Convert.ToDecimal(previousTaxAndSalary.Rows[0]["Paid_Extra_Amount"]);
                            previousTax = Convert.ToDecimal(previousTaxAndSalary.Rows[0]["PaidTax"]);
                            previousSalary = Convert.ToDecimal(previousTaxAndSalary.Rows[0]["PaidSalary"]);
                        }
                    }

                    decimal remainingMonthSalary = (_grossPay - (_amountCash + medicalAllowance)) * forwardMonths.Length;
                    decimal yearlySalary = previousSalary + remainingMonthSalary + previousExtraAmountTaxable + _extraAmountTaxable;
                    TaxSlabList? slab = _taxSlabList.Find(item => yearlySalary > item.LimitFrom && yearlySalary < item.LimitTo);
                    decimal pctAmount = slab?.Pct_Amount ?? 0m;
                    decimal fixAmount = slab?.Fix_Amount ?? 0m;
                    decimal limitFrom = slab?.LimitFrom ?? 0m;

                    _tax = (yearlySalary - limitFrom) + 1;
                    _tax = (_tax * pctAmount) / 100m;
                    _tax += fixAmount;
                    _tax -= previousTax;
                    _tax = forwardMonths.Length == 0 ? _tax : _tax / forwardMonths.Length;
                    _tax = _tax < 0 ? 0 : _tax;

                    _grossPay += _extraAmountTaxable;
                    if (string.Equals(employeeType.EmpType, "005", StringComparison.OrdinalIgnoreCase))
                    {
                        _tax = employeeType.IsFiler ? ((_grossPay - _amountCash) * 15m) / 100m : ((_grossPay - _amountCash) * 22m) / 100m;
                    }

                    if (string.Equals(_currentUserRole, "023", StringComparison.Ordinal))
                    {
                        _totalDeductions += _penaltyDeduction;
                        _totalDeductions += _tax;
                        _amountBank = _grossPay - _amountCash;
                        _amountBank -= _totalDeductions;
                    }
                    else
                    {
                        _totalDeductions += _tax;
                        _amountBank = _grossPay - _amountCash - _tax;
                        _amountCash -= _totalDeductions;
                        decimal tempDeduction = _penaltyDeduction + _fuelCardUsage + _codKpiDeduction;

                        if (_amountCash <= 0)
                        {
                            tempDeduction = _amountCash - (_penaltyDeduction + _fuelCardUsage + _codKpiDeduction);
                            _amountCash -= _penaltyDeduction + _fuelCardUsage + _codKpiDeduction;
                            _amountCash = _amountCash <= 0 ? 0 : _amountCash;
                            if (tempDeduction <= 0 && _amountCash == 0)
                            {
                                _amountBank += tempDeduction;
                            }
                        }

                        _totalDeductions += _penaltyDeduction;
                        _totalDeductions += _fuelCardUsage;
                        _totalDeductions += _codKpiDeduction;
                    }
                }
            }
            else
            {
                _cashAmount = 0m;
                _grossPay += _extraAmountTaxable;
                if (string.Equals(employeeType.EmpType, "005", StringComparison.OrdinalIgnoreCase))
                {
                    _tax = employeeType.IsFiler ? ((_grossPay - _amountCash) * 15m) / 100m : ((_grossPay - _amountCash) * 22m) / 100m;
                }

                _totalDeductions += _tax + _penaltyDeduction + _fuelCardUsage + _codKpiDeduction;
                _amountCash = _grossPay - _totalDeductions;
            }
        }

        private decimal GetOtherFixedExtras(string empNo)
        {
            return _connection.ExecuteScalar<decimal?>(
                       @"SELECT OtherExtraFixed
                         FROM hr_employeepersonaldetail
                         WHERE emp_no = @EmpNo",
                       new { EmpNo = empNo },
                       _transaction)
                   ?? 0m;
        }

        private decimal GetCurrentBasicSalary(string empNo)
        {
            return _connection.ExecuteScalar<decimal?>(
                       @"SELECT BasicSalary
                         FROM hr_employeepersonaldetail
                         WHERE emp_no = @EmpNo",
                       new { EmpNo = empNo },
                       _transaction)
                   ?? 0m;
        }

        private decimal GetSalaryAdjustmentExtraFixed(string empNo)
        {
            return _connection.ExecuteScalar<decimal?>(
                       @"SELECT SalaryAdjustmentExtraFixed
                         FROM hr_employeepersonaldetail
                         WHERE emp_no = @EmpNo",
                       new { EmpNo = empNo },
                       _transaction)
                   ?? 0m;
        }

        private decimal GetTotalFixedGross(string empNo)
        {
            return _connection.ExecuteScalar<decimal?>(
                       @"SELECT TotalFixedGross
                         FROM hr_employeepersonaldetail
                         WHERE emp_no = @EmpNo",
                       new { EmpNo = empNo },
                       _transaction)
                   ?? 0m;
        }

        private decimal GetNewBasicSalary(string empNo)
        {
            decimal basicSalary = GetCurrentBasicSalary(empNo);
            decimal totalFixedGross = GetTotalFixedGross(empNo);
            return totalFixedGross >= 40000m ? totalFixedGross * 65m / 100m : basicSalary;
        }

        private decimal GetAbsentDayDeduction(string empNo)
        {
            return _connection.ExecuteScalar<decimal?>(
                       @"SELECT b.Fix_Amount
                         FROM hr_employeead_details a
                         INNER JOIN hr_allow_ded_details b ON a.ad_code = b.code
                         WHERE emp_no = @EmpNo
                           AND TYPE = 'D'
                           AND DATE(a.EffectiveFrom) <= @SalaryDate
                           AND a.EffectiveTo IS NULL
                         ORDER BY b.code
                         LIMIT 1;",
                       new { EmpNo = empNo, SalaryDate = _salaryDate },
                       _transaction)
                   ?? 0m;
        }

        private decimal GetFixGrossSalary(string empNo)
        {
            return _connection.ExecuteScalar<decimal?>(
                       @"SELECT FixGrossSalary
                         FROM hr_employeepersonaldetail
                         WHERE emp_no = @EmpNo",
                       new { EmpNo = empNo },
                       _transaction)
                   ?? 0m;
        }

        private string GetLoanComment()
        {
            return $"From Salary of {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(_model.Month)} {_model.Year}";
        }
    }
}
