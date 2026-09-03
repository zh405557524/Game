using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ProjectRealm.Domain
{
    public enum GovernmentBudgetCategory
    {
        Administration = 0,
        Military = 1,
        Relief = 2,
        DebtService = 3,
        ExistingProject = 4,
        Education = 5,
        Infrastructure = 6,
        EconomicDevelopment = 7,
        Investigation = 8
    }

    public enum GovernmentActionKind
    {
        Edict = 0,
        Construction = 1,
        Appointment = 2,
        MilitaryOperation = 3,
        Relief = 4,
        Investigation = 5,
        Borrowing = 6,
        TaxAdjustment = 7
    }

    public enum GovernmentRiskKind
    {
        FoodShortage = 0,
        FiscalShortfall = 1,
        MilitaryThreat = 2,
        Disorder = 3,
        Epidemic = 4,
        NaturalDisaster = 5,
        LogisticsFailure = 6,
        InformationGap = 7,
        EducationDeficit = 8,
        ProductionCollapse = 9
    }

    public enum MonthlyCrisisKind
    {
        None = 0,
        War = 1,
        MajorDisaster = 2,
        Epidemic = 3,
        Rebellion = 4,
        CriticalProjectFailure = 5,
        KeyOfficialReplacement = 6
    }

    public enum MonthlyAdjustmentKind
    {
        None = 0,
        ReserveReallocation = 1,
        EmergencyReplan = 2
    }

    public sealed class GovernmentReportingPolicy
    {
        public GovernmentReportingPolicy(
            int reportingDelayMonths,
            decimal completeness,
            decimal confidence,
            decimal reportedValueFactor)
        {
            if (reportingDelayMonths < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reportingDelayMonths));
            }

            LedgerContractGuard.RequireRatio(completeness, nameof(completeness));
            LedgerContractGuard.RequireRatio(confidence, nameof(confidence));
            LedgerContractGuard.RequireNonNegative(reportedValueFactor, nameof(reportedValueFactor));

            ReportingDelayMonths = reportingDelayMonths;
            Completeness = completeness;
            Confidence = confidence;
            ReportedValueFactor = reportedValueFactor;
        }

        public int ReportingDelayMonths { get; }

        public decimal Completeness { get; }

        public decimal Confidence { get; }

        /// <summary>
        /// Deterministic testable distortion. One means accurate; values below or
        /// above one model omission, corruption or estimation without exposing truth.
        /// </summary>
        public decimal ReportedValueFactor { get; }
    }

    public sealed class ReportedEconomicSector
    {
        internal ReportedEconomicSector(
            EconomicSectorKind sector,
            decimal grossOutput,
            decimal intermediateConsumption,
            decimal valueAdded,
            decimal realValueAdded,
            decimal capacityUtilization)
        {
            Sector = sector;
            GrossOutput = grossOutput;
            IntermediateConsumption = intermediateConsumption;
            ValueAdded = valueAdded;
            RealValueAdded = realValueAdded;
            CapacityUtilization = capacityUtilization;
        }

        public EconomicSectorKind Sector { get; }

        public decimal GrossOutput { get; }

        public decimal IntermediateConsumption { get; }

        public decimal ValueAdded { get; }

        public decimal RealValueAdded { get; }

        public decimal CapacityUtilization { get; }
    }

    public sealed class ReportedEconomicSummary
    {
        internal ReportedEconomicSummary(
            string valuationUnit,
            int referencePriceYear,
            decimal grossOutput,
            decimal intermediateConsumption,
            decimal valueAdded,
            decimal realValueAdded,
            IReadOnlyList<ReportedEconomicSector> sectors)
        {
            ValuationUnit = valuationUnit;
            ReferencePriceYear = referencePriceYear;
            GrossOutput = grossOutput;
            IntermediateConsumption = intermediateConsumption;
            ValueAdded = valueAdded;
            RealValueAdded = realValueAdded;
            Sectors = sectors;
        }

        public string ValuationUnit { get; }

        public int ReferencePriceYear { get; }

        public decimal GrossOutput { get; }

        public decimal IntermediateConsumption { get; }

        public decimal ValueAdded { get; }

        public decimal RealValueAdded { get; }

        public IReadOnlyList<ReportedEconomicSector> Sectors { get; }
    }

    public sealed class ReportedFiscalSummary
    {
        internal ReportedFiscalSummary(
            decimal assessedRevenue,
            decimal collectedRevenue,
            decimal transfersReportedSent,
            decimal revenueReceivable,
            decimal revenueInTransit,
            decimal paymentArrears,
            decimal debtOutstanding,
            decimal reportedLocalTreasury)
        {
            AssessedRevenue = assessedRevenue;
            CollectedRevenue = collectedRevenue;
            TransfersReportedSent = transfersReportedSent;
            RevenueReceivable = revenueReceivable;
            RevenueInTransit = revenueInTransit;
            PaymentArrears = paymentArrears;
            DebtOutstanding = debtOutstanding;
            ReportedLocalTreasury = reportedLocalTreasury;
        }

        public decimal AssessedRevenue { get; }

        public decimal CollectedRevenue { get; }

        public decimal TransfersReportedSent { get; }

        public decimal RevenueReceivable { get; }

        public decimal RevenueInTransit { get; }

        public decimal PaymentArrears { get; }

        public decimal DebtOutstanding { get; }

        public decimal ReportedLocalTreasury { get; }
    }

    public sealed class ReportedMilitarySummary
    {
        internal ReportedMilitarySummary(
            decimal troopStrength,
            decimal fitForDutyTroops,
            decimal landTransportCapacityKg,
            decimal navalTransportCapacityKg,
            decimal averageMaterielServiceability)
        {
            TroopStrength = troopStrength;
            FitForDutyTroops = fitForDutyTroops;
            LandTransportCapacityKg = landTransportCapacityKg;
            NavalTransportCapacityKg = navalTransportCapacityKg;
            AverageMaterielServiceability = averageMaterielServiceability;
        }

        public decimal TroopStrength { get; }

        public decimal FitForDutyTroops { get; }

        public decimal LandTransportCapacityKg { get; }

        public decimal NavalTransportCapacityKg { get; }

        public decimal AverageMaterielServiceability { get; }
    }

    public sealed class GovernmentReport
    {
        internal GovernmentReport(
            StableId reportId,
            StableId recipientAuthorityId,
            StableId sourceJurisdictionId,
            int observedYear,
            LedgerPeriod deliveredPeriod,
            int reportingDelayMonths,
            decimal completeness,
            decimal confidence,
            string sourceLedgerFingerprint,
            ReportedEconomicSummary economy,
            ReportedFiscalSummary fiscal,
            ReportedMilitarySummary military,
            IReadOnlyList<LedgerMetric> reportedIndicators,
            IReadOnlyList<StableId> reportedCauseIds)
        {
            ReportId = reportId;
            RecipientAuthorityId = recipientAuthorityId;
            SourceJurisdictionId = sourceJurisdictionId;
            ObservedYear = observedYear;
            DeliveredPeriod = deliveredPeriod;
            ReportingDelayMonths = reportingDelayMonths;
            Completeness = completeness;
            Confidence = confidence;
            SourceLedgerFingerprint = sourceLedgerFingerprint;
            Economy = economy;
            Fiscal = fiscal;
            Military = military;
            ReportedIndicators = reportedIndicators;
            ReportedCauseIds = reportedCauseIds;
        }

        public StableId ReportId { get; }

        public StableId RecipientAuthorityId { get; }

        public StableId SourceJurisdictionId { get; }

        public int ObservedYear { get; }

        public LedgerPeriod DeliveredPeriod { get; }

        public int ReportingDelayMonths { get; }

        public decimal Completeness { get; }

        public decimal Confidence { get; }

        /// <summary>
        /// Audit pointer only. The report deliberately does not expose its source ledger.
        /// </summary>
        public string SourceLedgerFingerprint { get; }

        public ReportedEconomicSummary Economy { get; }

        public ReportedFiscalSummary Fiscal { get; }

        public ReportedMilitarySummary Military { get; }

        public IReadOnlyList<LedgerMetric> ReportedIndicators { get; }

        public IReadOnlyList<StableId> ReportedCauseIds { get; }
    }

    public sealed class GovernmentReportService
    {
        public GovernmentReport BuildGovernmentReport(
            AnnualClosingLedger source,
            StableId reportId,
            StableId recipientAuthorityId,
            LedgerPeriod deliveredPeriod,
            GovernmentReportingPolicy policy)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            LedgerContractGuard.RequireId(reportId, nameof(reportId));
            LedgerContractGuard.RequireId(recipientAuthorityId, nameof(recipientAuthorityId));
            if (deliveredPeriod == null || deliveredPeriod.Kind != LedgerPeriodKind.Monthly)
            {
                throw new ArgumentException("Government reports require a monthly delivery period.", nameof(deliveredPeriod));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            var factor = policy.ReportedValueFactor;
            var sectors = new List<ReportedEconomicSector>();
            for (var index = 0; index < source.EconomicOutput.Sectors.Count; index++)
            {
                var sector = source.EconomicOutput.Sectors[index];
                sectors.Add(new ReportedEconomicSector(
                    sector.Sector,
                    sector.NominalGrossOutput * factor,
                    sector.NominalIntermediateConsumption * factor,
                    sector.NominalValueAdded * factor,
                    sector.RealValueAddedAtReferencePrices * factor,
                    sector.CapacityUtilization));
            }

            var economy = new ReportedEconomicSummary(
                source.EconomicOutput.ValuationUnit,
                source.EconomicOutput.ReferencePriceYear,
                source.EconomicOutput.NominalGrossOutput * factor,
                source.EconomicOutput.NominalIntermediateConsumption * factor,
                source.EconomicOutput.NominalValueAdded * factor,
                source.EconomicOutput.RealValueAddedAtReferencePrices * factor,
                new ReadOnlyCollection<ReportedEconomicSector>(sectors));
            var fiscal = new ReportedFiscalSummary(
                source.Fiscal.AssessedRevenue * factor,
                source.Fiscal.CollectedRevenue * factor,
                source.Fiscal.TransfersSent * factor,
                source.Fiscal.RevenueReceivableClosing * factor,
                source.Fiscal.RevenueInTransitClosing * factor,
                source.Fiscal.PaymentArrearsClosing * factor,
                source.Fiscal.DebtOutstandingClosing * factor,
                source.Fiscal.ClosingTreasury * factor);

            var serviceabilityTotal = 0m;
            var serviceabilityWeight = 0m;
            for (var index = 0; index < source.MilitaryMateriel.Materiel.Count; index++)
            {
                var item = source.MilitaryMateriel.Materiel[index];
                serviceabilityTotal += item.ServiceabilityRate * item.Flow.Closing;
                serviceabilityWeight += item.Flow.Closing;
            }

            var military = new ReportedMilitarySummary(
                source.MilitaryMateriel.TroopStrength * factor,
                source.MilitaryMateriel.FitForDutyTroops * factor,
                source.MilitaryMateriel.LandTransportCapacityKg * factor,
                source.MilitaryMateriel.NavalTransportCapacityKg * factor,
                serviceabilityWeight <= 0m ? 0m : serviceabilityTotal / serviceabilityWeight);

            var indicators = new List<LedgerMetric>();
            for (var index = 0; index < source.CapacityAndStock.DecisionIndicators.Count; index++)
            {
                var metric = source.CapacityAndStock.DecisionIndicators[index];
                indicators.Add(new LedgerMetric(
                    metric.MetricId,
                    metric.Domain,
                    metric.Unit,
                    metric.Value * factor,
                    metric.AggregationMode,
                    metric.Weight));
            }

            var causeIds = new List<StableId>();
            for (var index = 0; index < source.AppliedDrivers.Count; index++)
            {
                causeIds.Add(source.AppliedDrivers[index].DriverId);
            }

            return new GovernmentReport(
                reportId,
                recipientAuthorityId,
                source.JurisdictionId,
                source.Year,
                deliveredPeriod,
                policy.ReportingDelayMonths,
                policy.Completeness,
                policy.Confidence,
                source.StateFingerprint,
                economy,
                fiscal,
                military,
                new ReadOnlyCollection<LedgerMetric>(indicators),
                new ReadOnlyCollection<StableId>(causeIds));
        }
    }

    public sealed class GovernmentRiskSignal
    {
        public GovernmentRiskSignal(
            StableId riskId,
            GovernmentRiskKind kind,
            StableId scopeId,
            decimal severity,
            decimal confidence,
            StableId? sourceReportId = null)
        {
            LedgerContractGuard.RequireId(riskId, nameof(riskId));
            LedgerContractGuard.RequireId(scopeId, nameof(scopeId));
            LedgerContractGuard.RequireNullableId(sourceReportId, nameof(sourceReportId));
            LedgerContractGuard.RequireRatio(severity, nameof(severity));
            LedgerContractGuard.RequireRatio(confidence, nameof(confidence));

            RiskId = riskId;
            Kind = kind;
            ScopeId = scopeId;
            Severity = severity;
            Confidence = confidence;
            SourceReportId = sourceReportId;
        }

        public StableId RiskId { get; }

        public GovernmentRiskKind Kind { get; }

        public StableId ScopeId { get; }

        public decimal Severity { get; }

        public decimal Confidence { get; }

        public StableId? SourceReportId { get; }
    }

    public sealed class BudgetDemand
    {
        public BudgetDemand(
            StableId demandId,
            GovernmentBudgetCategory category,
            decimal requiredCash,
            int priority,
            bool canBecomeArrears,
            string reason)
        {
            LedgerContractGuard.RequireId(demandId, nameof(demandId));
            LedgerContractGuard.RequireNonNegative(requiredCash, nameof(requiredCash));
            LedgerContractGuard.RequireText(reason, nameof(reason));
            if (priority < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(priority));
            }

            DemandId = demandId;
            Category = category;
            RequiredCash = requiredCash;
            Priority = priority;
            CanBecomeArrears = canBecomeArrears;
            Reason = reason;
        }

        public StableId DemandId { get; }

        public GovernmentBudgetCategory Category { get; }

        public decimal RequiredCash { get; }

        /// <summary>Lower values are funded first.</summary>
        public int Priority { get; }

        public bool CanBecomeArrears { get; }

        public string Reason { get; }
    }

    public sealed class GovernmentActionCandidate
    {
        public GovernmentActionCandidate(
            StableId actionId,
            StableId targetScopeId,
            GovernmentActionKind kind,
            GovernmentBudgetCategory category,
            decimal cashCost,
            decimal administrativeLoad,
            decimal expectedBenefit,
            decimal executionProbability,
            decimal risk,
            bool deferrable)
        {
            LedgerContractGuard.RequireId(actionId, nameof(actionId));
            LedgerContractGuard.RequireId(targetScopeId, nameof(targetScopeId));
            LedgerContractGuard.RequireNonNegative(cashCost, nameof(cashCost));
            LedgerContractGuard.RequireNonNegative(administrativeLoad, nameof(administrativeLoad));
            LedgerContractGuard.RequireNonNegative(expectedBenefit, nameof(expectedBenefit));
            LedgerContractGuard.RequireRatio(executionProbability, nameof(executionProbability));
            LedgerContractGuard.RequireRatio(risk, nameof(risk));

            ActionId = actionId;
            TargetScopeId = targetScopeId;
            Kind = kind;
            Category = category;
            CashCost = cashCost;
            AdministrativeLoad = administrativeLoad;
            ExpectedBenefit = expectedBenefit;
            ExecutionProbability = executionProbability;
            Risk = risk;
            Deferrable = deferrable;
        }

        public StableId ActionId { get; }

        public StableId TargetScopeId { get; }

        public GovernmentActionKind Kind { get; }

        public GovernmentBudgetCategory Category { get; }

        public decimal CashCost { get; }

        public decimal AdministrativeLoad { get; }

        public decimal ExpectedBenefit { get; }

        public decimal ExecutionProbability { get; }

        public decimal Risk { get; }

        public bool Deferrable { get; }

        public decimal Score(decimal informationConfidence)
        {
            var costDenominator = Math.Max(CashCost, 1m);
            return ExpectedBenefit * ExecutionProbability * (1m - Risk) * informationConfidence / costDenominator;
        }
    }

    public sealed class GovernmentDecisionPacket
    {
        internal GovernmentDecisionPacket(
            StableId packetId,
            StableId authorityId,
            StableId jurisdictionId,
            int planningYear,
            decimal actualTreasuryCash,
            decimal confirmedRevenueReceived,
            decimal nominalReportedRevenue,
            decimal reportedRevenueInTransit,
            decimal reportedRevenueArrears,
            decimal actualGovernmentGrainStockKg,
            decimal minimumCashReserve,
            decimal administrativeCapacity,
            int maximumParallelActions,
            IReadOnlyList<BudgetDemand> mandatoryDemands,
            IReadOnlyList<GovernmentActionCandidate> candidates,
            IReadOnlyList<GovernmentReport> reports,
            IReadOnlyList<GovernmentRiskSignal> risks,
            decimal informationConfidence)
        {
            PacketId = packetId;
            AuthorityId = authorityId;
            JurisdictionId = jurisdictionId;
            PlanningYear = planningYear;
            ActualTreasuryCash = actualTreasuryCash;
            ConfirmedRevenueReceived = confirmedRevenueReceived;
            NominalReportedRevenue = nominalReportedRevenue;
            ReportedRevenueInTransit = reportedRevenueInTransit;
            ReportedRevenueArrears = reportedRevenueArrears;
            ActualGovernmentGrainStockKg = actualGovernmentGrainStockKg;
            MinimumCashReserve = minimumCashReserve;
            AdministrativeCapacity = administrativeCapacity;
            MaximumParallelActions = maximumParallelActions;
            MandatoryDemands = mandatoryDemands;
            Candidates = candidates;
            Reports = reports;
            Risks = risks;
            InformationConfidence = informationConfidence;
        }

        public StableId PacketId { get; }

        public StableId AuthorityId { get; }

        public StableId JurisdictionId { get; }

        public int PlanningYear { get; }

        public decimal ActualTreasuryCash { get; }

        public decimal ConfirmedRevenueReceived { get; }

        public decimal NominalReportedRevenue { get; }

        public decimal ReportedRevenueInTransit { get; }

        public decimal ReportedRevenueArrears { get; }

        public decimal ActualGovernmentGrainStockKg { get; }

        public decimal MinimumCashReserve { get; }

        public decimal AdministrativeCapacity { get; }

        public int MaximumParallelActions { get; }

        public IReadOnlyList<BudgetDemand> MandatoryDemands { get; }

        public IReadOnlyList<GovernmentActionCandidate> Candidates { get; }

        public IReadOnlyList<GovernmentReport> Reports { get; }

        public IReadOnlyList<GovernmentRiskSignal> Risks { get; }

        public decimal InformationConfidence { get; }

        public decimal CashAvailableAfterReserve => Math.Max(0m, ActualTreasuryCash - MinimumCashReserve);
    }

    public sealed class BudgetAllocation
    {
        internal BudgetAllocation(BudgetDemand demand, decimal fundedCash)
        {
            Demand = demand;
            FundedCash = fundedCash;
        }

        public BudgetDemand Demand { get; }

        public decimal FundedCash { get; }

        public decimal UnfundedCash => Demand.RequiredCash - FundedCash;
    }

    public sealed class AnnualGovernmentPlan
    {
        internal AnnualGovernmentPlan(
            StableId planId,
            GovernmentDecisionPacket sourcePacket,
            decimal reserveSetAside,
            IReadOnlyList<BudgetAllocation> mandatoryAllocations,
            IReadOnlyList<GovernmentActionCandidate> fundedActions,
            IReadOnlyList<GovernmentActionCandidate> deferredActions,
            decimal financingGap,
            decimal unallocatedCash,
            decimal administrativeCapacityRemaining)
        {
            PlanId = planId;
            SourcePacket = sourcePacket;
            ReserveSetAside = reserveSetAside;
            MandatoryAllocations = mandatoryAllocations;
            FundedActions = fundedActions;
            DeferredActions = deferredActions;
            FinancingGap = financingGap;
            UnallocatedCash = unallocatedCash;
            AdministrativeCapacityRemaining = administrativeCapacityRemaining;
        }

        public StableId PlanId { get; }

        public GovernmentDecisionPacket SourcePacket { get; }

        public decimal ReserveSetAside { get; }

        public IReadOnlyList<BudgetAllocation> MandatoryAllocations { get; }

        public IReadOnlyList<GovernmentActionCandidate> FundedActions { get; }

        public IReadOnlyList<GovernmentActionCandidate> DeferredActions { get; }

        public decimal FinancingGap { get; }

        public decimal UnallocatedCash { get; }

        public decimal AdministrativeCapacityRemaining { get; }

        public bool IsActionFunded(StableId actionId)
        {
            for (var index = 0; index < FundedActions.Count; index++)
            {
                if (FundedActions[index].ActionId.Equals(actionId))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class MonthlyVarianceReport
    {
        public MonthlyVarianceReport(
            int year,
            int month,
            decimal revenueVarianceRate,
            decimal expenseVarianceRate,
            decimal transportVarianceRate,
            decimal actualTreasuryCash,
            decimal minimumTreasuryCash,
            decimal foodCoverageMonths,
            decimal minimumFoodCoverageMonths,
            decimal militarySupplyCoverageMonths,
            decimal minimumMilitarySupplyCoverageMonths,
            MonthlyCrisisKind crisis,
            decimal requiredEmergencyCash,
            string explanation)
        {
            if (year <= 0 || month < 1 || month > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(month));
            }

            LedgerContractGuard.RequireNonNegative(actualTreasuryCash, nameof(actualTreasuryCash));
            LedgerContractGuard.RequireNonNegative(minimumTreasuryCash, nameof(minimumTreasuryCash));
            LedgerContractGuard.RequireNonNegative(foodCoverageMonths, nameof(foodCoverageMonths));
            LedgerContractGuard.RequireNonNegative(
                minimumFoodCoverageMonths,
                nameof(minimumFoodCoverageMonths));
            LedgerContractGuard.RequireNonNegative(
                militarySupplyCoverageMonths,
                nameof(militarySupplyCoverageMonths));
            LedgerContractGuard.RequireNonNegative(
                minimumMilitarySupplyCoverageMonths,
                nameof(minimumMilitarySupplyCoverageMonths));
            LedgerContractGuard.RequireNonNegative(requiredEmergencyCash, nameof(requiredEmergencyCash));
            LedgerContractGuard.RequireText(explanation, nameof(explanation));

            Year = year;
            Month = month;
            RevenueVarianceRate = revenueVarianceRate;
            ExpenseVarianceRate = expenseVarianceRate;
            TransportVarianceRate = transportVarianceRate;
            ActualTreasuryCash = actualTreasuryCash;
            MinimumTreasuryCash = minimumTreasuryCash;
            FoodCoverageMonths = foodCoverageMonths;
            MinimumFoodCoverageMonths = minimumFoodCoverageMonths;
            MilitarySupplyCoverageMonths = militarySupplyCoverageMonths;
            MinimumMilitarySupplyCoverageMonths = minimumMilitarySupplyCoverageMonths;
            Crisis = crisis;
            RequiredEmergencyCash = requiredEmergencyCash;
            Explanation = explanation;
        }

        public int Year { get; }

        public int Month { get; }

        public decimal RevenueVarianceRate { get; }

        public decimal ExpenseVarianceRate { get; }

        public decimal TransportVarianceRate { get; }

        public decimal ActualTreasuryCash { get; }

        public decimal MinimumTreasuryCash { get; }

        public decimal FoodCoverageMonths { get; }

        public decimal MinimumFoodCoverageMonths { get; }

        public decimal MilitarySupplyCoverageMonths { get; }

        public decimal MinimumMilitarySupplyCoverageMonths { get; }

        public MonthlyCrisisKind Crisis { get; }

        public decimal RequiredEmergencyCash { get; }

        public string Explanation { get; }
    }

    public sealed class MonthlyAdjustmentPolicy
    {
        public MonthlyAdjustmentPolicy(
            decimal revenueVarianceThreshold,
            decimal expenseVarianceThreshold,
            decimal transportVarianceThreshold)
        {
            LedgerContractGuard.RequireNonNegative(revenueVarianceThreshold, nameof(revenueVarianceThreshold));
            LedgerContractGuard.RequireNonNegative(expenseVarianceThreshold, nameof(expenseVarianceThreshold));
            LedgerContractGuard.RequireNonNegative(transportVarianceThreshold, nameof(transportVarianceThreshold));

            RevenueVarianceThreshold = revenueVarianceThreshold;
            ExpenseVarianceThreshold = expenseVarianceThreshold;
            TransportVarianceThreshold = transportVarianceThreshold;
        }

        public decimal RevenueVarianceThreshold { get; }

        public decimal ExpenseVarianceThreshold { get; }

        public decimal TransportVarianceThreshold { get; }
    }

    public sealed class MonthlyPlanAdjustment
    {
        internal MonthlyPlanAdjustment(
            StableId adjustmentId,
            MonthlyAdjustmentKind kind,
            decimal reserveReallocated,
            decimal unresolvedCashGap,
            IReadOnlyList<StableId> pausedActionIds,
            string reason)
        {
            AdjustmentId = adjustmentId;
            Kind = kind;
            ReserveReallocated = reserveReallocated;
            UnresolvedCashGap = unresolvedCashGap;
            PausedActionIds = pausedActionIds;
            Reason = reason;
        }

        public StableId AdjustmentId { get; }

        public MonthlyAdjustmentKind Kind { get; }

        public decimal ReserveReallocated { get; }

        public decimal UnresolvedCashGap { get; }

        public IReadOnlyList<StableId> PausedActionIds { get; }

        public string Reason { get; }
    }

    public sealed class GovernmentDecisionService
    {
        public GovernmentDecisionPacket BuildDecisionPacket(
            StableId packetId,
            StableId authorityId,
            StableId jurisdictionId,
            int planningYear,
            decimal actualTreasuryCash,
            decimal confirmedRevenueReceived,
            decimal nominalReportedRevenue,
            decimal reportedRevenueInTransit,
            decimal reportedRevenueArrears,
            decimal actualGovernmentGrainStockKg,
            decimal minimumCashReserve,
            decimal administrativeCapacity,
            int maximumParallelActions,
            IEnumerable<BudgetDemand> mandatoryDemands,
            IEnumerable<GovernmentActionCandidate> candidates,
            IEnumerable<GovernmentReport> reports,
            IEnumerable<GovernmentRiskSignal> risks)
        {
            LedgerContractGuard.RequireId(packetId, nameof(packetId));
            LedgerContractGuard.RequireId(authorityId, nameof(authorityId));
            LedgerContractGuard.RequireId(jurisdictionId, nameof(jurisdictionId));
            if (planningYear <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(planningYear));
            }

            LedgerContractGuard.RequireNonNegative(actualTreasuryCash, nameof(actualTreasuryCash));
            LedgerContractGuard.RequireNonNegative(confirmedRevenueReceived, nameof(confirmedRevenueReceived));
            LedgerContractGuard.RequireNonNegative(nominalReportedRevenue, nameof(nominalReportedRevenue));
            LedgerContractGuard.RequireNonNegative(reportedRevenueInTransit, nameof(reportedRevenueInTransit));
            LedgerContractGuard.RequireNonNegative(reportedRevenueArrears, nameof(reportedRevenueArrears));
            LedgerContractGuard.RequireNonNegative(actualGovernmentGrainStockKg, nameof(actualGovernmentGrainStockKg));
            LedgerContractGuard.RequireNonNegative(minimumCashReserve, nameof(minimumCashReserve));
            LedgerContractGuard.RequireNonNegative(administrativeCapacity, nameof(administrativeCapacity));
            if (maximumParallelActions < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumParallelActions));
            }

            var demandList = CopyItems(mandatoryDemands, nameof(mandatoryDemands));
            var candidateList = CopyItems(candidates, nameof(candidates));
            var reportList = CopyItems(reports, nameof(reports));
            var riskList = CopyItems(risks, nameof(risks));
            var informationConfidence = 1m;
            if (reportList.Count > 0)
            {
                informationConfidence = 0m;
                for (var index = 0; index < reportList.Count; index++)
                {
                    if (!reportList[index].RecipientAuthorityId.Equals(authorityId))
                    {
                        throw new ArgumentException("Every report must be addressed to the decision authority.", nameof(reports));
                    }

                    informationConfidence += reportList[index].Confidence * reportList[index].Completeness;
                }

                informationConfidence /= reportList.Count;
            }

            return new GovernmentDecisionPacket(
                packetId,
                authorityId,
                jurisdictionId,
                planningYear,
                actualTreasuryCash,
                confirmedRevenueReceived,
                nominalReportedRevenue,
                reportedRevenueInTransit,
                reportedRevenueArrears,
                actualGovernmentGrainStockKg,
                minimumCashReserve,
                administrativeCapacity,
                maximumParallelActions,
                demandList,
                candidateList,
                reportList,
                riskList,
                informationConfidence);
        }

        public AnnualGovernmentPlan PlanNextYear(
            StableId planId,
            GovernmentDecisionPacket packet)
        {
            LedgerContractGuard.RequireId(planId, nameof(planId));
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            var reserve = Math.Min(packet.ActualTreasuryCash, packet.MinimumCashReserve);
            var cash = packet.ActualTreasuryCash - reserve;
            var allocations = new List<BudgetAllocation>();
            var demands = new List<BudgetDemand>(packet.MandatoryDemands);
            demands.Sort(CompareDemands);
            var financingGap = 0m;
            for (var index = 0; index < demands.Count; index++)
            {
                var funded = Math.Min(cash, demands[index].RequiredCash);
                cash -= funded;
                allocations.Add(new BudgetAllocation(demands[index], funded));
                financingGap += demands[index].RequiredCash - funded;
            }

            var candidates = new List<GovernmentActionCandidate>(packet.Candidates);
            candidates.Sort((left, right) => CompareCandidates(left, right, packet.InformationConfidence));
            var fundedActions = new List<GovernmentActionCandidate>();
            var deferredActions = new List<GovernmentActionCandidate>();
            var administrativeCapacity = packet.AdministrativeCapacity;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var canFund = financingGap <= LedgerContractGuard.Tolerance &&
                              fundedActions.Count < packet.MaximumParallelActions &&
                              candidate.CashCost <= cash + LedgerContractGuard.Tolerance &&
                              candidate.AdministrativeLoad <= administrativeCapacity + LedgerContractGuard.Tolerance;
                if (canFund)
                {
                    cash -= candidate.CashCost;
                    administrativeCapacity -= candidate.AdministrativeLoad;
                    fundedActions.Add(candidate);
                }
                else
                {
                    deferredActions.Add(candidate);
                }
            }

            return new AnnualGovernmentPlan(
                planId,
                packet,
                reserve,
                new ReadOnlyCollection<BudgetAllocation>(allocations),
                new ReadOnlyCollection<GovernmentActionCandidate>(fundedActions),
                new ReadOnlyCollection<GovernmentActionCandidate>(deferredActions),
                financingGap,
                cash,
                administrativeCapacity);
        }

        public MonthlyPlanAdjustment AdjustMonthlyPlan(
            StableId adjustmentId,
            AnnualGovernmentPlan plan,
            MonthlyVarianceReport variance,
            MonthlyAdjustmentPolicy policy)
        {
            LedgerContractGuard.RequireId(adjustmentId, nameof(adjustmentId));
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (variance == null)
            {
                throw new ArgumentNullException(nameof(variance));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            var majorCrisis = variance.Crisis != MonthlyCrisisKind.None;
            var materialVariance =
                Math.Abs(variance.RevenueVarianceRate) >= policy.RevenueVarianceThreshold ||
                Math.Abs(variance.ExpenseVarianceRate) >= policy.ExpenseVarianceThreshold ||
                Math.Abs(variance.TransportVarianceRate) >= policy.TransportVarianceThreshold ||
                variance.ActualTreasuryCash < variance.MinimumTreasuryCash ||
                variance.FoodCoverageMonths < variance.MinimumFoodCoverageMonths ||
                variance.MilitarySupplyCoverageMonths < variance.MinimumMilitarySupplyCoverageMonths;

            if (!majorCrisis && !materialVariance)
            {
                return new MonthlyPlanAdjustment(
                    adjustmentId,
                    MonthlyAdjustmentKind.None,
                    0m,
                    0m,
                    new ReadOnlyCollection<StableId>(new List<StableId>()),
                    "The monthly deviation stays within the annual plan tolerance.");
            }

            var available = plan.UnallocatedCash;
            var reserveUsed = Math.Min(available, variance.RequiredEmergencyCash);
            var remainingGap = variance.RequiredEmergencyCash - reserveUsed;
            var pausedActions = new List<StableId>();
            if (remainingGap > LedgerContractGuard.Tolerance)
            {
                var deferrable = new List<GovernmentActionCandidate>();
                for (var index = 0; index < plan.FundedActions.Count; index++)
                {
                    if (plan.FundedActions[index].Deferrable)
                    {
                        deferrable.Add(plan.FundedActions[index]);
                    }
                }

                deferrable.Sort((left, right) =>
                    left.Score(plan.SourcePacket.InformationConfidence).CompareTo(
                        right.Score(plan.SourcePacket.InformationConfidence)));
                for (var index = 0; index < deferrable.Count && remainingGap > LedgerContractGuard.Tolerance; index++)
                {
                    pausedActions.Add(deferrable[index].ActionId);
                    var reclaimed = Math.Min(remainingGap, deferrable[index].CashCost);
                    reserveUsed += reclaimed;
                    remainingGap -= reclaimed;
                }
            }

            return new MonthlyPlanAdjustment(
                adjustmentId,
                majorCrisis ? MonthlyAdjustmentKind.EmergencyReplan : MonthlyAdjustmentKind.ReserveReallocation,
                reserveUsed,
                Math.Max(0m, remainingGap),
                new ReadOnlyCollection<StableId>(pausedActions),
                variance.Explanation);
        }

        public SimulationDriverRecord ScheduleFundedEdictEffect(
            AnnualGovernmentPlan plan,
            StableId actionId,
            StableId driverId,
            StableId edictInstanceId,
            LedgerPeriod decisionPeriod,
            LedgerPeriod effectivePeriod,
            LedgerPeriod expiresAfterPeriod,
            decimal executionRate,
            IEnumerable<SimulationDriverEffect> effects)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            GovernmentActionCandidate action = null;
            for (var index = 0; index < plan.FundedActions.Count; index++)
            {
                if (plan.FundedActions[index].ActionId.Equals(actionId))
                {
                    action = plan.FundedActions[index];
                    break;
                }
            }

            if (action == null || action.Kind != GovernmentActionKind.Edict)
            {
                throw new InvalidOperationException("Only a funded edict action can schedule policy effects.");
            }

            return new SimulationDriverRecord(
                driverId,
                action.TargetScopeId,
                SimulationDriverOrigin.GovernmentPolicy,
                SimulationDriverKind.EdictExecution,
                decisionPeriod,
                effectivePeriod,
                expiresAfterPeriod,
                executionRate,
                effects,
                action.ActionId,
                edictInstanceId,
                new[] { plan.PlanId, plan.SourcePacket.PacketId });
        }

        private static int CompareDemands(BudgetDemand left, BudgetDemand right)
        {
            var priority = left.Priority.CompareTo(right.Priority);
            return priority != 0 ? priority : string.CompareOrdinal(left.DemandId.Value, right.DemandId.Value);
        }

        private static int CompareCandidates(
            GovernmentActionCandidate left,
            GovernmentActionCandidate right,
            decimal informationConfidence)
        {
            var score = right.Score(informationConfidence).CompareTo(left.Score(informationConfidence));
            return score != 0 ? score : string.CompareOrdinal(left.ActionId.Value, right.ActionId.Value);
        }

        private static IReadOnlyList<T> CopyItems<T>(IEnumerable<T> source, string parameterName)
            where T : class
        {
            var result = new List<T>();
            if (source != null)
            {
                foreach (var item in source)
                {
                    if (item == null)
                    {
                        throw new ArgumentException("Decision inputs cannot contain null.", parameterName);
                    }

                    result.Add(item);
                }
            }

            return new ReadOnlyCollection<T>(result);
        }
    }
}
