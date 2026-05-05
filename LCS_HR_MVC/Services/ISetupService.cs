using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface ISetupService
    {
        Task<IEnumerable<CountryModel>> GetAllCountriesAsync();
        Task<bool> IsCountryExistsAsync(string fullName, string shortName, string? excludeCode = null);
        Task<bool> AddCountryAsync(CountryModel model, string currentUserId);
        Task<bool> UpdateCountryAsync(CountryModel model, string currentUserId);
        Task<bool> DeleteCountryAsync(string code);

        Task<IEnumerable<ProvinceModel>> GetAllProvincesAsync();
        Task<bool> IsProvinceExistsAsync(string countryCode, string fullName, string? excludeCode = null);
        Task<bool> AddProvinceAsync(ProvinceModel model, string currentUserId);
        Task<bool> UpdateProvinceAsync(ProvinceModel model, string currentUserId);
        Task<bool> DeleteProvinceAsync(string code);

        Task<IEnumerable<CityModel>> GetAllCitiesAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetZonesAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetProvincesByCountryAsync(string countryCode);
        Task<bool> IsCityExistsAsync(string fullName, string shortName, string? excludeCode = null);
        Task<bool> AddCityAsync(CityModel model, string currentUserId);
        Task<bool> UpdateCityAsync(CityModel model, string currentUserId);
        Task<bool> DeleteCityAsync(string code);
        Task<int> GetExtraFixedDaysAsync(string cityId);

        Task<IEnumerable<DepartmentModel>> GetAllDepartmentsAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCompaniesAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetBusinessUnitsAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetParentDepartmentsAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetParentDepartmentsByIDAsync(int companyId, int buId);
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetAllSubDepartmentsAsync();
        Task<bool> IsDepartmentExistsAsync(string fullName, string shortName, string companyId, string buId, bool isParent, string parentId, string? excludeSdid = null);
        Task<bool> AddDepartmentAsync(DepartmentModel model, string currentUserId);
        Task<bool> UpdateDepartmentAsync(DepartmentModel model, string currentUserId);
        Task<bool> DeleteDepartmentAsync(string sdid, string pdid);

        Task<IEnumerable<DivisionModel>> GetAllDivisionsAsync();
        Task<bool> IsDivisionExistsAsync(string fullName, string shortName, string? excludeId = null);
        Task<bool> AddDivisionAsync(DivisionModel model, string currentUserId);
        Task<bool> UpdateDivisionAsync(DivisionModel model, string currentUserId);

        Task<IEnumerable<JobModel>> GetAllJobsAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetSubDepartmentsByParentAsync(string parentId);
        Task<bool> IsJobExistsAsync(string fullName, string shortName, string? excludeCode = null);
        Task<bool> AddJobAsync(JobModel model, string currentUserId);
        Task<bool> UpdateJobAsync(JobModel model, string currentUserId);
        Task<bool> DeleteJobAsync(string code);

        Task<IEnumerable<EmployeeTypeModel>> GetAllEmployeeTypesAsync();
        Task<bool> IsEmployeeTypeExistsAsync(string fullName, string shortName, string? excludeCode = null);
        Task<bool> AddEmployeeTypeAsync(EmployeeTypeModel model, string currentUserId);
        Task<bool> UpdateEmployeeTypeAsync(EmployeeTypeModel model, string currentUserId);
        Task<bool> DeleteEmployeeTypeAsync(string code);

        Task<IEnumerable<RegionalZoneModel>> GetAllRegionalZonesAsync();
        Task<bool> IsRegionalZoneExistsAsync(string fullName, string shortName, string? excludeCode = null);
        Task<bool> AddRegionalZoneAsync(RegionalZoneModel model, string currentUserId);
        Task<bool> UpdateRegionalZoneAsync(RegionalZoneModel model, string currentUserId);
        Task<bool> DeleteRegionalZoneAsync(string code);

        Task<IEnumerable<SalaryBankModel>> GetAllSalaryBanksAsync(string currentUserId);
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCitiesByUserAsync(string currentUserId);
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetBanksByCityAsync(string cityCode);
        Task<bool> IsSalaryBankExistsAsync(string cityId, string? excludeCode = null);
        Task<bool> AddSalaryBankAsync(SalaryBankModel model, string currentUserId, string bankDesc);
        Task<bool> UpdateSalaryBankAsync(SalaryBankModel model, string currentUserId, string bankDesc);
        Task<bool> DeleteSalaryBankAsync(string code);

        Task<IEnumerable<LoanTypeModel>> GetAllLoanTypesAsync();
        Task<bool> IsLoanTypeExistsAsync(string fullName, string shortName, string? excludeCode = null);
        Task<bool> AddLoanTypeAsync(LoanTypeModel model, string currentUserId);
        Task<bool> UpdateLoanTypeAsync(LoanTypeModel model, string currentUserId);
        Task<bool> DeleteLoanTypeAsync(string code);

        Task<IEnumerable<ShiftModel>> GetAllShiftsAsync();
        Task<bool> IsShiftExistsAsync(string name, string? excludeCode = null);
        Task<bool> AddShiftAsync(ShiftModel model, string currentUserId);
        Task<bool> UpdateShiftAsync(ShiftModel model, string currentUserId);
        Task<bool> DeleteShiftAsync(string code);

        Task<IEnumerable<AllowanceDeductionModel>> GetAllAllowanceDeductionsAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetAllowanceCodeTypesAsync();
        Task<bool> AddAllowanceDeductionAsync(AllowanceDeductionModel model, string currentUserId);
        Task<bool> UpdateAllowanceDeductionAsync(AllowanceDeductionModel model, string currentUserId);
        Task<bool> DeleteAllowanceDeductionAsync(string id);

        Task<IEnumerable<AllowanceDeductionDetailModel>> GetAllAllowanceDeductionDetailsAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetAllowanceTypesAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCommissionPolicyRatesAsync();
        Task<AllowanceDeductionDetailModel?> GetAllowanceDeductionDetailByIdAsync(string id);
        Task<bool> AddAllowanceDeductionDetailAsync(AllowanceDeductionDetailModel model, string currentUserId);
        Task<bool> UpdateAllowanceDeductionDetailAsync(AllowanceDeductionDetailModel model, string currentUserId);
        Task<bool> DeleteAllowanceDeductionDetailAsync(string id);

        Task<IEnumerable<GradeAllowanceModel>> GetAllGradeAllowancesAsync();
        Task<GradeAllowanceModel?> GetGradeAllowanceByCodeAsync(string code);
        Task<bool> IsGradeAllowanceExistsAsync(string type, string fullName, string? excludeCode = null);
        Task<bool> AddGradeAllowanceAsync(GradeAllowanceModel model, string currentUserId);
        Task<bool> UpdateGradeAllowanceAsync(GradeAllowanceModel model, string currentUserId);
        Task<bool> DeleteGradeAllowanceAsync(string code);
        Task<IEnumerable<dynamic>> SearchDepartmentsAsync(string term);
        Task<string?> GetGlCodeByShortNameAsync(string shortName);

        Task<IEnumerable<CompanyAssetModel>> GetAllCompanyAssetsAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetAssetTypesAsync();
        Task<List<string>> GetAssetStructureLabelsAsync(string typeCode);
        Task<bool> IsCompanyAssetExistsAsync(string name, string? excludeCode = null);
        Task<bool> AddCompanyAssetAsync(CompanyAssetModel model, string currentUserId);
        Task<bool> UpdateCompanyAssetAsync(CompanyAssetModel model, string currentUserId);
        Task<bool> DeleteCompanyAssetAsync(string code);

        Task<IEnumerable<AssetStructureModel>> GetAllAssetStructuresAsync();
        Task<bool> IsAssetStructureExistsAsync(string description, string? excludeCode = null);
        Task<bool> AddAssetStructureAsync(AssetStructureModel model, string currentUserId);
        Task<bool> UpdateAssetStructureAsync(AssetStructureModel model, string currentUserId);
        Task<bool> DeleteAssetStructureAsync(string code);

        Task<IEnumerable<AttendanceRuleModel>> GetAllAttendanceRulesAsync();
        Task<bool> IsAttendanceRuleExistsAsync(string leaveName, string? excludeCode = null);
        Task<bool> AddAttendanceRuleAsync(AttendanceRuleModel model, string currentUserId);
        Task<bool> UpdateAttendanceRuleAsync(AttendanceRuleModel model, string currentUserId);
        Task<bool> DeleteAttendanceRuleAsync(string code);

        Task<IEnumerable<CommissionRateModel>> GetAllCommissionRatesAsync(string currentUserId);
        Task<bool> IsCommissionRateExistsAsync(string cityCode, string? excludeCode = null);
        Task<bool> AddCommissionRateAsync(CommissionRateModel model, string currentUserId);
        Task<bool> UpdateCommissionRateAsync(CommissionRateModel model, string currentUserId);
        Task<bool> DeleteCommissionRateAsync(string code);

        Task<IEnumerable<CommissionEligibilityListModel>> GetAllCommissionEligibilitiesAsync();
        Task<CommissionEligibilityModel?> GetCommissionEligibilityByEmpNoAsync(string empNo);
        Task<IEnumerable<dynamic>> SearchActiveEmployeesAsync(string term);
        Task<bool> SaveCommissionEligibilityAsync(CommissionEligibilityModel model, string currentUserId);

        Task<IEnumerable<LeaveStructureModel>> GetAllLeaveStructuresAsync();
        Task<bool> AddLeaveStructureAsync(LeaveStructureModel model, string currentUserId);
        Task<bool> UpdateLeaveStructureAsync(LeaveStructureModel model, string currentUserId);
        Task<bool> DeleteLeaveStructureAsync(string code);

        Task<IEnumerable<DepartmentStrengthModel>> GetDepartmentStrengthsByCityAsync(string cityCode);
        Task<bool> UpdateDepartmentStrengthAsync(string cityId, string pdid, string sdid, int strength, string currentUserId);

        Task<IEnumerable<GazettedHolidayModel>> GetAllGazettedHolidaysAsync();
        Task<bool> AddGazettedHolidayAsync(GazettedHolidayModel model, string currentUserId);
        Task<bool> UpdateGazettedHolidayAsync(GazettedHolidayModel model, string currentUserId);
        Task<bool> DeleteGazettedHolidayAsync(string code);

        Task<int> InsertEmpHierarchyAsync(List<HRHierarchyModel> empHierarchyInfo, string currentUserId);
        Task<IEnumerable<dynamic>> GetEmployeesBySubDepartmentAsync(string deptId, string subDeptId);
        Task<IEnumerable<dynamic>> GetReportedEmployeesAsync(string reportToId);

        // GL Locations
        Task<IEnumerable<GLLocationModel>> GetAllGLLocationsAsync();
        Task<bool> IsGLLocationExistsAsync(string description, string? excludeCode = null);
        Task<bool> AddGLLocationAsync(GLLocationModel model, string currentUserId);
        Task<bool> UpdateGLLocationAsync(GLLocationModel model, string currentUserId);
        Task<bool> DeleteGLLocationAsync(string code);

        // Assign Multiple Locations
        Task<IEnumerable<SetupLocationModel>> GetSetupLocationsAsync();
        Task<List<int>> GetAssignedLocationsByEmpAsync(string empNo);
        Task<bool> SaveAssignedLocationsAsync(string empNo, List<int> locIds, string currentUserId);

        // Location Coordinate Update
        Task<IEnumerable<LocationCoordinateModel>> GetLocationsWithCoordinatesAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCitiesByZoneAsync(string zoneCode);
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetLocationsByCityAsync(string cityId);
        Task<bool> UpdateLocationCoordinatesAsync(int locationId, string cityId, string latitude, string longitude, string currentUserId);

        // Employee GL Code Generation
        Task<(int generated, string message)> GenerateEmpGlCodesAsync(string currentUserId);

        // Employee Personal Detail Lookups
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCountriesSelectAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCitiesByCountryAsync(string countryCode);
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetEmployeeTypesSelectAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetDivisionsSelectAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetJobTypesAsync();
        Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetThirdPartiesAsync();
    }
}
