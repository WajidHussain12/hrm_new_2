namespace LCS_HR_MVC.Data
{
    /// <summary>
    /// Resolves commission output table names.
    /// In test mode, all inserts go to _AC_Test mirror tables.
    /// Switch modes by changing CommissionSettings:UseTestTables in appsettings.json.
    ///
    /// TO SWITCH TO PRODUCTION:
    ///   1. Open appsettings.json
    ///   2. Change: "UseTestTables": true  →  "UseTestTables": false
    ///   3. Restart the application
    ///   4. Done — all inserts go to real tables immediately
    ///
    ///   No code changes required.
    ///   No recompile required.
    ///
    /// TO SWITCH BACK TO TEST:
    ///   1. "UseTestTables": false → "UseTestTables": true
    ///   2. Restart
    ///   3. Optional: POST /ac-test/truncate to clear old test data
    /// </summary>
    public static class AcTestTableNames
    {
        // ── Real table names (never change these) ─────────────────────────────
        public const string CashConsignments        = "hr_cash_consignments";
        public const string VasIncentiveDetail      = "hr_vas_incentive_detail";
        public const string CodConsignments         = "hr_cod_consignments";
        public const string AllCodConsignment       = "hr_all_cod_consignment";
        public const string CodReturnShipments      = "cod_returnshipments";
        public const string CodCommission           = "hr_codcommission";
        public const string OleCommission           = "hr_olecommission";
        public const string RbiIncentiveDetail      = "hr_rbi_incentive_detail";
        public const string OleCommissionProcess    = "hr_olecommissionprocess";
        public const string CodReturnConsignments   = "hr_codreturn_consignments";
        public const string CodReturnCommission     = "hr_codreturncommission";
        public const string CodReturnCommissionProc = "hr_codreturncommissionprocess";
        public const string CommissionProcess       = "hr_commissionprocess";
        public const string EmpCommAdjDtl           = "hr_empcommadjdtl";
        public const string Acknowledgment          = "hr_acknowledgment";

        // ── Test table suffix ──────────────────────────────────────────────────
        public const string TestSuffix = "_AC_Test";

        // ── All 15 output tables that have _AC_Test mirrors ────────────────────
        public static readonly IReadOnlyList<string> AllOutputTables =
            new[]
            {
                CashConsignments, VasIncentiveDetail,
                CodConsignments, AllCodConsignment, CodReturnShipments,
                CodCommission, OleCommission, RbiIncentiveDetail,
                OleCommissionProcess, CodReturnConsignments,
                CodReturnCommission, CodReturnCommissionProc,
                CommissionProcess, EmpCommAdjDtl, Acknowledgment
            };

        private static bool _useTestTables;

        /// <summary>Call once at startup with value from appsettings.</summary>
        public static void Initialize(bool useTestTables)
        {
            _useTestTables = useTestTables;
        }

        /// <summary>
        /// Returns the correct table name based on current mode.
        /// In test mode:  hr_cash_consignments → hr_cash_consignments_AC_Test
        /// In prod mode:  hr_cash_consignments → hr_cash_consignments
        /// </summary>
        public static string Resolve(string realTableName)
            => _useTestTables ? realTableName + TestSuffix : realTableName;

        public static bool IsTestMode => _useTestTables;

        // ── Convenience properties — use these in commission services ──────────
        public static string T_CashConsignments        => Resolve(CashConsignments);
        public static string T_VasIncentiveDetail      => Resolve(VasIncentiveDetail);
        public static string T_CodConsignments         => Resolve(CodConsignments);
        public static string T_AllCodConsignment       => Resolve(AllCodConsignment);
        public static string T_CodReturnShipments      => Resolve(CodReturnShipments);
        public static string T_CodCommission           => Resolve(CodCommission);
        public static string T_OleCommission           => Resolve(OleCommission);
        public static string T_RbiIncentiveDetail      => Resolve(RbiIncentiveDetail);
        public static string T_OleCommissionProcess    => Resolve(OleCommissionProcess);
        public static string T_CodReturnConsignments   => Resolve(CodReturnConsignments);
        public static string T_CodReturnCommission     => Resolve(CodReturnCommission);
        public static string T_CodReturnCommissionProc => Resolve(CodReturnCommissionProc);
        public static string T_CommissionProcess       => Resolve(CommissionProcess);
        public static string T_EmpCommAdjDtl           => Resolve(EmpCommAdjDtl);
        public static string T_Acknowledgment          => Resolve(Acknowledgment);
    }
}
