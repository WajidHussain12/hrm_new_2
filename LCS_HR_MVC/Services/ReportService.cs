using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models.Report;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySql.Data.MySqlClient;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace LCS_HR_MVC.Services
{
    public class ReportService : IReportService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public ReportService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<Dictionary<string, object>>> GetReportDataAsync(ReportViewModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<Dictionary<string, object>>();
                await connection.OpenAsync();

                string query = string.Empty;
                var parameters = new DynamicParameters();
                parameters.Add("@UserId", currentUserId);
                parameters.Add("@Year", model.Year);
                parameters.Add("@Month", model.Month == 0 ? (int?)null : model.Month);
                parameters.Add("@EmpNo", string.IsNullOrEmpty(model.EmpNo) ? null : model.EmpNo);
                parameters.Add("@ZoneCode", string.IsNullOrEmpty(model.ZoneCode) || model.ZoneCode == "00" ? null : model.ZoneCode);
                parameters.Add("@CityCode", string.IsNullOrEmpty(model.CityCode) || model.CityCode == "00" ? null : model.CityCode);
                parameters.Add("@DepartmentId", string.IsNullOrEmpty(model.DepartmentId) ? null : model.DepartmentId);
                parameters.Add("@FromDate", model.FromDate);
                parameters.Add("@ToDate", model.ToDate);
                parameters.Add("@BankName", string.IsNullOrEmpty(model.BankName) ? null : model.BankName);
                parameters.Add("@ReportMode", string.IsNullOrEmpty(model.ReportMode) ? null : model.ReportMode);

                switch (model.ReportType)
                {
                    case "PaySlips":
                        query = @"
                            SELECT
                                h.Emp_No AS `Employee No`,
                                p.NAME AS `Employee Name`,
                                p.NIC_NO AS `CNIC`,
                                c.FullName AS `City`,
                                sd.FullName AS `Department`,
                                h.BasicSalary AS `Basic Salary`,
                                h.GrossPay AS `Gross Pay`,
                                h.Total_Deduction AS `Total Deduction`,
                                h.NetPay AS `Net Pay`,
                                h.amount_bank AS `Bank Amount`,
                                h.amount_cash AS `Cash Amount`,
                                h.Payment_Mode AS `Payment Mode`,
                                COALESCE(vs.status, 'ORIGINAL') AS `Voucher Status`,
                                h.SalaryProcessedDate AS `Processed Date`
                            FROM hr_salaryprocessed_hdr h
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = h.Emp_No
                            LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                            LEFT JOIN hr_salaryvouchers_status vs
                                ON vs.emp_no = h.Emp_No
                               AND vs.year = h.SalaryYear
                               AND vs.month = h.SalaryMonth
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.userid = @UserId
                              AND h.SalaryYear = @Year
                              AND h.SalaryMonth = @Month
                              AND (@EmpNo IS NULL OR h.Emp_No = @EmpNo)
                              AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                            ORDER BY h.Emp_No";
                        break;

                    case "SalariesDetails":
                        query = @"
                            SELECT
                                h.Emp_No AS `Employee No`,
                                p.NAME AS `Employee Name`,
                                c.FullName AS `City`,
                                sd.FullName AS `Department`,
                                h.BasicSalary AS `Basic Salary`,
                                h.currentsalary AS `Current Salary`,
                                h.WorkedDays AS `Worked Days`,
                                h.AbsentDays AS `Absent Days`,
                                h.Allowances AS `Allowances`,
                                COALESCE(dtl.DetailAllowances, 0) AS `Allowance Details`,
                                h.deductions AS `Deductions`,
                                COALESCE(dtl.DetailDeductions, 0) AS `Deduction Details`,
                                h.Loan AS `Loan`,
                                h.Advance AS `Advance`,
                                h.Tax AS `Tax`,
                                h.CommAmount AS `Commission`,
                                h.Fuel_Amount AS `Fuel Amount`,
                                h.Extra_amount AS `Extra Amount`,
                                h.GrossPay AS `Gross Pay`,
                                h.Total_Deduction AS `Total Deduction`,
                                h.NetPay AS `Net Pay`,
                                h.SalaryProcessedDate AS `Processed Date`
                            FROM hr_salaryprocessed_hdr h
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = h.Emp_No
                            LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                            LEFT JOIN (
                                SELECT
                                    SalaryYear,
                                    SalaryMonth,
                                    emp_no,
                                    SUM(CASE WHEN type = 'A' THEN Amount ELSE 0 END) AS DetailAllowances,
                                    SUM(CASE WHEN type = 'D' THEN Amount ELSE 0 END) AS DetailDeductions
                                FROM hr_salaryprocessed_dtl
                                GROUP BY SalaryYear, SalaryMonth, emp_no
                            ) dtl
                                ON dtl.SalaryYear = h.SalaryYear
                               AND dtl.SalaryMonth = h.SalaryMonth
                               AND dtl.emp_no = h.Emp_No
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.userid = @UserId
                              AND h.SalaryYear = @Year
                              AND h.SalaryMonth = @Month
                              AND (@EmpNo IS NULL OR h.Emp_No = @EmpNo)
                              AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                            ORDER BY h.Emp_No";
                        break;

                    case "SalaryBreakup":
                        query = @"
                            SELECT
                                d.SalaryYear AS `Year`,
                                d.SalaryMonth AS `Month`,
                                d.emp_no AS `Employee No`,
                                p.NAME AS `Employee Name`,
                                c.FullName AS `City`,
                                sd.FullName AS `Department`,
                                d.Description AS `Description`,
                                d.Amount AS `Amount`,
                                CASE d.type
                                    WHEN 'A' THEN 'Allowance'
                                    WHEN 'D' THEN 'Deduction'
                                    WHEN 'E' THEN 'Extra'
                                    ELSE d.type
                                END AS `Line Type`,
                                d.Deduction_Type AS `Deduction Type`,
                                d.Allow_Code AS `Allowance Code`,
                                d.glcode AS `GL Code`
                            FROM hr_salaryprocessed_dtl d
                            INNER JOIN hr_salaryprocessed_hdr h
                                ON h.SalaryYear = d.SalaryYear
                               AND h.SalaryMonth = d.SalaryMonth
                               AND h.Emp_No = d.emp_no
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = d.emp_no
                            LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.userid = @UserId
                              AND d.SalaryYear = @Year
                              AND d.SalaryMonth = @Month
                              AND (@EmpNo IS NULL OR d.emp_no = @EmpNo)
                              AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                            ORDER BY d.emp_no, d.type DESC, d.Description";
                        break;

                    case "CommissionBreakup":
                        query = @"
                            SELECT
                                cp.Year AS `Year`,
                                cp.Month AS `Month`,
                                cp.emp_no AS `Employee No`,
                                p.NAME AS `Employee Name`,
                                c.FullName AS `City`,
                                sd.FullName AS `Department`,
                                cp.DOM_CREDIT AS `Domestic Credit`,
                                cp.LOCAL_CREDIT AS `Local Credit`,
                                cp.LOCAL_DLD AS `Local DLD`,
                                cp.DomesticDelivery AS `Domestic Delivery`,
                                cp.INTL_CREDIT AS `Intl Credit`,
                                cp.COD AS `COD`,
                                cp.PREPAID AS `Prepaid`,
                                cp.AllInOne AS `All In One`,
                                cp.VAS AS `VAS`,
                                cp.COD_Bonus AS `COD Bonus`,
                                cp.COD_Deduction AS `COD Deduction`,
                                cp.Retail_Deduction AS `Retail Deduction`,
                                (
                                    COALESCE(cp.DOM_CREDIT, 0) +
                                    COALESCE(cp.LOCAL_CREDIT, 0) +
                                    COALESCE(cp.LOCAL_DLD, 0) +
                                    COALESCE(cp.DomesticDelivery, 0) +
                                    COALESCE(cp.INTL_CREDIT, 0) +
                                    COALESCE(cp.COD, 0) +
                                    COALESCE(cp.PREPAID, 0) +
                                    COALESCE(cp.AllInOne, 0) +
                                    COALESCE(cp.VAS, 0) +
                                    COALESCE(cp.COD_Bonus, 0) -
                                    COALESCE(cp.COD_Deduction, 0) -
                                    COALESCE(cp.Retail_Deduction, 0)
                                ) AS `Selected Commission Total`
                            FROM hr_commissionprocess cp
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = cp.emp_no
                            LEFT JOIN hr_salaryprocessed_hdr h
                                ON h.SalaryYear = cp.Year
                               AND h.SalaryMonth = cp.Month
                               AND h.Emp_No = cp.emp_no
                            LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                            LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.userid = @UserId
                              AND cp.Year = @Year
                              AND cp.Month = @Month
                              AND (@EmpNo IS NULL OR cp.emp_no = @EmpNo)
                              AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                            ORDER BY cp.emp_no";
                        break;
                    
                    case "EmployeesList":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Name`, p.F_NAME AS `Father Name`,
                                   p.NIC_NO AS `CNIC`, p.CELL_CONTACT_1 AS `Cell No`,
                                   p.APPOINT_DATE AS `Joining Date`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   (SELECT j.FullName FROM hr_employeejobdetails jd
                                    INNER JOIN hr_jobs j ON jd.JobCode = j.Code
                                    WHERE jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL LIMIT 1) AS `Designation`,
                                   (SELECT CONCAT(rhdr.RouteCode,'-',rhdr.Description)
                                    FROM hr_employeeroutecode erc
                                    INNER JOIN hr_routecodes_hdr rhdr ON erc.RouteCode = rhdr.RouteCode AND erc.citycode = rhdr.CityCode
                                    WHERE erc.emp_no = p.EMP_NO LIMIT 1) AS `Route Code`,
                                   p.Emp_WalletNumber AS `Wallet No`,
                                   (SELECT bd.AccountNo FROM hr_employeebankdetails bd WHERE bd.emp_no = p.EMP_NO LIMIT 1) AS `Bank Account`,
                                   et.FullName AS `Employee Type`
                            FROM hr_employeepersonaldetail p
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            LEFT JOIN hr_employeetype et ON et.Code = p.EMPLOYEE_TYPE
                            WHERE p.EMP_STATUS = 'A' AND p.LEFT_DATE IS NULL
                              AND lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                              AND (@DepartmentId IS NULL OR deptDet.DeptCode = @DepartmentId)
                            ORDER BY p.EMP_NO";
                        break;

                    case "TerminationReport":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`, p.F_NAME AS `Father Name`,
                                   p.NIC_NO AS `CNIC`, p.APPOINT_DATE AS `Joining Date`,
                                   t.TerminationDate AS `Termination Date`,
                                   CONCAT(TIMESTAMPDIFF(YEAR, p.APPOINT_DATE, t.TerminationDate), ' Year') AS `Job Duration`,
                                   t.LeavingReason AS `Leaving Reason`,
                                   t.Comments AS `Remarks`,
                                   IF(t.Settlement = 'Y', 'Yes', 'No') AS `Settlement`,
                                   u.Name AS `Created By`
                            FROM hr_employeeterminationdetails t
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = t.Emp_No
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            LEFT JOIN lcs_users u ON u.userID = t.CreatedBy
                            WHERE lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY p.EMP_NO ASC";
                        break;

                    case "AttendanceReport":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   CONCAT(MONTHNAME(STR_TO_DATE(ap.Month,'%m')),'-',ap.Year) AS `Month/Year`,
                                   ap.Absents AS `Absents`,
                                   ap.Sundays AS `Sundays`,
                                   ap.Holidays AS `Holidays`,
                                   ap.Leaves AS `Leaves`,
                                   ap.Late AS `Late`,
                                   ap.HalfDay AS `Half Day`,
                                   ap.ruleAbsents AS `Rule Absents`,
                                   ap.Notout AS `Not Out`,
                                   ap.Early AS `Early`,
                                   ap.adjustmentLate AS `Adj Late`,
                                   ap.adjustmentAbsent AS `Adj Absent`
                            FROM hr_employeeattendanceprocess ap
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = ap.emp_no
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE ap.Year = @Year AND (@Month IS NULL OR ap.Month = @Month)
                              AND lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY c.FullName, p.EMP_NO";
                        break;

                    case "AttendanceSummary":
                        return await BuildAttendanceSummaryAsync(connection, model, currentUserId);

                    case "TrainingReport":
                        query = @"
                            SELECT p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   (SELECT j.FullName FROM hr_employeejobdetails jd
                                    INNER JOIN hr_jobs j ON jd.JobCode = j.Code
                                    WHERE jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL LIMIT 1) AS `Designation`,
                                   pd.PDName AS `Department`,
                                   t.Name AS `Training Name`,
                                   t.InstitutionName AS `Institution`,
                                   t.FromDate AS `From Date`,
                                   t.Amount AS `Amount`
                            FROM hr_employeetrainingdetails t
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = t.Emp_No AND t.flag = 'E'
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE lul.Userid = @UserId
                            ORDER BY t.FromDate DESC";
                        break;

                    case "NewJoiningDetails":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   p.F_NAME AS `Father Name`, p.NIC_NO AS `CNIC`,
                                   p.APPOINT_DATE AS `Joining Date`,
                                   (SELECT j.FullName FROM hr_employeejobdetails jd
                                    INNER JOIN hr_jobs j ON jd.JobCode = j.Code
                                    WHERE jd.Emp_No = p.EMP_NO ORDER BY jd.Created_Date LIMIT 1) AS `Designation`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   et.FullName AS `Employee Type`,
                                   CASE p.dual_job_approve
                                     WHEN 'NA' THEN 'Not Applicable'
                                     WHEN 'N' THEN 'Not Approved'
                                     ELSE 'Approved'
                                   END AS `Dual Job`
                            FROM hr_employeepersonaldetail p
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            INNER JOIN hr_employeetype et ON et.Code = p.EMPLOYEE_TYPE
                            WHERE p.EMP_STATUS <> 'I'
                              AND p.APPOINT_DATE BETWEEN @FromDate AND @ToDate
                              AND lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            GROUP BY p.EMP_NO
                            ORDER BY p.APPOINT_DATE";
                        break;

                    case "LeaveStatusReport":
                        query = @"
                            SELECT p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   sd.FullName AS `Department`,
                                   ls.FullName AS `Leave Type`,
                                   elr.LeaveFromDate AS `From Date`,
                                   elr.LeaveToDate AS `To Date`,
                                   DATEDIFF(elr.LeaveToDate, elr.LeaveFromDate) + 1 AS `No Of Days`,
                                   elr.Status AS `Status`
                            FROM hr_employeeleaverequest elr
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = elr.Emp_No
                            LEFT JOIN hr_leavestructure ls ON ls.Code = elr.LeaveCode
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            WHERE YEAR(elr.LeaveFromDate) = @Year
                              AND (@Month IS NULL OR MONTH(elr.LeaveFromDate) = @Month)
                              AND lul.Userid = @UserId
                              AND (@DepartmentId IS NULL OR deptDet.DeptCode = @DepartmentId)
                            ORDER BY elr.LeaveFromDate DESC";
                        break;

                    case "EmployeePersonalInfo":
                        query = @"
                            SELECT p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   p.F_NAME AS `Father Name`, p.NIC_NO AS `CNIC`,
                                   p.BIRTH_DATE AS `Date of Birth`,
                                   CASE p.GENDER WHEN 'M' THEN 'Male' ELSE 'Female' END AS `Gender`,
                                   p.CELL_CONTACT_1 AS `Cell No`, p.EMAIL_ADD AS `Email`,
                                   p.P_ADDRESS_1 AS `Address`,
                                   (SELECT j.FullName FROM hr_employeejobdetails jd
                                    INNER JOIN hr_jobs j ON jd.JobCode = j.Code
                                    WHERE jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL LIMIT 1) AS `Designation`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   c.FullName AS `City`, rz.FullName AS `Zone`
                            FROM hr_employeepersonaldetail p
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE p.EMP_STATUS = 'A' AND p.LEFT_DATE IS NULL
                              AND lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                              AND (@DepartmentId IS NULL OR deptDet.DeptCode = @DepartmentId)
                            ORDER BY p.EMP_NO";
                        break;

                    case "EmpPerInfoSummary":
                        query = @"
                            SELECT DISTINCT p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   p.F_NAME AS `Father Name`, p.NIC_NO AS `CNIC`,
                                   p.CELL_CONTACT_1 AS `Cell No`, p.EMAIL_ADD AS `Email`,
                                   (SELECT j.FullName FROM hr_employeejobdetails jd
                                    INNER JOIN hr_jobs j ON jd.JobCode = j.Code
                                    WHERE jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL LIMIT 1) AS `Designation`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   c.FullName AS `City`, rz.FullName AS `Zone`,
                                   p.APPOINT_DATE AS `Joining Date`,
                                   CASE p.EMP_STATUS WHEN 'A' THEN 'Active' ELSE 'Inactive' END AS `Status`
                            FROM hr_employeepersonaldetail p
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                              AND (@DepartmentId IS NULL OR deptDet.DeptCode = @DepartmentId)
                              AND (@EmpNo IS NULL OR p.EMP_NO = @EmpNo)
                            ORDER BY p.EMP_NO";
                        break;

                    case "WarningInfo":
                        query = @"
                            SELECT p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   (SELECT j.FullName FROM hr_employeejobdetails jd
                                    INNER JOIN hr_jobs j ON jd.JobCode = j.Code
                                    WHERE jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL LIMIT 1) AS `Designation`,
                                   pd.PDName AS `Department`,
                                   sc.Code AS `Code`,
                                   sc.Type AS `Type`,
                                   sc.reason AS `Reason`,
                                   sc.IssueDate AS `Issue Date`
                            FROM hr_employeeshowcause sc
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = sc.Emp_No
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE lul.Userid = @UserId
                              AND p.EMP_STATUS <> 'I'
                            ORDER BY sc.IssueDate DESC";
                        break;

                    case "RouteDetails":
                        query = @"
                            SELECT c.FullName AS `City`,
                                   rhdr.RouteCode AS `Route Code`,
                                   rhdr.Description AS `Route Description`,
                                   p.EMP_NO AS `Employee No`,
                                   p.NAME AS `Employee Name`,
                                   (SELECT j.FullName FROM hr_employeejobdetails jd
                                    INNER JOIN hr_jobs j ON jd.JobCode = j.Code
                                    WHERE jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL LIMIT 1) AS `Designation`,
                                   pd.PDName AS `Department`
                            FROM hr_employeeroutecode erc
                            INNER JOIN hr_routecodes_hdr rhdr ON erc.RouteCode = rhdr.RouteCode AND erc.citycode = rhdr.CityCode
                            INNER JOIN hr_city c ON rhdr.CityCode = c.Code
                            INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                            LEFT JOIN hr_employeepersonaldetail p ON erc.emp_no = p.EMP_NO
                            LEFT JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            LEFT JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            LEFT JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY c.Code, rhdr.RouteCode, p.EMP_NO";
                        break;

                    case "AttendanceTimeSpan":
                        return await BuildAttendanceTimeSpanAsync(connection, model, currentUserId);

                    case "SalariesInBank":
                        query = @"
                            SELECT
                                c.FullName AS `City`,
                                sd.FullName AS `Department`,
                                h.Emp_No AS `Employee No`,
                                p.NAME AS `Employee Name`,
                                IFNULL(bd.BankName, 'N/A') AS `Bank Name`,
                                IFNULL(bd.BranchCode, 'N/A') AS `Branch Code`,
                                IFNULL(bd.AccountNo, 'N/A') AS `Account No`,
                                ROUND(SUM(h.amount_bank)) AS `Amount`
                            FROM hr_salaryprocessed_hdr h
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = h.Emp_No AND p.generate_salary = 'Y'
                            LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                            LEFT JOIN hr_employeebankdetails bd ON bd.emp_no = h.Emp_No
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.userid = @UserId
                              AND h.SalaryYEAR = @Year AND h.SalaryMONTH = @Month
                              AND h.amount_bank > 0 AND h.Payment_Mode = 'Bank'
                              AND (@BankName IS NULL OR bd.BankName = @BankName)
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                              AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                            GROUP BY h.Emp_No
                            ORDER BY sd.FullName, h.Emp_No";
                        break;

                    case "SalariesInChequeWallet":
                        if (model.ReportMode == "Cash")
                        {
                            query = @"
                                SELECT
                                    c.FullName AS `City`,
                                    sd.FullName AS `Department`,
                                    h.Emp_No AS `Employee No`,
                                    p.NAME AS `Employee Name`,
                                    p.Emp_WalletNumber AS `Wallet Number`,
                                    SUM(h.amount_cash) AS `Amount`
                                FROM hr_salaryprocessed_hdr h
                                INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = h.Emp_No AND p.generate_salary = 'Y'
                                LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                                LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                                INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                                WHERE lul.userid = @UserId
                                  AND h.amount_cash > 0 AND h.Payment_Mode = 'CASH'
                                  AND p.Left_date IS NULL AND p.Emp_WalletNumber IS NOT NULL
                                  AND h.SalaryYEAR = @Year AND h.SalaryMONTH = @Month
                                  AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                                  AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                                GROUP BY h.Emp_No, p.Emp_WalletNumber
                                ORDER BY sd.FullName, h.Emp_No";
                        }
                        else
                        {
                            query = @"
                                SELECT
                                    c.FullName AS `City`,
                                    sd.FullName AS `Department`,
                                    h.Emp_No AS `Employee No`,
                                    p.NAME AS `Employee Name`,
                                    p.Emp_WalletNumber AS `Wallet Number`,
                                    SUM(h.amount_bank) AS `Amount`
                                FROM hr_salaryprocessed_hdr h
                                INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = h.Emp_No AND p.generate_salary = 'Y'
                                LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                                LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                                INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                                WHERE lul.userid = @UserId
                                  AND h.amount_bank > 0 AND h.Payment_Mode = 'Cheuqe'
                                  AND p.Left_date IS NULL
                                  AND h.SalaryYEAR = @Year AND h.SalaryMONTH = @Month
                                  AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                                  AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                                GROUP BY h.Emp_No
                                ORDER BY sd.FullName, h.Emp_No";
                        }
                        break;

                    case "DeductionDetail":
                        query = @"
                            SELECT
                                c.FullName AS `City`,
                                sd.FullName AS `Department`,
                                d.emp_no AS `Employee No`,
                                p.NAME AS `Employee Name`,
                                d.Description AS `Description`,
                                d.Deduction_Type AS `Deduction Type`,
                                d.Amount AS `Amount`
                            FROM hr_salaryprocessed_dtl d
                            INNER JOIN hr_salaryprocessed_hdr h
                                ON h.SalaryYear = d.SalaryYear AND h.SalaryMonth = d.SalaryMonth AND h.Emp_No = d.emp_no
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = d.emp_no
                            LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.userid = @UserId
                              AND d.SalaryYear = @Year AND d.SalaryMonth = @Month
                              AND d.type = 'D'
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                              AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                            ORDER BY d.emp_no, d.Description";
                        break;

                    case "SalarySummary":
                        query = @"
                            SELECT
                                c.FullName AS `City`,
                                sd.FullName AS `Department`,
                                h.Emp_No AS `Employee No`,
                                p.NAME AS `Employee Name`,
                                h.Payment_Mode AS `Payment Mode`,
                                ROUND(h.BasicSalary) AS `Basic Salary`,
                                ROUND(h.GrossPay) AS `Gross Pay`,
                                ROUND(h.Total_Deduction) AS `Total Deduction`,
                                ROUND(h.NetPay) AS `Net Pay`,
                                ROUND(h.amount_bank) AS `Bank Amount`,
                                ROUND(h.amount_cash) AS `Cash Amount`
                            FROM hr_salaryprocessed_hdr h
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = h.Emp_No
                            LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            LEFT JOIN hr_subdepartment sd ON sd.SDID = h.Dept
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.userid = @UserId
                              AND h.SalaryYEAR = @Year AND h.SalaryMONTH = @Month
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                              AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                              AND (@DepartmentId IS NULL OR h.Dept = @DepartmentId)
                            ORDER BY c.FullName, sd.FullName, h.Emp_No";
                        break;

                    case "AdvanceSalaryDetail":
                        query = @"
                            SELECT
                                z.FullName AS `Zone`,
                                c.FullName AS `City`,
                                has2.emp_no AS `Employee No`,
                                p.NAME AS `Employee Name`,
                                pd.PDName AS `Department`,
                                sd.FullName AS `Sub Department`,
                                has2.Amount AS `Advance Salary`,
                                IFNULL((SELECT hsh.Advance FROM hr_salaryprocessed_hdr hsh
                                        WHERE hsh.SalaryYear = @Year AND hsh.SalaryMonth = @Month
                                          AND hsh.Emp_No = has2.emp_no), 0) AS `Deduction`
                            FROM hr_advance_salary has2
                            INNER JOIN hr_employeedepartmentdetails hed ON hed.Emp_No = has2.emp_no AND hed.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = hed.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = has2.emp_no
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            LEFT JOIN hr_regionalzones z ON z.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.userid = @UserId
                              AND has2.Year = @Year AND has2.Month = @Month AND has2.status = 'A'
                              AND (@ZoneCode IS NULL OR c.RZoneCode = @ZoneCode)
                              AND (@CityCode IS NULL OR p.P_CITY_CODE = @CityCode)
                              AND (@DepartmentId IS NULL OR sd.SDID = @DepartmentId)
                            ORDER BY c.RZoneCode, c.FullName, sd.FullName, has2.emp_no";
                        break;

                    case "DeductionReport":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   j.FullName AS `Designation`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   pt.PenaltyType AS `Deduction Type`,
                                   DATE_FORMAT(f.FineDate, '%d/%m/%Y') AS `Date`,
                                   f.amount AS `Amount`,
                                   f.Remarks AS `Remarks`
                            FROM hr_penalty_fine f
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = f.code
                            INNER JOIN hr_city c ON c.Code = f.city_id
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN hr_penaltytype pt ON pt.PTID = f.Type
                            INNER JOIN hr_employeejobdetails jd ON jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL
                            INNER JOIN hr_jobs j ON j.Code = jd.JobCode
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.Userid = @UserId
                              AND YEAR(f.FineDate) = @Year AND MONTH(f.FineDate) = @Month
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                              AND (@DepartmentId IS NULL OR sd.SDID = @DepartmentId)
                              AND (@EmpNo IS NULL OR p.EMP_NO = @EmpNo)
                            ORDER BY rz.FullName, c.FullName, p.EMP_NO";
                        break;

                    case "ExtrasReport":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   j.FullName AS `Designation`,
                                   et.ExtraType AS `Extra Type`,
                                   e.Year AS `Year`, e.Month AS `Month`,
                                   e.Value AS `Value`,
                                   e.Amount AS `Amount`,
                                   CASE e.Extra_type
                                       WHEN '1' THEN ROUND(e.Amount + IFNULL(e.ExtraDaysFuelAmount, 0), 0)
                                       WHEN '2' THEN ROUND(e.Amount, 0)
                                       WHEN '3' THEN ROUND(e.Amount, 0)
                                       ELSE e.Value
                                   END AS `Payable Amount`,
                                   e.Comments AS `Remarks`
                            FROM hr_employeeextras e
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = e.emp_no
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            INNER JOIN hr_employeejobdetails jd ON jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL
                            INNER JOIN hr_jobs j ON j.Code = jd.JobCode
                            LEFT JOIN hr_extratype et ON et.ETId = e.Extra_type
                            WHERE lul.Userid = @UserId
                              AND e.Year = @Year AND e.Month = @Month
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            UNION
                            SELECT rz.FullName, c.FullName,
                                   p.EMP_NO, p.NAME,
                                   pd.PDName, sd.FullName,
                                   j.FullName,
                                   et.ExtraType,
                                   YEAR(ef.FromDate), MONTH(ef.FromDate),
                                   ef.Amount,
                                   ef.Amount,
                                   ef.Amount,
                                   ef.Comments
                            FROM hr_employee_extras_fixed ef
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = ef.Emp_no
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            INNER JOIN hr_employeejobdetails jd ON jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL
                            INNER JOIN hr_jobs j ON j.Code = jd.JobCode
                            LEFT JOIN hr_extratype et ON et.ETId = ef.Extra_TypeID
                            WHERE lul.Userid = @UserId
                              AND ef.ToDate IS NULL
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY 1, 2, 3";
                        break;

                    case "HLAttendanceRatio":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   @FromDate AS `Date`,
                                   COUNT(p.EMP_NO) AS `Total Employees`,
                                   COUNT(xb.EmpNo) AS `Present (App)`,
                                   CONCAT(ROUND((COUNT(xb.EmpNo) / NULLIF(COUNT(p.EMP_NO), 0)) * 100, 2), '%') AS `Attendance %`
                            FROM hr_employeepersonaldetail p
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            LEFT JOIN (SELECT DISTINCT EmpNo FROM hr_mobileappattendence WHERE DATE(AttendenceDate) = DATE(@FromDate)) xb ON xb.EmpNo = p.EMP_NO
                            WHERE p.LEFT_DATE IS NULL
                              AND lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            GROUP BY rz.Code, c.Code, sd.SDID
                            ORDER BY rz.FullName, c.FullName, sd.FullName";
                        break;

                    case "HumLeopardAttendance":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`
                            FROM hr_employeepersonaldetail p
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE p.LEFT_DATE IS NULL
                              AND lul.Userid = @UserId
                              AND NOT EXISTS (SELECT 1 FROM hr_mobileappattendence ma WHERE ma.EmpNo = p.EMP_NO)
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, p.EMP_NO";
                        break;

                    case "EmployeeFuelDetail":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   j.FullName AS `Designation`,
                                   p.NIC_NO AS `CNIC`,
                                   ft.Type AS `Fuel Type`,
                                   f.Fuel_Pday AS `Fuel Per Day`,
                                   f.Fuel_Mode AS `Fuel Mode`,
                                   fc.FuelCardName AS `Fuel Card Name`,
                                   fc.FuelCardNo AS `Fuel Card No`
                            FROM hr_employeefueldetails f
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = f.Emp_No
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_fueltype ft ON ft.Code = f.Fcode
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            INNER JOIN hr_employeejobdetails jd ON jd.Emp_No = p.EMP_NO AND jd.EffectiveTo IS NULL
                            INNER JOIN hr_jobs j ON j.Code = jd.JobCode
                            LEFT JOIN hr_fuelcarddetail fc ON fc.Emp_No = p.EMP_NO AND fc.ToDate IS NULL
                            WHERE f.ToDate IS NULL AND p.LEFT_DATE IS NULL
                              AND lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, p.EMP_NO";
                        break;

                    case "FuelCardTransaction":
                        if (model.ReportMode == "Detail")
                        {
                            query = @"
                                SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                       p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                       fc.FuelCardNo AS `Card No`, ft.DriverName AS `Driver`,
                                       DATE_FORMAT(ft.DeliveryDate, '%d/%m/%Y') AS `Delivery Date`,
                                       ft.DTime AS `Time`, ft.ReceiptNumber AS `Receipt No`,
                                       ft.SiteName AS `Site Name`,
                                       ft.Quantity AS `Qty (L)`,
                                       ft.UnitPrice AS `Unit Price`,
                                       ft.GrossAmount AS `Amount`
                                FROM hr_fuel_transaction ft
                                LEFT JOIN hr_fuelcarddetail fc ON ft.CardNumber = fc.FuelCardNo
                                INNER JOIN hr_employeepersonaldetail p ON fc.Emp_No = p.EMP_NO
                                INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                                INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                                INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                                WHERE ft.DeliveryDate BETWEEN @FromDate AND @ToDate
                                  AND lul.Userid = @UserId
                                  AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                                  AND (@CityCode IS NULL OR c.Code = @CityCode)
                                ORDER BY rz.FullName, c.FullName, p.EMP_NO, ft.DeliveryDate";
                        }
                        else
                        {
                            query = @"
                                SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                       p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                       fc.FuelCardName AS `Card Name`, fc.FuelCardNo AS `Card No`,
                                       ft.UnitPrice AS `Unit Price`,
                                       SUM(ft.Quantity) AS `Total Qty (L)`,
                                       SUM(ft.GrossAmount) AS `Total Amount`
                                FROM hr_employeepersonaldetail p
                                INNER JOIN hr_fuelcarddetail fc ON fc.Emp_No = p.EMP_NO
                                INNER JOIN hr_fuel_transaction ft ON ft.CardNumber = fc.FuelCardNo
                                INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                                INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                                INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                                WHERE ft.DeliveryDate BETWEEN @FromDate AND @ToDate
                                  AND lul.Userid = @UserId
                                  AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                                  AND (@CityCode IS NULL OR c.Code = @CityCode)
                                GROUP BY p.EMP_NO, fc.FuelCardNo, ft.UnitPrice
                                ORDER BY rz.FullName, c.FullName, p.EMP_NO";
                        }
                        break;

                    case "EmpPayDetails":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   sph.Emp_No AS `Employee No`, p.NAME AS `Employee Name`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   sph.BasicSalary AS `Basic Salary`,
                                   sph.currentsalary AS `Current Salary`,
                                   sph.OT_Amount AS `OT Amount`,
                                   IFNULL(sph.PT_Amount, 0) AS `PT Amount`,
                                   sph.extra_hours_amt AS `Extra Hours Amt`,
                                   sph.extra_days_amt AS `Extra Days Amt`,
                                   sph.extra_fuel_amt AS `Extra Fuel Amt`,
                                   sph.Fuel_Amount AS `Fuel Amount`,
                                   sph.CommAmount AS `Commission`,
                                   sph.Allowances AS `Allowances`,
                                   sph.GrossPay AS `Gross Pay`,
                                   sph.deductions AS `Deductions`,
                                   sph.Loan AS `Loan`,
                                   sph.Advance AS `Advance`,
                                   sph.Tax AS `Tax`,
                                   sph.Total_Deduction AS `Total Deduction`,
                                   sph.NetPay AS `Net Pay`,
                                   sph.CashPayment AS `Cash Payment`,
                                   sph.Absent_amt AS `Absent Amount`
                            FROM hr_salaryprocessed_hdr sph
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = sph.Emp_No
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_subdepartment sd ON sd.SDID = sph.Dept
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE lul.Userid = @UserId
                              AND sph.SalaryYear = @Year AND sph.SalaryMonth = @Month
                              AND (@ReportMode IS NULL OR @ReportMode = '' OR (@ReportMode = 'Executive' AND p.IsExecutive = 1) OR (@ReportMode = 'NonExecutive' AND p.IsExecutive = 0))
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, sph.Emp_No";
                        break;

                    case "VoiceOfEmployee":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   req.Request_No AS `Request No`,
                                   DATE_FORMAT(req.Request_Date, '%d/%m/%Y') AS `Request Date`,
                                   CASE req.IssueType
                                       WHEN 'A'  THEN 'Administration'
                                       WHEN 'C'  THEN 'Commission'
                                       WHEN 'S'  THEN 'Salary'
                                       WHEN 'OT' THEN 'Overtime'
                                       WHEN 'RL' THEN 'Regarding Late'
                                       WHEN 'M'  THEN 'Management'
                                       WHEN 'V'  THEN 'Violence'
                                       WHEN 'T'  THEN 'Theft'
                                       WHEN 'H'  THEN 'Harassment'
                                       WHEN 'D'  THEN 'Discrimination'
                                       WHEN 'O'  THEN 'Other'
                                       ELSE req.IssueType
                                   END AS `Issue Type`,
                                   req.ConcernAuth_PersonName AS `Concern Person`,
                                   CASE req.Status
                                       WHEN 'I' THEN 'Issued'
                                       WHEN 'R' THEN 'Resolved'
                                       WHEN 'C' THEN 'Closed'
                                       ELSE req.Status
                                   END AS `Status`
                            FROM hr_employeerequest req
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = req.Emp_No
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, p.EMP_NO, req.Request_Date DESC";
                        break;

                    case "EmpLoanDetails":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   lt.FullName AS `Loan Type`,
                                   DATE_FORMAT(req.RequestDate, '%d/%m/%Y') AS `Request Date`,
                                   req.RequestAmount AS `Request Amount`,
                                   req.RequestInstallments AS `Request Installments`,
                                   DATE_FORMAT(dis.DisbursedDate, '%d/%m/%Y') AS `Disbursed Date`,
                                   dis.DisbursedAmount AS `Disbursed Amount`,
                                   dis.DeductionInstallments AS `Deduction Installments`,
                                   DATE_FORMAT(dis.DeductionStartDate, '%d/%m/%Y') AS `Deduction Start`,
                                   COALESCE(ded.TotalDeducted, 0) AS `Total Deducted`,
                                   COALESCE(dis.DisbursedAmount - ded.TotalDeducted, dis.DisbursedAmount) AS `Balance`,
                                   req.Reason AS `Reason`
                            FROM hr_employeeloanrequest req
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = req.Emp_No
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            INNER JOIN hr_loantypes lt ON lt.Code = req.LoanCode
                            LEFT JOIN hr_employeeloandisbursed dis ON dis.LR_No = req.LR_No
                            LEFT JOIN (
                                SELECT LD_No, SUM(DeductionAmount) AS TotalDeducted
                                FROM hr_employeeloandeduction
                                GROUP BY LD_No
                            ) ded ON ded.LD_No = dis.LD_No
                            WHERE req.Status = 'A'
                              AND lul.Userid = @UserId
                              AND (@EmpNo IS NULL OR p.EMP_NO = @EmpNo)
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, p.EMP_NO";
                        break;

                    case "RBICNWiseDetail":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   rbi.Emp_No AS `Employee No`, p.NAME AS `Employee Name`,
                                   p.APPOINT_DATE AS `Appoint Date`, p.LEFT_DATE AS `Left Date`,
                                   rbi.Cour_Id AS `Leopard ID`, rbi.Cour_Name AS `Leopard Name`,
                                   ct.Name AS `Code Type`,
                                   rbi.ShimpmentType AS `Shipment Type`,
                                   rbi.WeightSlab AS `Weight Slab`,
                                   rbi.ShipmentCategory AS `Service`,
                                   rbi.client_id AS `Account No`,
                                   rbi.ClientName AS `Client Name`,
                                   rbi.ClientStatus AS `Client Status`,
                                   rbi.CN_Number AS `CN Number`,
                                   rbi.Total_Amount AS `Total Amount`,
                                   rbi.Total_Weight AS `Total Weight`,
                                   rbi.OldIncentive AS `Ship Incentive`,
                                   rbi.NewIncentive AS `RBI Incentive`,
                                   rbi.FinalIncentive AS `Final Incentive`
                            FROM hr_rbi_incentive_detail rbi
                            LEFT JOIN hr_employeepersonaldetail p ON p.EMP_NO = rbi.Emp_No
                            INNER JOIN hr_city c ON c.station_id = rbi.Station_id
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                            LEFT JOIN couriercodetype ct ON ct.Id = rbi.CodeType
                            WHERE lul.Userid = @UserId
                              AND rbi.year = @Year AND rbi.month = @Month
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, rbi.Emp_No";
                        break;

                    case "RBIDeductionCNWiseDetail":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   rbi.Emp_No AS `Employee No`, p.NAME AS `Employee Name`,
                                   rbi.Cour_Id AS `Leopard ID`, rbi.Cour_Name AS `Leopard Name`,
                                   ct.Name AS `Code Type`,
                                   rbi.ShimpmentType AS `Shipment Type`,
                                   rbi.ShipmentCategory AS `Service`,
                                   rbi.client_id AS `Account No`,
                                   rbi.ClientName AS `Client Name`,
                                   rbi.CN_Number AS `CN Number`,
                                   rbi.Total_Amount AS `Total Amount`,
                                   rbi.OldIncentive AS `Original Incentive`,
                                   rbi.FinalIncentive AS `Final Incentive`,
                                   (rbi.OldIncentive - rbi.FinalIncentive) AS `Deduction Amount`,
                                   IF(rbi.RBIExclude = 1, 'Yes', 'No') AS `Excluded`
                            FROM hr_rbi_incentive_detail rbi
                            LEFT JOIN hr_employeepersonaldetail p ON p.EMP_NO = rbi.Emp_No
                            INNER JOIN hr_city c ON c.station_id = rbi.Station_id
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                            LEFT JOIN couriercodetype ct ON ct.Id = rbi.CodeType
                            WHERE lul.Userid = @UserId
                              AND rbi.year = @Year AND rbi.month = @Month
                              AND (rbi.RBIExclude = 1 OR rbi.OldIncentive > rbi.FinalIncentive)
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, rbi.Emp_No";
                        break;

                    case "CashCNWiseDetail":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   cc.cour_id AS `Courier ID`, cc.cour_Name AS `Courier Name`,
                                   ct.Name AS `Code Type`,
                                   cc.cn_number AS `CN Number`,
                                   cc.Billing_Type AS `Billing Type`,
                                   DATE_FORMAT(cc.billing_date, '%d/%m/%Y') AS `Billing Date`,
                                   cc.client_id AS `Client Account`,
                                   cc.shipment_type AS `Shipment Type`,
                                   cc.no_of_peices AS `Pieces`,
                                   cc.Weight_KG AS `Weight (KG)`,
                                   cc.Weight_Bucket AS `Weight Bucket`,
                                   cc.rate AS `Rate`,
                                   cc.Gross_Amount AS `Gross Amount`,
                                   cc.Commission_Rate AS `Commission Rate`,
                                   cc.BaseCommission AS `Base Commission`,
                                   cc.MTDWeight_Bucket AS `MTD Weight Bucket`,
                                   cc.MTDCommission AS `MTD Commission`,
                                   cc.InsuranceCommission AS `Insurance Commission`,
                                   cc.VASCommission AS `VAS Commission`,
                                   cc.TotalCommission AS `Total Commission`
                            FROM hr_cash_consignments cc
                            LEFT JOIN hr_city c ON c.station_id = cc.Station_id
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                            LEFT JOIN hr_employeeroutecode rc ON rc.RouteCode = cc.cour_id AND rc.citycode = c.Code AND rc.ToDate IS NULL
                            LEFT JOIN couriercodetype ct ON ct.Id = rc.CodeType
                            WHERE lul.Userid = @UserId
                              AND cc.billing_date BETWEEN DATE_SUB(DATE(CONCAT(@Year,'-',LPAD(@Month,2,'0'),'-21')), INTERVAL 1 MONTH)
                                                      AND DATE(CONCAT(@Year,'-',LPAD(@Month,2,'0'),'-20'))
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, cc.cour_id, cc.billing_date";
                        break;

                    case "CODReturnCNWiseDetail":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   cr.Emp_No AS `Employee No`,
                                   cr.HubName AS `Hub`,
                                   DATE_FORMAT(cr.COUR_DATE, '%d/%m/%Y') AS `Courier Date`,
                                   cr.CN_NUMBER AS `CN Number`,
                                   cr.COURIER_ID AS `Courier ID`,
                                   cr.Cour_Name AS `Courier Name`,
                                   cr.CodeType AS `Code Type`,
                                   DATE_FORMAT(cr.book_date, '%d/%m/%Y') AS `Booking Date`,
                                   cr.client_id AS `Client Account`,
                                   cr.shipment_name AS `Shipment`,
                                   cr.brand_name AS `Brand`,
                                   cr.consignment_name AS `Consignee`,
                                   cr.STATUS AS `Status`,
                                   DATE_FORMAT(cr.DELIVERY_DATE, '%d/%m/%Y') AS `Delivery Date`,
                                   cr.RatePerShipment AS `Rate/Shipment`,
                                   cr.OpsInc AS `Ops Incentive`
                            FROM hr_codreturn_consignments cr
                            INNER JOIN hr_city c ON c.station_id = cr.Station_id
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                            WHERE lul.Userid = @UserId
                              AND cr.Year = @Year AND cr.Month = @Month
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, cr.Emp_No";
                        break;

                    case "VASCNWiseDetail":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   v.Emp_No AS `Employee No`, v.Emp_Name AS `Employee Name`,
                                   v.Cour_id AS `Courier ID`, v.Cour_Name AS `Courier Name`,
                                   v.CodeType AS `Code Type`,
                                   v.Cn_number AS `CN Number`,
                                   v.Billing_Type AS `Billing Type`,
                                   DATE_FORMAT(v.Delivery_Date, '%d/%m/%Y') AS `Delivery Date`,
                                   v.Client_id AS `Client Account`,
                                   v.Client_Name AS `Client Name`,
                                   v.Shipment_type AS `Shipment Type`,
                                   v.No_of_peices AS `Pieces`,
                                   v.Weight AS `Weight`,
                                   v.Status AS `Status`,
                                   v.Category AS `Category`,
                                   v.IncentiveRate AS `Incentive Rate`,
                                   v.Final_Incentive AS `Final Incentive`
                            FROM hr_vas_incentive_detail v
                            LEFT JOIN hr_city c ON c.station_id = v.Station_id
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                            WHERE lul.Userid = @UserId
                              AND v.Delivery_Date BETWEEN DATE_SUB(DATE(CONCAT(@Year,'-',LPAD(@Month,2,'0'),'-21')), INTERVAL 1 MONTH)
                                                      AND DATE(CONCAT(@Year,'-',LPAD(@Month,2,'0'),'-20'))
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, v.Emp_No, v.Delivery_Date";
                        break;

                    case "CODPickupCNWiseDetail":
                        query = @"
                            SELECT cc.ZONE_NAME AS `Zone`, cc.CITY_NAME AS `City`,
                                   cc.emp_no AS `Employee No`, cc.employee_name AS `Employee Name`,
                                   cc.cn_ar AS `CN Number`,
                                   DATE_FORMAT(cc.RC_date, '%d/%m/%Y') AS `RC Date`,
                                   cc.status_code AS `Status`,
                                   cc.cour_id AS `Courier ID`, cc.cour_name AS `Courier Name`,
                                   cc.clnt_id AS `Client Account`,
                                   cc.shipment_name AS `Shipment`,
                                   cc.brand_name AS `Brand`,
                                   cc.courier_type AS `Courier Type`,
                                   cc.Category AS `Category`,
                                   cc.Subcategory AS `Sub Category`,
                                   cc.Rate_per_Shipment_Rs AS `Rate/Shipment`,
                                   cc.Pickup_Leopard AS `Pickup Leopard`,
                                   cc.Pickup_Backend AS `Pickup Backend`,
                                   cc.OPS_Backend AS `OPS Backend`,
                                   cc.BM_Sup_Mngr AS `BM/Sup/Mngr`
                            FROM asif_data_sept_oct cc
                            INNER JOIN hr_city c ON c.station_id = CAST(cc.origin_city_id AS CHAR)
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                            WHERE lul.Userid = @UserId
                              AND cc.RC_date BETWEEN DATE_SUB(DATE(CONCAT(@Year,'-',LPAD(@Month,2,'0'),'-21')), INTERVAL 1 MONTH)
                                                 AND DATE(CONCAT(@Year,'-',LPAD(@Month,2,'0'),'-20'))
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY cc.ZONE_NAME, cc.CITY_NAME, cc.emp_no, cc.RC_date";
                        break;

                    case "AdvanceSalaryVoucher":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   adv.emp_no AS `Employee No`, p.NAME AS `Employee Name`,
                                   p.NIC_NO AS `CNIC`,
                                   sd.FullName AS `Department`,
                                   adv.Year AS `Year`, adv.Month AS `Month`,
                                   adv.Amount AS `Amount`,
                                   CASE adv.status
                                       WHEN 'A' THEN 'Approved'
                                       WHEN 'P' THEN 'Pending'
                                       WHEN 'R' THEN 'Rejected'
                                       ELSE adv.status
                                   END AS `Status`,
                                   DATE_FORMAT(adv.lPrintDate, '%d/%m/%Y %H:%i') AS `Print Date`
                            FROM hr_advance_salary adv
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = adv.emp_no
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            WHERE lul.Userid = @UserId
                              AND adv.Year = @Year AND adv.Month = @Month
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, adv.emp_no";
                        break;

                    case "SalaryTracking":
                        query = @"
                            SELECT DATE_FORMAT(CONCAT(hsh.SalaryYear,'-',LPAD(hsh.SalaryMonth,2,'0'),'-01'),'%b-%Y') AS `Month/Year`,
                                   DATE_FORMAT(hsh.SalaryProcessedDate,'%d-%b-%Y') AS `Processed Date`,
                                   hsh.WorkedDays AS `Worked Days`,
                                   hsh.AbsentDays AS `Absent Days`,
                                   FORMAT(hsh.currentsalary, 0) AS `Basic Salary`,
                                   FORMAT(IFNULL(hsh.PT_Amount,0), 0) AS `PT Amount`,
                                   FORMAT(hsh.Allowances, 0) AS `Allowances`,
                                   FORMAT(IFNULL(hsh.extra_hours_amt,0)+IFNULL(hsh.extra_days_amt,0)+IFNULL(hsh.extra_fuel_amt,0)+IFNULL(hsh.Extra_amount,0), 0) AS `Extra Amount`,
                                   FORMAT(IFNULL(hsh.Fuel_Amount,0), 0) AS `Fuel Amount`,
                                   FORMAT(IFNULL(hsh.CommAmount,0), 0) AS `Commission`,
                                   FORMAT(hsh.GrossPay, 0) AS `Gross Pay`,
                                   FORMAT(IFNULL(hsh.Loan,0), 0) AS `Loan`,
                                   FORMAT(IFNULL(hsh.Advance,0), 0) AS `Advance`,
                                   FORMAT(IFNULL(hsh.Tax,0), 0) AS `Tax`,
                                   FORMAT(IFNULL(hsh.deductions,0), 0) AS `Penalty`,
                                   FORMAT(hsh.Total_Deduction, 0) AS `Total Deduction`,
                                   FORMAT(hsh.NetPay, 0) AS `Net Pay`,
                                   CASE WHEN hsh.IsPaid = 1 THEN 'PAID' ELSE 'Unpaid' END AS `Status`,
                                   FORMAT(CASE WHEN hsh.Payment_Mode = 'Bank' THEN hsh.amount_bank ELSE 0 END, 0) AS `Bank`,
                                   FORMAT(IFNULL(hsh.amount_cash,0), 0) AS `Cash`
                            FROM hr_salaryprocessed_hdr hsh
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = hsh.Emp_No
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE hsh.Emp_No = @EmpNo
                              AND lul.Userid = @UserId
                            GROUP BY hsh.Emp_No, hsh.SalaryYear, hsh.SalaryMonth
                            ORDER BY hsh.SalaryYear DESC, hsh.SalaryMonth DESC
                            LIMIT 12";
                        break;

                    case "SalariesSummaryAccounts":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   s.Emp_No AS `Emp No`,
                                   p.NAME AS `Employee Name`,
                                   GetRouteCodeWithName(s.Emp_No) AS `Route`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   ROUND((s.BasicSalary+s.CashPayment)-s.Absent_amt, 0) AS `Salary`,
                                   ROUND(s.OT_Amount, 0) AS `OT`,
                                   ROUND(IFNULL(s.PT_Amount,0), 0) AS `PT`,
                                   ROUND(s.extra_hours_amt, 0) AS `Ex Hrs`,
                                   ROUND(s.extra_days_amt, 0) AS `Ex Days`,
                                   ROUND(s.extra_fuel_amt, 0) AS `Ex Fuel`,
                                   ROUND(s.Extra_amount, 0) AS `Ex Amt`,
                                   ROUND(s.CommAmount, 0) AS `Commission`,
                                   ROUND(s.Allowances, 0) AS `Allowances`,
                                   ROUND(s.Fuel_Amount, 0) AS `Fuel`,
                                   ROUND((s.BasicSalary+s.CashPayment+s.OT_Amount+IFNULL(s.PT_Amount,0)+s.extra_hours_amt+s.extra_days_amt+s.extra_fuel_amt+s.Extra_amount+s.CommAmount+s.Allowances+s.Fuel_Amount)-s.Absent_amt, 0) AS `Gross`,
                                   ROUND(s.Tax, 0) AS `Tax`,
                                   ROUND(s.Loan, 0) AS `Loan`,
                                   ROUND(s.Advance, 0) AS `Advance`,
                                   ROUND(IFNULL((SELECT SUM(d.Amount) FROM hr_salaryprocessed_dtl d WHERE d.SalaryYear=@Year AND d.SalaryMonth=@Month AND d.type='D' AND d.emp_no=s.Emp_No AND d.Deduction_Type='MH'),0), 0) AS `MH`,
                                   ROUND(IFNULL((SELECT SUM(d.Amount) FROM hr_salaryprocessed_dtl d WHERE d.SalaryYear=@Year AND d.SalaryMonth=@Month AND d.type='D' AND d.emp_no=s.Emp_No AND d.Deduction_Type='LT'),0), 0) AS `Late`,
                                   ROUND(IFNULL((SELECT SUM(d.Amount) FROM hr_salaryprocessed_dtl d WHERE d.SalaryYear=@Year AND d.SalaryMonth=@Month AND d.type='D' AND d.emp_no=s.Emp_No AND d.Deduction_Type='SP'),0), 0) AS `SP`,
                                   ROUND(IFNULL((SELECT SUM(d.Amount) FROM hr_salaryprocessed_dtl d WHERE d.SalaryYear=@Year AND d.SalaryMonth=@Month AND d.type='D' AND d.emp_no=s.Emp_No AND d.Deduction_Type='PF'),0), 0) AS `PF`,
                                   ROUND(s.Loan+s.Advance+s.Tax+s.deductions, 0) AS `Total Deduction`,
                                   ROUND(s.NetPay, 0) AS `Net Pay`
                            FROM hr_salaryprocessed_hdr s
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = s.Emp_No
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_subdepartment sd ON sd.SDID = s.Dept
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE s.SalaryYear = @Year AND s.SalaryMonth = @Month
                              AND lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, s.Emp_No";
                        break;

                    case "CommissionDetail":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   cp.emp_no AS `Employee No`, p.NAME AS `Employee Name`,
                                   GetRouteCodeWithName(cp.emp_no) AS `Route`,
                                   sd.FullName AS `Department`,
                                   cp.DOM_CREDIT AS `Dom Credit`,
                                   cp.LOCAL_CREDIT AS `Local Credit`,
                                   cp.LOCAL_DLD AS `Local DLD`,
                                   cp.PMCL AS `PMCL`,
                                   cp.DomesticDelivery AS `Dom Delivery`,
                                   cp.INTL_CREDIT AS `Intl Credit`,
                                   cp.COD AS `COD`,
                                   cp.OVERNIGHT AS `Overnight`,
                                   cp.YB1KG AS `YB 1KG`, cp.YB2KG AS `YB 2KG`,
                                   cp.YB5KG AS `YB 5KG`, cp.YB10KG AS `YB 10KG`,
                                   cp.YB15KG AS `YB 15KG`, cp.YB25KG AS `YB 25KG`,
                                   cp.FLAYER AS `Flayer`,
                                   cp.OVERLAND AS `Overland`,
                                   cp.MTD AS `MTD`,
                                   cp.VAS AS `VAS`,
                                   cp.IntlDox AS `Intl Dox`,
                                   cp.IntlEconomy AS `Intl Economy`,
                                   cp.AllInOne AS `All In One`,
                                   cp.Insurance_Com AS `Insurance`,
                                   cp.Retail_Deduction AS `Retail Deduction`,
                                   cp.COD_Bonus AS `COD Bonus`,
                                   cp.COD_Deduction AS `COD Deduction`,
                                   ROUND(
                                       IFNULL(cp.DOM_CREDIT,0)+IFNULL(cp.LOCAL_CREDIT,0)+IFNULL(cp.LOCAL_DLD,0)+
                                       IFNULL(cp.PMCL,0)+IFNULL(cp.DomesticDelivery,0)+IFNULL(cp.INTL_CREDIT,0)+
                                       IFNULL(cp.COD,0)+IFNULL(cp.OVERNIGHT,0)+IFNULL(cp.YB1KG,0)+IFNULL(cp.YB2KG,0)+
                                       IFNULL(cp.YB5KG,0)+IFNULL(cp.YB10KG,0)+IFNULL(cp.YB15KG,0)+IFNULL(cp.YB25KG,0)+
                                       IFNULL(cp.FLAYER,0)+IFNULL(cp.DETAIN,0)+IFNULL(cp.OVERLAND,0)+
                                       IFNULL(cp.MTD,0)+IFNULL(cp.VAS,0)+IFNULL(cp.IntlDox,0)+IFNULL(cp.IntlEconomy,0)+
                                       IFNULL(cp.AllInOne,0)+IFNULL(cp.Insurance_Com,0)+IFNULL(cp.COD_Bonus,0)+
                                       IFNULL(cp.Pickup_Leopard,0), 0
                                   ) AS `Total Commission`,
                                   ROUND(IFNULL(cp.Retail_Deduction,0)+IFNULL(cp.COD_Deduction,0), 0) AS `Total Deduction`
                            FROM hr_commissionprocess cp
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = cp.emp_no
                            INNER JOIN hr_city c ON c.Code = cp.citycode
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = cp.citycode
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = cp.emp_no AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            WHERE lul.Userid = @UserId
                              AND cp.Year = @Year AND cp.Month = @Month
                              AND (@ReportMode IS NULL OR @ReportMode = '' OR (@ReportMode = 'Executive' AND p.IsExecutive = 1) OR (@ReportMode = 'NonExecutive' AND p.IsExecutive = 0))
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, cp.emp_no";
                        break;

                    case "LoanDetail":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   ded.Emp_No AS `Employee No`, p.NAME AS `Employee Name`,
                                   lt.FullName AS `Loan Type`,
                                   DATE_FORMAT(dis.DisbursedDate, '%d/%m/%Y') AS `Disbursed Date`,
                                   dis.DisbursedAmount AS `Disbursed Amount`,
                                   req.RequestInstallments AS `Total Installments`,
                                   DATE_FORMAT(dis.DeductionStartDate, '%d/%m/%Y') AS `Deduction Start`,
                                   (SELECT COUNT(*) FROM hr_employeeloandeduction x
                                    WHERE x.Emp_No = ded.Emp_No AND x.LD_No = ded.LD_No
                                      AND x.LoanCode = ded.LoanCode AND x.DeductionDate <= ded.DeductionDate) AS `Installment No`,
                                   DATE_FORMAT(ded.DeductionDate, '%d/%m/%Y') AS `Deduction Date`,
                                   ded.DeductionAmount AS `Deduction Amount`,
                                   ded.Balance AS `Balance`
                            FROM hr_employeeloandeduction ded
                            INNER JOIN hr_employeeloandisbursed dis ON dis.LD_No = ded.LD_No
                            INNER JOIN hr_employeeloanrequest req ON req.LR_No = dis.LR_No
                            INNER JOIN hr_loantypes lt ON lt.Code = ded.LoanCode
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = ded.Emp_No
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            WHERE lul.Userid = @UserId
                              AND YEAR(ded.DeductionDate) = @Year AND MONTH(ded.DeductionDate) = @Month
                              AND (@EmpNo IS NULL OR ded.Emp_No = @EmpNo)
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, ded.Emp_No, ded.DeductionDate";
                        break;

                    case "GratuityReport":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   p.EMP_NO AS `Employee No`, p.NAME AS `Employee Name`,
                                   p.NIC_NO AS `CNIC`,
                                   DATE_FORMAT(p.APPOINT_DATE, '%d/%m/%Y') AS `Appoint Date`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   TIMESTAMPDIFF(YEAR, p.APPOINT_DATE, IFNULL(p.LEFT_DATE, NOW())) AS `Years`,
                                   CONCAT(
                                       TIMESTAMPDIFF(YEAR, p.APPOINT_DATE, IFNULL(p.LEFT_DATE, NOW())), ' Yrs, ',
                                       MOD(TIMESTAMPDIFF(MONTH, p.APPOINT_DATE, IFNULL(p.LEFT_DATE, NOW())), 12), ' Months'
                                   ) AS `Service Duration`,
                                   ROUND(s.currentsalary, 0) AS `Basic Salary`,
                                   CAST(s.currentsalary *
                                       IF(MOD(TIMESTAMPDIFF(MONTH, p.APPOINT_DATE, IFNULL(p.LEFT_DATE, NOW())), 12) < 6,
                                          TIMESTAMPDIFF(YEAR, p.APPOINT_DATE, IFNULL(p.LEFT_DATE, NOW())),
                                          TIMESTAMPDIFF(YEAR, p.APPOINT_DATE, IFNULL(p.LEFT_DATE, NOW())) + 1)
                                   AS DECIMAL(18,2)) AS `Gratuity Amount`
                            FROM hr_employeepersonaldetail p
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            INNER JOIN (
                                SELECT Emp_No, MAX(SalaryYear * 100 + SalaryMonth) AS maxym
                                FROM hr_salaryprocessed_hdr GROUP BY Emp_No
                            ) lm ON lm.Emp_No = p.EMP_NO
                            INNER JOIN hr_salaryprocessed_hdr s ON s.Emp_No = p.EMP_NO
                                AND s.SalaryYear * 100 + s.SalaryMonth = lm.maxym
                            WHERE lul.Userid = @UserId
                              AND TIMESTAMPDIFF(YEAR, p.APPOINT_DATE, IFNULL(p.LEFT_DATE, NOW())) > 0
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            ORDER BY rz.FullName, c.FullName, p.EMP_NO";
                        break;

                    case "AttendanceSummaryChart":
                        query = @"
                            SELECT rz.FullName AS `Zone`, c.FullName AS `City`,
                                   pd.PDName AS `Department`, sd.FullName AS `Sub Department`,
                                   CONCAT(ap.Year, '-', LPAD(ap.Month, 2, '0')) AS `Month/Year`,
                                   COUNT(DISTINCT ap.emp_no) AS `Total Employees`,
                                   SUM(ap.Absents) AS `Absents`,
                                   SUM(ap.Sundays) AS `Sundays`,
                                   SUM(ap.Holidays) AS `Holidays`,
                                   SUM(ap.Leaves) AS `Leaves`,
                                   SUM(ap.Late) AS `Late`,
                                   SUM(ap.HalfDay) AS `Half Day`,
                                   SUM(ap.ruleAbsents) AS `Rule Absents`,
                                   SUM(ap.Notout) AS `Not Out`,
                                   SUM(ap.Early) AS `Early Go`
                            FROM hr_employeeattendanceprocess ap
                            INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = ap.emp_no
                            INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                            INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                            INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                            INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                            INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                            INNER JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID
                            WHERE ap.Year = @Year AND (@Month IS NULL OR ap.Month = @Month)
                              AND lul.Userid = @UserId
                              AND (@ZoneCode IS NULL OR rz.Code = @ZoneCode)
                              AND (@CityCode IS NULL OR c.Code = @CityCode)
                            GROUP BY rz.Code, c.Code, sd.SDID, ap.Year, ap.Month
                            ORDER BY rz.FullName, c.FullName, pd.PDName, sd.FullName";
                        break;

                    default:
                        throw new ArgumentException($"Report type '{model.ReportType}' is not supported yet.");
                }

                var rawData = await connection.QueryAsync(query, parameters);
                var result = new List<Dictionary<string, object>>();

                foreach (var row in rawData)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (var kvp in (IDictionary<string, object>)row)
                    {
                        dict.Add(kvp.Key, kvp.Value);
                    }
                    result.Add(dict);
                }

                return result;
            }
        }

        // -----------------------------------------------------------------------
        // AttendanceSummary: day-grid pivot per department (legacy parity)
        // Source: hr_employeeattendance (ZKTeco / biometric), same as legacy
        // -----------------------------------------------------------------------
        private async Task<List<Dictionary<string, object>>> BuildAttendanceSummaryAsync(
            MySqlConnection connection, ReportViewModel model, string currentUserId)
        {
            int year = model.Year;
            int month = model.Month;
            int numberOfDays = DateTime.DaysInMonth(year, month);

            var p = new DynamicParameters();
            p.Add("@UserId", currentUserId);
            p.Add("@Year", year);
            p.Add("@Month", month);
            p.Add("@ZoneCode", string.IsNullOrEmpty(model.ZoneCode) || model.ZoneCode == "00" ? null : model.ZoneCode);
            p.Add("@CityCode", string.IsNullOrEmpty(model.CityCode) || model.CityCode == "00" ? null : model.CityCode);
            p.Add("@DepartmentId", string.IsNullOrEmpty(model.DepartmentId) ? null : model.DepartmentId);

            // Step 1: Raw attendance records (emp_no + day) from biometric table
            const string attQuery = @"
                SELECT perDet.emp_no, attDta.Day AS att_day
                FROM hr_employeeattendance attDta
                INNER JOIN hr_employeepersonaldetail perDet ON attDta.emp_no = perDet.emp_no
                INNER JOIN hr_employeedepartmentdetails deptDet ON perDet.emp_no = deptDet.emp_no AND deptDet.ToDate IS NULL
                WHERE attDta.status <> 'D'
                  AND attDta.Month = @Month AND attDta.Year = @Year
                  AND perDet.P_CITY_CODE IN (SELECT city_code FROM lcs_user_location WHERE Userid = @UserId)
                  AND (@ZoneCode IS NULL OR perDet.P_CITY_CODE IN (SELECT c.Code FROM hr_city c WHERE c.RZoneCode = @ZoneCode))
                  AND (@CityCode IS NULL OR perDet.P_CITY_CODE = @CityCode)
                  AND (@DepartmentId IS NULL OR deptDet.DeptCode = @DepartmentId)";

            // Step 2: Distinct departments that appear in the attendance data for the month
            const string deptQuery = @"
                SELECT DISTINCT dept.SDID AS Code, dept.FullName,
                       City.Code AS CityID, City.FullName AS City
                FROM hr_employeeattendance attDta
                INNER JOIN hr_employeepersonaldetail pd ON pd.EMP_NO = attDta.emp_no
                INNER JOIN hr_employeedepartmentdetails deptDet ON pd.EMP_NO = deptDet.emp_no AND deptDet.ToDate IS NULL
                INNER JOIN hr_subdepartment dept ON deptDet.DeptCode = dept.SDID
                INNER JOIN hr_city City ON pd.P_CITY_CODE = City.Code
                WHERE attDta.Status <> 'D'
                  AND attDta.Month = @Month AND attDta.Year = @Year
                  AND City.Code IN (SELECT city_code FROM lcs_user_location WHERE Userid = @UserId)
                  AND (@ZoneCode IS NULL OR City.RZoneCode = @ZoneCode)
                  AND (@CityCode IS NULL OR pd.P_CITY_CODE = @CityCode)
                  AND (@DepartmentId IS NULL OR deptDet.DeptCode = @DepartmentId)
                ORDER BY City.FullName, dept.FullName";

            // Step 3: All current department assignments (ToDate IS NULL = current)
            const string deptDetailsQuery = @"
                SELECT Emp_No, DeptCode FROM hr_employeedepartmentdetails WHERE ToDate IS NULL";

            var attRows = (await connection.QueryAsync(attQuery, p)).ToList();
            var departments = (await connection.QueryAsync(deptQuery, p)).ToList();
            var deptDetails = (await connection.QueryAsync(deptDetailsQuery)).ToList();

            if (!departments.Any()) return new List<Dictionary<string, object>>();

            // Pre-index: attendance by (deptCode, day)
            // We need: for each (dept, day) -> distinct emp_nos who have an attendance record
            // deptDetails gives us: emp_no -> deptCode mapping
            var empToDept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in deptDetails)
            {
                string empNo = d.Emp_No?.ToString() ?? "";
                string deptCode = d.DeptCode?.ToString() ?? "";
                if (!string.IsNullOrEmpty(empNo))
                    empToDept[empNo] = deptCode;
            }

            // attendance[deptCode][day] = set of emp_nos present
            var attByDeptDay = new Dictionary<string, Dictionary<int, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in attRows)
            {
                string empNo = a.emp_no?.ToString() ?? "";
                int day = (int)a.att_day;
                if (!empToDept.TryGetValue(empNo, out var deptCode)) continue;
                if (!attByDeptDay.TryGetValue(deptCode, out var byDay))
                {
                    byDay = new Dictionary<int, HashSet<string>>();
                    attByDeptDay[deptCode] = byDay;
                }
                if (!byDay.TryGetValue(day, out var empSet))
                {
                    empSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    byDay[day] = empSet;
                }
                empSet.Add(empNo);
            }

            // empsPerDept[deptCode] = total employees currently in dept
            var empsPerDept = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in deptDetails)
            {
                string empNo = d.Emp_No?.ToString() ?? "";
                string deptCode = d.DeptCode?.ToString() ?? "";
                if (string.IsNullOrEmpty(empNo) || string.IsNullOrEmpty(deptCode)) continue;
                if (!empsPerDept.TryGetValue(deptCode, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    empsPerDept[deptCode] = set;
                }
                set.Add(empNo);
            }

            var result = new List<Dictionary<string, object>>();
            foreach (var dept in departments)
            {
                string deptCode = dept.Code?.ToString() ?? "";
                var row = new Dictionary<string, object>();
                row["City"] = dept.City;
                row["Department"] = dept.FullName;

                int totalEmps = empsPerDept.TryGetValue(deptCode, out var empSet) ? empSet.Count : 0;
                attByDeptDay.TryGetValue(deptCode, out var dayMap);

                for (int day = 1; day <= numberOfDays; day++)
                {
                    string colName = day.ToString("00");
                    int presentCount = dayMap != null && dayMap.TryGetValue(day, out var presentSet)
                        ? presentSet.Count : 0;
                    row[colName] = presentCount.ToString();
                    row["TE_" + colName] = totalEmps.ToString();
                }
                result.Add(row);
            }
            return result;
        }

        // -----------------------------------------------------------------------
        // AttendanceTimeSpan: per-employee per-day time span from biometric data
        // Source: hr_employeeattendance (ZKTeco punches), same as legacy
        // Shift logic: hr_employeeshifttimings + hr_shiftdetails
        // Adjustments: hr_employeeattandenceadjust
        // Holidays: hr_gazetted_holidays
        // -----------------------------------------------------------------------
        private async Task<List<Dictionary<string, object>>> BuildAttendanceTimeSpanAsync(
            MySqlConnection connection, ReportViewModel model, string currentUserId)
        {
            if (model.FromDate == null || model.ToDate == null)
                return new List<Dictionary<string, object>>();

            int year = model.FromDate.Value.Year;
            int month = model.FromDate.Value.Month;
            int startDay = model.FromDate.Value.Day;
            int endDay = model.ToDate.Value.Day;

            // Legacy date window: prev-month-26 to this-month-25 (ZKTeco cycle)
            DateTime legacyFrom = month == 1
                ? new DateTime(year - 1, 12, 26)
                : new DateTime(year, month - 1, 26);
            DateTime legacyTo = new DateTime(year, month, 25);
            DateTime currentDate = new DateTime(year, month, 25);

            string zoneCode = string.IsNullOrEmpty(model.ZoneCode) || model.ZoneCode == "00" ? null : model.ZoneCode;
            string cityCode = string.IsNullOrEmpty(model.CityCode) || model.CityCode == "00" ? null : model.CityCode;

            // Employees with their most-recent shift details
            string empQuery = @"
                SELECT p.EMP_NO AS emp_no, p.NAME AS Name,
                       sd.FullName AS Department,
                       p.P_CITY_CODE AS CityCode,
                       (SELECT sh.ShiftCode FROM hr_employeeshifttimings sh
                        WHERE sh.Emp_No = p.EMP_NO AND sh.FromDate <= @currDate
                        ORDER BY sh.ID DESC LIMIT 1) AS ShiftCode
                FROM hr_employeepersonaldetail p
                INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                INNER JOIN hr_employeedepartmentdetails deptDet ON deptDet.Emp_No = p.EMP_NO AND deptDet.ToDate IS NULL
                INNER JOIN hr_subdepartment sd ON sd.SDID = deptDet.DeptCode
                WHERE p.EMP_STATUS <> 'I' AND p.LEFT_DATE IS NULL
                  AND p.APPOINT_DATE <= @currDate
                  AND lul.Userid = @userId
                  AND (@zoneCode IS NULL OR c.RZoneCode = @zoneCode)
                  AND (@cityCode IS NULL OR p.P_CITY_CODE = @cityCode)
                ORDER BY p.EMP_NO";

            var epParams = new DynamicParameters();
            epParams.Add("@currDate", currentDate);
            epParams.Add("@userId", currentUserId);
            epParams.Add("@zoneCode", zoneCode);
            epParams.Add("@cityCode", cityCode);

            var employees = (await connection.QueryAsync(empQuery, epParams)).ToList();
            if (!employees.Any()) return new List<Dictionary<string, object>>();

            // Collect unique shift codes for batch lookup
            var shiftCodes = employees
                .Where(e => e.ShiftCode != null)
                .Select(e => (string)e.ShiftCode.ToString())
                .Distinct()
                .ToList();

            Dictionary<string, dynamic> shiftMap = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            if (shiftCodes.Any())
            {
                string shiftQuery = @"
                    SELECT Code, Start_Time, End_Time, NightShift
                    FROM hr_shiftdetails WHERE Code IN @Codes";
                var shifts = await connection.QueryAsync(shiftQuery, new { Codes = shiftCodes });
                foreach (var s in shifts)
                    shiftMap[s.Code.ToString()] = s;
            }

            // Biometric attendance logs for the cycle window
            string logsQuery = @"
                SELECT att.emp_no, att.CHECKTIME, att.Day, att.CHECKTYPE
                FROM hr_employeeattendance att
                WHERE att.Status <> 'D'
                  AND att.CHECKTIME BETWEEN @legFrom AND @legTo
                  AND att.Day >= @sDay AND att.Day <= @eDay";

            var lp = new DynamicParameters();
            lp.Add("@legFrom", legacyFrom);
            lp.Add("@legTo", legacyTo);
            lp.Add("@sDay", startDay);
            lp.Add("@eDay", endDay);

            var logs = (await connection.QueryAsync(logsQuery, lp)).ToList();

            // Holidays for the date range
            string holidayQuery = @"
                SELECT FromDate, ToDate
                FROM hr_gazetted_holidays
                WHERE FromDate BETWEEN @legFrom AND @legTo
                  AND Holiday_flag IN ('All',
                      (SELECT p.P_CITY_CODE FROM hr_employeepersonaldetail p
                       INNER JOIN lcs_user_location lul ON lul.city_code = p.P_CITY_CODE
                       WHERE lul.Userid = @userId LIMIT 1))";

            var hp = new DynamicParameters();
            hp.Add("@legFrom", legacyFrom);
            hp.Add("@legTo", legacyTo);
            hp.Add("@userId", currentUserId);
            var holidays = (await connection.QueryAsync(holidayQuery, hp)).ToList();

            // Build set of holiday day-numbers within the requested range
            var holidayDays = new HashSet<int>();
            foreach (var h in holidays)
            {
                DateTime hFrom = (DateTime)h.FromDate;
                DateTime hTo = (DateTime)h.ToDate;
                for (DateTime dt = hFrom; dt <= hTo; dt = dt.AddDays(1))
                {
                    if (dt.Year == year && dt.Month == month
                        && dt.Day >= startDay && dt.Day <= endDay
                        && dt.DayOfWeek != DayOfWeek.Sunday)
                        holidayDays.Add(dt.Day);
                }
            }

            // Attendance adjustments (columns: emp_no, adjustmentDate, adjustmentType)
            string adjQuery = @"
                SELECT adj.emp_no AS EmpNo, adj.adjustmentDate AS AttDate
                FROM hr_employeeattandenceadjust adj
                WHERE adj.adjustmentDate BETWEEN @adjFrom AND @adjTo";

            var ap = new DynamicParameters();
            ap.Add("@adjFrom", model.FromDate.Value.Date);
            ap.Add("@adjTo", model.ToDate.Value.Date);
            var adjustments = (await connection.QueryAsync(adjQuery, ap)).ToList();

            // Index logs by (emp_no, day) for fast access
            var logsByEmpDay = new Dictionary<string, Dictionary<int, List<dynamic>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in logs)
            {
                string empNo = l.emp_no?.ToString() ?? "";
                int day = (int)l.Day;
                if (!logsByEmpDay.TryGetValue(empNo, out var byDay))
                {
                    byDay = new Dictionary<int, List<dynamic>>();
                    logsByEmpDay[empNo] = byDay;
                }
                if (!byDay.TryGetValue(day, out var list))
                {
                    list = new List<dynamic>();
                    byDay[day] = list;
                }
                list.Add(l);
            }

            // Index adjustments by (emp_no, day)
            var adjByEmp = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in adjustments)
            {
                string empNo = a.EmpNo?.ToString() ?? "";
                int day = ((DateTime)a.AttDate).Day;
                if (!adjByEmp.TryGetValue(empNo, out var days))
                {
                    days = new HashSet<int>();
                    adjByEmp[empNo] = days;
                }
                days.Add(day);
            }

            var result = new List<Dictionary<string, object>>();

            foreach (var emp in employees)
            {
                string empNo = emp.emp_no?.ToString() ?? "";
                string shiftCode = emp.ShiftCode?.ToString();
                TimeSpan shiftStart = TimeSpan.Zero;
                TimeSpan shiftEnd = TimeSpan.Zero;
                bool nightShift = false;
                string shiftLabel = "N/A";

                if (shiftCode != null && shiftMap.TryGetValue(shiftCode, out var sd))
                {
                    shiftStart = sd.Start_Time != null ? (TimeSpan)sd.Start_Time : TimeSpan.Zero;
                    shiftEnd = sd.End_Time != null ? (TimeSpan)sd.End_Time : TimeSpan.Zero;
                    nightShift = sd.NightShift?.ToString() == "Y";
                    shiftLabel = $"{shiftStart:hh\\:mm}-{shiftEnd:hh\\:mm}";
                }

                TimeSpan shiftDuration;
                if (!nightShift)
                    shiftDuration = shiftEnd - shiftStart;
                else
                    shiftDuration = shiftEnd <= TimeSpan.FromHours(12) && shiftStart > TimeSpan.FromHours(12)
                        ? shiftEnd.Add(TimeSpan.FromHours(24)) - shiftStart
                        : shiftEnd - shiftStart;
                if (shiftDuration < TimeSpan.Zero) shiftDuration = TimeSpan.Zero;

                logsByEmpDay.TryGetValue(empNo, out var empDayLogs);
                adjByEmp.TryGetValue(empNo, out var empAdjDays);

                TimeSpan totalWorked = TimeSpan.Zero;
                TimeSpan officialWorked = TimeSpan.Zero;
                TimeSpan officialTotal = TimeSpan.Zero;

                for (int day = startDay; day <= endDay; day++)
                {
                    var date = new DateTime(year, month, day);
                    var row = new Dictionary<string, object>
                    {
                        ["Employee No"] = empNo,
                        ["Name"] = emp.Name,
                        ["Department"] = emp.Department,
                        ["Shift"] = shiftLabel,
                        ["Date"] = date,
                        ["Day"] = date.ToString("ddd")
                    };

                    if (date.DayOfWeek == DayOfWeek.Sunday)
                    {
                        row["In Time"] = "S"; row["Out Time"] = "S";
                        row["Time Span"] = "S"; row["Note"] = "Sunday";
                        result.Add(row); continue;
                    }
                    if (holidayDays.Contains(day))
                    {
                        row["In Time"] = "H"; row["Out Time"] = "H";
                        row["Time Span"] = "H"; row["Note"] = "Holiday";
                        result.Add(row); continue;
                    }

                    officialTotal += shiftDuration;
                    var dayLogs = empDayLogs != null && empDayLogs.TryGetValue(day, out var dl) ? dl : null;
                    bool isAdj = empAdjDays != null && empAdjDays.Contains(day);

                    DateTime? inTime = dayLogs?
                        .Where(l => l.CHECKTYPE?.ToString() == "I" && l.CHECKTIME != null)
                        .OrderBy(l => (DateTime)l.CHECKTIME)
                        .Select(l => (DateTime?)l.CHECKTIME)
                        .FirstOrDefault();
                    DateTime? outTime = dayLogs?
                        .Where(l => l.CHECKTYPE?.ToString() == "O" && l.CHECKTIME != null)
                        .OrderByDescending(l => (DateTime)l.CHECKTIME)
                        .Select(l => (DateTime?)l.CHECKTIME)
                        .FirstOrDefault();

                    string note = isAdj ? "Adj" : "P";

                    if (inTime.HasValue && outTime.HasValue)
                    {
                        TimeSpan span = outTime.Value - inTime.Value;
                        if (nightShift && span < TimeSpan.Zero) span = span.Add(TimeSpan.FromHours(24));
                        TimeSpan spanAbs = span.Duration();

                        row["In Time"] = inTime.Value.ToString("HH:mm");
                        row["Out Time"] = outTime.Value.ToString("HH:mm");
                        row["Time Span"] = $"{(int)spanAbs.TotalHours:00}:{spanAbs.Minutes:00}";
                        row["Note"] = note;
                        officialWorked += shiftDuration;
                        totalWorked += isAdj ? shiftDuration : spanAbs;
                    }
                    else if (inTime.HasValue)
                    {
                        row["In Time"] = inTime.Value.ToString("HH:mm");
                        row["Out Time"] = ""; row["Time Span"] = "A - 00:00"; row["Note"] = "No Out";
                    }
                    else if (outTime.HasValue)
                    {
                        row["In Time"] = ""; row["Out Time"] = outTime.Value.ToString("HH:mm");
                        row["Time Span"] = "A - 00:00"; row["Note"] = "No In";
                    }
                    else
                    {
                        row["In Time"] = "-"; row["Out Time"] = "-";
                        row["Time Span"] = "A - 00:00"; row["Note"] = isAdj ? "Adj" : "Absent";
                        if (isAdj) { officialWorked += shiftDuration; totalWorked += shiftDuration; }
                    }
                    result.Add(row);
                }

                // Summary row per employee (matches legacy OfficialTotalHours/OfficialWorkedHours/EmpTotalWorkingHours)
                result.Add(new Dictionary<string, object>
                {
                    ["Employee No"] = empNo,
                    ["Name"] = $"--- {emp.Name} ---",
                    ["Department"] = emp.Department,
                    ["Shift"] = shiftLabel,
                    ["Date"] = DBNull.Value,
                    ["Day"] = "TOTAL",
                    ["In Time"] = $"Official: {(int)officialTotal.TotalHours:00}:{officialTotal.Minutes:00}",
                    ["Out Time"] = $"Worked: {(int)totalWorked.TotalHours:00}:{totalWorked.Minutes:00}",
                    ["Time Span"] = $"Offcl Wrkd: {(int)officialWorked.TotalHours:00}:{officialWorked.Minutes:00}",
                    ["Note"] = ""
                });
            }
            return result;
        }

        public async Task<List<SelectListItem>> GetHrDepartmentsAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<SelectListItem>();
                await connection.OpenAsync();
                var rows = await connection.QueryAsync("SELECT SDID AS DeptId, FullName AS DeptName FROM hr_subdepartment ORDER BY FullName");
                return rows.Select(r => new SelectListItem { Value = r.DeptId.ToString(), Text = r.DeptName }).ToList();
            }
        }

        public byte[] GenerateExcelReport(List<Dictionary<string, object>> data, string reportName)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add(reportName);

                if (data != null && data.Count > 0)
                {
                    // Headers
                    var keys = data[0].Keys.ToList();
                    for (int i = 0; i < keys.Count; i++)
                    {
                        var cell = worksheet.Cells[1, i + 1];
                        cell.Value = keys[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                    }

                    // Data
                    for (int r = 0; r < data.Count; r++)
                    {
                        var rowDict = data[r];
                        for (int c = 0; c < keys.Count; c++)
                        {
                            var val = rowDict[keys[c]];
                            if (val != null)
                            {
                                if (val is DateTime dt)
                                {
                                    worksheet.Cells[r + 2, c + 1].Value = dt.ToString("yyyy-MM-dd");
                                }
                                else
                                {
                                    worksheet.Cells[r + 2, c + 1].Value = val;
                                }
                            }
                        }
                    }

                    worksheet.Cells.AutoFitColumns();
                }
                else
                {
                    worksheet.Cells[1, 1].Value = "No Data Found";
                }

                return package.GetAsByteArray();
            }
        }
    }
}
