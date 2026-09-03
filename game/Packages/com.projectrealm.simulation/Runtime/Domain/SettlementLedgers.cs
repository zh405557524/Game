using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace ProjectRealm.Domain
{
    public enum SimulationResolution
    {
        VillageDetailed = 0,
        TownshipNode = 1,
        CountyFull = 2,
        ZhouAggregate = 3,
        FuAggregate = 4,
        SiStrategic = 5,
        RealmCentral = 6
    }

    public enum SettlementLedgerDataKind
    {
        AuthoritativeTruth = 0,
        EstimatedAggregate = 1,
        GovernmentReport = 2
    }

    public enum LedgerPeriodKind
    {
        Monthly = 0,
        Annual = 1
    }

    public enum LedgerCloseStatus
    {
        Closed = 0,
        CorrectedByEntry = 1
    }

    public enum LedgerMetricDomain
    {
        PopulationAndLabor = 0,
        LandAndResources = 1,
        FacilitiesAndTechnology = 2,
        ProductionAndNeeds = 3,
        MarketAndTrade = 4,
        MoneyAndCredit = 5,
        GovernmentFinance = 6,
        AdministrationAndOrder = 7,
        Military = 8,
        ProjectsAndEdicts = 9,
        SocietyAndDisasters = 10,
        ActorImpact = 11,
        Audit = 12
    }

    public enum MetricAggregationMode
    {
        Sum = 0,
        WeightedAverage = 1,
        Minimum = 2,
        Maximum = 3,
        Latest = 4
    }

    public enum ConsolidationAdjustmentKind
    {
        ResourceInternalTransfer = 0,
        FiscalInternalTransfer = 1,
        EconomicInternalTrade = 2
    }

    public enum EconomicSectorKind
    {
        Agriculture = 0,
        Mining = 1,
        Manufacturing = 2,
        Construction = 3,
        CommerceAndTransport = 4,
        PublicServices = 5,
        MilitarySupply = 6
    }

    public enum MilitaryMaterielKind
    {
        MeleeWeapon = 0,
        BowOrCrossbow = 1,
        Firearm = 2,
        Artillery = 3,
        Armor = 4,
        Ammunition = 5,
        EngineeringEquipment = 6,
        WarVessel = 7,
        TransportVessel = 8,
        RequisitionedCivilianVessel = 9,
        Wagon = 10,
        ArtilleryCarriage = 11,
        PackAnimal = 12,
        DraftAnimal = 13,
        WarHorse = 14,
        MilitaryFood = 15,
        Fodder = 16,
        RepairMaterial = 17,
        SparePart = 18,
        FieldTool = 19
    }

    public enum ActorInfluenceChannel
    {
        Property = 0,
        Market = 1,
        Administration = 2,
        Military = 3,
        SocialOrganization = 4,
        Knowledge = 5
    }

    public sealed class LedgerPeriod : IEquatable<LedgerPeriod>
    {
        private LedgerPeriod(int year, int month, LedgerPeriodKind kind)
        {
            if (year <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(year));
            }

            if (kind == LedgerPeriodKind.Monthly && (month < 1 || month > 12))
            {
                throw new ArgumentOutOfRangeException(nameof(month));
            }

            if (kind == LedgerPeriodKind.Annual && month != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(month));
            }

            Year = year;
            Month = month;
            Kind = kind;
        }

        public int Year { get; }

        public int Month { get; }

        public LedgerPeriodKind Kind { get; }

        public static LedgerPeriod Monthly(int year, int month)
        {
            return new LedgerPeriod(year, month, LedgerPeriodKind.Monthly);
        }

        public static LedgerPeriod Annual(int year)
        {
            return new LedgerPeriod(year, 0, LedgerPeriodKind.Annual);
        }

        public bool Equals(LedgerPeriod other)
        {
            return other != null && Year == other.Year && Month == other.Month && Kind == other.Kind;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as LedgerPeriod);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Year * 397) ^ Month) * 397 ^ (int)Kind;
            }
        }

        public override string ToString()
        {
            return Kind == LedgerPeriodKind.Annual
                ? Year.ToString(CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}", Year, Month);
        }
    }

    public sealed class SettlementLedgerHeader
    {
        public SettlementLedgerHeader(
            StableId ledgerId,
            StableId jurisdictionId,
            StableId? parentJurisdictionId,
            LedgerPeriod period,
            SimulationResolution resolution,
            StableId settlementModelId,
            string ruleVersion,
            StableId settlementOwnerId,
            IEnumerable<StableId> childLedgerIds = null,
            StableId? residualLedgerId = null,
            SettlementLedgerDataKind dataKind = SettlementLedgerDataKind.AuthoritativeTruth,
            LedgerPeriod observedThrough = null,
            int reportingDelayMonths = 0,
            decimal completeness = 1m,
            decimal confidence = 1m,
            string inputHash = "none",
            string outputHash = "none",
            LedgerCloseStatus closeStatus = LedgerCloseStatus.Closed)
        {
            LedgerContractGuard.RequireId(ledgerId, nameof(ledgerId));
            LedgerContractGuard.RequireId(jurisdictionId, nameof(jurisdictionId));
            LedgerContractGuard.RequireNullableId(parentJurisdictionId, nameof(parentJurisdictionId));
            LedgerContractGuard.RequireId(settlementModelId, nameof(settlementModelId));
            LedgerContractGuard.RequireId(settlementOwnerId, nameof(settlementOwnerId));
            LedgerContractGuard.RequireText(ruleVersion, nameof(ruleVersion));
            LedgerContractGuard.RequireText(inputHash, nameof(inputHash));
            LedgerContractGuard.RequireText(outputHash, nameof(outputHash));
            LedgerContractGuard.RequireRatio(completeness, nameof(completeness));
            LedgerContractGuard.RequireRatio(confidence, nameof(confidence));

            if (period == null)
            {
                throw new ArgumentNullException(nameof(period));
            }

            if (observedThrough == null)
            {
                observedThrough = period;
            }

            if (reportingDelayMonths < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reportingDelayMonths));
            }

            LedgerId = ledgerId;
            JurisdictionId = jurisdictionId;
            ParentJurisdictionId = parentJurisdictionId;
            Period = period;
            Resolution = resolution;
            SettlementModelId = settlementModelId;
            RuleVersion = ruleVersion;
            SettlementOwnerId = settlementOwnerId;
            ChildLedgerIds = LedgerContractGuard.CopyUniqueIds(childLedgerIds, nameof(childLedgerIds));
            ResidualLedgerId = residualLedgerId;
            DataKind = dataKind;
            ObservedThrough = observedThrough;
            ReportingDelayMonths = reportingDelayMonths;
            Completeness = completeness;
            Confidence = confidence;
            InputHash = inputHash;
            OutputHash = outputHash;
            CloseStatus = closeStatus;
        }

        public StableId LedgerId { get; }

        public StableId JurisdictionId { get; }

        public StableId? ParentJurisdictionId { get; }

        public LedgerPeriod Period { get; }

        public SimulationResolution Resolution { get; }

        public StableId SettlementModelId { get; }

        public string RuleVersion { get; }

        public StableId SettlementOwnerId { get; }

        public IReadOnlyList<StableId> ChildLedgerIds { get; }

        public StableId? ResidualLedgerId { get; }

        public SettlementLedgerDataKind DataKind { get; }

        public LedgerPeriod ObservedThrough { get; }

        public int ReportingDelayMonths { get; }

        public decimal Completeness { get; }

        public decimal Confidence { get; }

        public string InputHash { get; }

        public string OutputHash { get; }

        public LedgerCloseStatus CloseStatus { get; }
    }

    public sealed class LedgerFlowLine
    {
        public LedgerFlowLine(
            StableId metricId,
            string unit,
            decimal opening,
            decimal externalInflow,
            decimal internalInflow,
            decimal produced,
            decimal externalOutflow,
            decimal internalOutflow,
            decimal consumed,
            decimal lostOrDestroyed,
            decimal closing)
        {
            LedgerContractGuard.RequireId(metricId, nameof(metricId));
            LedgerContractGuard.RequireText(unit, nameof(unit));
            LedgerContractGuard.RequireNonNegative(opening, nameof(opening));
            LedgerContractGuard.RequireNonNegative(externalInflow, nameof(externalInflow));
            LedgerContractGuard.RequireNonNegative(internalInflow, nameof(internalInflow));
            LedgerContractGuard.RequireNonNegative(produced, nameof(produced));
            LedgerContractGuard.RequireNonNegative(externalOutflow, nameof(externalOutflow));
            LedgerContractGuard.RequireNonNegative(internalOutflow, nameof(internalOutflow));
            LedgerContractGuard.RequireNonNegative(consumed, nameof(consumed));
            LedgerContractGuard.RequireNonNegative(lostOrDestroyed, nameof(lostOrDestroyed));
            LedgerContractGuard.RequireNonNegative(closing, nameof(closing));

            MetricId = metricId;
            Unit = unit;
            Opening = opening;
            ExternalInflow = externalInflow;
            InternalInflow = internalInflow;
            Produced = produced;
            ExternalOutflow = externalOutflow;
            InternalOutflow = internalOutflow;
            Consumed = consumed;
            LostOrDestroyed = lostOrDestroyed;
            Closing = closing;
        }

        public StableId MetricId { get; }

        public string Unit { get; }

        public decimal Opening { get; }

        public decimal ExternalInflow { get; }

        public decimal InternalInflow { get; }

        public decimal Produced { get; }

        public decimal ExternalOutflow { get; }

        public decimal InternalOutflow { get; }

        public decimal Consumed { get; }

        public decimal LostOrDestroyed { get; }

        public decimal Closing { get; }

        public decimal BalanceError =>
            Opening +
            ExternalInflow +
            InternalInflow +
            Produced -
            ExternalOutflow -
            InternalOutflow -
            Consumed -
            LostOrDestroyed -
            Closing;
    }

    public sealed class FiscalStatement
    {
        public FiscalStatement(
            decimal openingTreasury,
            decimal assessedRevenue,
            decimal collectedRevenue,
            decimal transfersReceived,
            decimal borrowingReceived,
            decimal mandatoryExpensesPaid,
            decimal discretionaryExpensesPaid,
            decimal debtServicePaid,
            decimal transfersSent,
            decimal closingTreasury,
            decimal revenueReceivableClosing,
            decimal paymentArrearsClosing,
            decimal revenueInTransitClosing,
            decimal debtOutstandingClosing)
        {
            LedgerContractGuard.RequireNonNegative(openingTreasury, nameof(openingTreasury));
            LedgerContractGuard.RequireNonNegative(assessedRevenue, nameof(assessedRevenue));
            LedgerContractGuard.RequireNonNegative(collectedRevenue, nameof(collectedRevenue));
            LedgerContractGuard.RequireNonNegative(transfersReceived, nameof(transfersReceived));
            LedgerContractGuard.RequireNonNegative(borrowingReceived, nameof(borrowingReceived));
            LedgerContractGuard.RequireNonNegative(mandatoryExpensesPaid, nameof(mandatoryExpensesPaid));
            LedgerContractGuard.RequireNonNegative(discretionaryExpensesPaid, nameof(discretionaryExpensesPaid));
            LedgerContractGuard.RequireNonNegative(debtServicePaid, nameof(debtServicePaid));
            LedgerContractGuard.RequireNonNegative(transfersSent, nameof(transfersSent));
            LedgerContractGuard.RequireNonNegative(closingTreasury, nameof(closingTreasury));
            LedgerContractGuard.RequireNonNegative(revenueReceivableClosing, nameof(revenueReceivableClosing));
            LedgerContractGuard.RequireNonNegative(paymentArrearsClosing, nameof(paymentArrearsClosing));
            LedgerContractGuard.RequireNonNegative(revenueInTransitClosing, nameof(revenueInTransitClosing));
            LedgerContractGuard.RequireNonNegative(debtOutstandingClosing, nameof(debtOutstandingClosing));

            OpeningTreasury = openingTreasury;
            AssessedRevenue = assessedRevenue;
            CollectedRevenue = collectedRevenue;
            TransfersReceived = transfersReceived;
            BorrowingReceived = borrowingReceived;
            MandatoryExpensesPaid = mandatoryExpensesPaid;
            DiscretionaryExpensesPaid = discretionaryExpensesPaid;
            DebtServicePaid = debtServicePaid;
            TransfersSent = transfersSent;
            ClosingTreasury = closingTreasury;
            RevenueReceivableClosing = revenueReceivableClosing;
            PaymentArrearsClosing = paymentArrearsClosing;
            RevenueInTransitClosing = revenueInTransitClosing;
            DebtOutstandingClosing = debtOutstandingClosing;
        }

        public decimal OpeningTreasury { get; }

        public decimal AssessedRevenue { get; }

        public decimal CollectedRevenue { get; }

        public decimal TransfersReceived { get; }

        public decimal BorrowingReceived { get; }

        public decimal MandatoryExpensesPaid { get; }

        public decimal DiscretionaryExpensesPaid { get; }

        public decimal DebtServicePaid { get; }

        public decimal TransfersSent { get; }

        public decimal ClosingTreasury { get; }

        public decimal RevenueReceivableClosing { get; }

        public decimal PaymentArrearsClosing { get; }

        public decimal RevenueInTransitClosing { get; }

        public decimal DebtOutstandingClosing { get; }

        public decimal BalanceError =>
            OpeningTreasury +
            CollectedRevenue +
            TransfersReceived +
            BorrowingReceived -
            MandatoryExpensesPaid -
            DiscretionaryExpensesPaid -
            DebtServicePaid -
            TransfersSent -
            ClosingTreasury;

        public static FiscalStatement Zero(decimal treasury = 0m)
        {
            return new FiscalStatement(
                treasury,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                treasury,
                0m,
                0m,
                0m,
                0m);
        }
    }

    public sealed class ObligationAging
    {
        public ObligationAging(
            StableId obligationKindId,
            StableId? creditorId,
            string unit,
            decimal due,
            decimal paid,
            decimal currentOutstanding,
            decimal overdueOneToThreeMonths,
            decimal overdueFourToTwelveMonths,
            decimal overdueMoreThanTwelveMonths)
        {
            LedgerContractGuard.RequireId(obligationKindId, nameof(obligationKindId));
            LedgerContractGuard.RequireNullableId(creditorId, nameof(creditorId));
            LedgerContractGuard.RequireText(unit, nameof(unit));
            LedgerContractGuard.RequireNonNegative(due, nameof(due));
            LedgerContractGuard.RequireNonNegative(paid, nameof(paid));
            LedgerContractGuard.RequireNonNegative(currentOutstanding, nameof(currentOutstanding));
            LedgerContractGuard.RequireNonNegative(overdueOneToThreeMonths, nameof(overdueOneToThreeMonths));
            LedgerContractGuard.RequireNonNegative(overdueFourToTwelveMonths, nameof(overdueFourToTwelveMonths));
            LedgerContractGuard.RequireNonNegative(overdueMoreThanTwelveMonths, nameof(overdueMoreThanTwelveMonths));

            if (paid + currentOutstanding + overdueOneToThreeMonths + overdueFourToTwelveMonths +
                overdueMoreThanTwelveMonths > due + LedgerContractGuard.Tolerance)
            {
                throw new ArgumentException("Paid and outstanding obligation amounts cannot exceed the amount due.", nameof(due));
            }

            ObligationKindId = obligationKindId;
            CreditorId = creditorId;
            Unit = unit;
            Due = due;
            Paid = paid;
            CurrentOutstanding = currentOutstanding;
            OverdueOneToThreeMonths = overdueOneToThreeMonths;
            OverdueFourToTwelveMonths = overdueFourToTwelveMonths;
            OverdueMoreThanTwelveMonths = overdueMoreThanTwelveMonths;
        }

        public StableId ObligationKindId { get; }

        public StableId? CreditorId { get; }

        public string Unit { get; }

        public decimal Due { get; }

        public decimal Paid { get; }

        public decimal CurrentOutstanding { get; }

        public decimal OverdueOneToThreeMonths { get; }

        public decimal OverdueFourToTwelveMonths { get; }

        public decimal OverdueMoreThanTwelveMonths { get; }

        public decimal TotalOutstanding =>
            CurrentOutstanding +
            OverdueOneToThreeMonths +
            OverdueFourToTwelveMonths +
            OverdueMoreThanTwelveMonths;
    }

    public sealed class LedgerMetric
    {
        public LedgerMetric(
            StableId metricId,
            LedgerMetricDomain domain,
            string unit,
            decimal value,
            MetricAggregationMode aggregationMode = MetricAggregationMode.Sum,
            decimal weight = 1m)
        {
            LedgerContractGuard.RequireId(metricId, nameof(metricId));
            LedgerContractGuard.RequireText(unit, nameof(unit));
            LedgerContractGuard.RequireNonNegative(weight, nameof(weight));

            if (aggregationMode == MetricAggregationMode.WeightedAverage && weight <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(weight));
            }

            MetricId = metricId;
            Domain = domain;
            Unit = unit;
            Value = value;
            AggregationMode = aggregationMode;
            Weight = weight;
        }

        public StableId MetricId { get; }

        public LedgerMetricDomain Domain { get; }

        public string Unit { get; }

        public decimal Value { get; }

        public MetricAggregationMode AggregationMode { get; }

        public decimal Weight { get; }
    }

    /// <summary>
    /// A value-added account for one economic sector. Physical goods remain in
    /// LedgerFlowLine; this statement values subsistence and market production
    /// without counting intermediate goods as new output more than once.
    /// </summary>
    public sealed class EconomicSectorStatement
    {
        public EconomicSectorStatement(
            EconomicSectorKind sector,
            string valuationUnit,
            int referencePriceYear,
            decimal nominalGrossOutput,
            decimal nominalIntermediateConsumption,
            decimal realGrossOutputAtReferencePrices,
            decimal realIntermediateConsumptionAtReferencePrices,
            decimal laborCompensation,
            decimal landAndAssetRent,
            decimal netProductionTaxes,
            decimal householdMixedIncomeAndOperatingSurplus,
            decimal laborPersonMonths,
            decimal capacityUtilization,
            decimal salesValue,
            decimal inventoryChangeValue)
        {
            LedgerContractGuard.RequireText(valuationUnit, nameof(valuationUnit));
            if (referencePriceYear <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(referencePriceYear));
            }

            LedgerContractGuard.RequireNonNegative(nominalGrossOutput, nameof(nominalGrossOutput));
            LedgerContractGuard.RequireNonNegative(
                nominalIntermediateConsumption,
                nameof(nominalIntermediateConsumption));
            LedgerContractGuard.RequireNonNegative(
                realGrossOutputAtReferencePrices,
                nameof(realGrossOutputAtReferencePrices));
            LedgerContractGuard.RequireNonNegative(
                realIntermediateConsumptionAtReferencePrices,
                nameof(realIntermediateConsumptionAtReferencePrices));
            LedgerContractGuard.RequireNonNegative(laborCompensation, nameof(laborCompensation));
            LedgerContractGuard.RequireNonNegative(landAndAssetRent, nameof(landAndAssetRent));
            LedgerContractGuard.RequireNonNegative(laborPersonMonths, nameof(laborPersonMonths));
            LedgerContractGuard.RequireRatio(capacityUtilization, nameof(capacityUtilization));
            LedgerContractGuard.RequireNonNegative(salesValue, nameof(salesValue));

            Sector = sector;
            ValuationUnit = valuationUnit;
            ReferencePriceYear = referencePriceYear;
            NominalGrossOutput = nominalGrossOutput;
            NominalIntermediateConsumption = nominalIntermediateConsumption;
            RealGrossOutputAtReferencePrices = realGrossOutputAtReferencePrices;
            RealIntermediateConsumptionAtReferencePrices = realIntermediateConsumptionAtReferencePrices;
            LaborCompensation = laborCompensation;
            LandAndAssetRent = landAndAssetRent;
            NetProductionTaxes = netProductionTaxes;
            HouseholdMixedIncomeAndOperatingSurplus = householdMixedIncomeAndOperatingSurplus;
            LaborPersonMonths = laborPersonMonths;
            CapacityUtilization = capacityUtilization;
            SalesValue = salesValue;
            InventoryChangeValue = inventoryChangeValue;

            if (Math.Abs(ValueAddedDistributionError) > LedgerContractGuard.Tolerance)
            {
                throw new ArgumentException(
                    "Labor income, rent, net production taxes and mixed income must distribute nominal value added.");
            }
        }

        public EconomicSectorKind Sector { get; }

        public string ValuationUnit { get; }

        public int ReferencePriceYear { get; }

        public decimal NominalGrossOutput { get; }

        public decimal NominalIntermediateConsumption { get; }

        public decimal NominalValueAdded => NominalGrossOutput - NominalIntermediateConsumption;

        public decimal RealGrossOutputAtReferencePrices { get; }

        public decimal RealIntermediateConsumptionAtReferencePrices { get; }

        public decimal RealValueAddedAtReferencePrices =>
            RealGrossOutputAtReferencePrices - RealIntermediateConsumptionAtReferencePrices;

        public decimal LaborCompensation { get; }

        public decimal LandAndAssetRent { get; }

        public decimal NetProductionTaxes { get; }

        public decimal HouseholdMixedIncomeAndOperatingSurplus { get; }

        public decimal LaborPersonMonths { get; }

        public decimal CapacityUtilization { get; }

        public decimal SalesValue { get; }

        public decimal InventoryChangeValue { get; }

        public decimal ValueAddedDistributionError =>
            NominalValueAdded -
            LaborCompensation -
            LandAndAssetRent -
            NetProductionTaxes -
            HouseholdMixedIncomeAndOperatingSurplus;
    }

    public sealed class EconomicOutputStatement
    {
        public EconomicOutputStatement(
            IEnumerable<EconomicSectorStatement> sectors,
            string valuationUnit,
            int referencePriceYear,
            decimal householdFinalConsumption,
            decimal governmentAndMilitaryFinalConsumption,
            decimal grossFixedCapitalFormation,
            decimal inventoryChange,
            decimal externalExports,
            decimal externalImports,
            decimal statisticalDiscrepancy)
        {
            LedgerContractGuard.RequireText(valuationUnit, nameof(valuationUnit));
            if (referencePriceYear <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(referencePriceYear));
            }

            LedgerContractGuard.RequireNonNegative(householdFinalConsumption, nameof(householdFinalConsumption));
            LedgerContractGuard.RequireNonNegative(
                governmentAndMilitaryFinalConsumption,
                nameof(governmentAndMilitaryFinalConsumption));
            LedgerContractGuard.RequireNonNegative(grossFixedCapitalFormation, nameof(grossFixedCapitalFormation));
            LedgerContractGuard.RequireNonNegative(externalExports, nameof(externalExports));
            LedgerContractGuard.RequireNonNegative(externalImports, nameof(externalImports));

            Sectors = CopySectors(sectors, valuationUnit, referencePriceYear);
            ValuationUnit = valuationUnit;
            ReferencePriceYear = referencePriceYear;
            HouseholdFinalConsumption = householdFinalConsumption;
            GovernmentAndMilitaryFinalConsumption = governmentAndMilitaryFinalConsumption;
            GrossFixedCapitalFormation = grossFixedCapitalFormation;
            InventoryChange = inventoryChange;
            ExternalExports = externalExports;
            ExternalImports = externalImports;
            StatisticalDiscrepancy = statisticalDiscrepancy;

            if (Math.Abs(AccountingIdentityError) > LedgerContractGuard.Tolerance)
            {
                throw new ArgumentException(
                    "The production and expenditure sides of the economic output statement do not reconcile.");
            }
        }

        public IReadOnlyList<EconomicSectorStatement> Sectors { get; }

        public string ValuationUnit { get; }

        public int ReferencePriceYear { get; }

        public decimal HouseholdFinalConsumption { get; }

        public decimal GovernmentAndMilitaryFinalConsumption { get; }

        public decimal GrossFixedCapitalFormation { get; }

        public decimal InventoryChange { get; }

        public decimal ExternalExports { get; }

        public decimal ExternalImports { get; }

        public decimal StatisticalDiscrepancy { get; }

        public decimal NominalGrossOutput => SumSectorValue(sector => sector.NominalGrossOutput);

        public decimal NominalIntermediateConsumption =>
            SumSectorValue(sector => sector.NominalIntermediateConsumption);

        public decimal NominalValueAdded => SumSectorValue(sector => sector.NominalValueAdded);

        public decimal RealValueAddedAtReferencePrices =>
            SumSectorValue(sector => sector.RealValueAddedAtReferencePrices);

        public decimal ExpenditureMeasure =>
            HouseholdFinalConsumption +
            GovernmentAndMilitaryFinalConsumption +
            GrossFixedCapitalFormation +
            InventoryChange +
            ExternalExports -
            ExternalImports;

        public decimal AccountingIdentityError => NominalValueAdded - ExpenditureMeasure - StatisticalDiscrepancy;

        public static EconomicOutputStatement Empty(string valuationUnit = "silver-equivalent", int referencePriceYear = 1628)
        {
            return new EconomicOutputStatement(
                null,
                valuationUnit,
                referencePriceYear,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m);
        }

        private decimal SumSectorValue(Func<EconomicSectorStatement, decimal> selector)
        {
            var total = 0m;
            for (var index = 0; index < Sectors.Count; index++)
            {
                total += selector(Sectors[index]);
            }

            return total;
        }

        private static IReadOnlyList<EconomicSectorStatement> CopySectors(
            IEnumerable<EconomicSectorStatement> sectors,
            string valuationUnit,
            int referencePriceYear)
        {
            var result = new List<EconomicSectorStatement>();
            var kinds = new HashSet<EconomicSectorKind>();
            if (sectors != null)
            {
                foreach (var sector in sectors)
                {
                    if (sector == null)
                    {
                        throw new ArgumentException("Economic sectors cannot contain null.", nameof(sectors));
                    }

                    if (!kinds.Add(sector.Sector))
                    {
                        throw new ArgumentException("An economic sector can appear only once in one statement.", nameof(sectors));
                    }

                    if (!string.Equals(sector.ValuationUnit, valuationUnit, StringComparison.Ordinal) ||
                        sector.ReferencePriceYear != referencePriceYear)
                    {
                        throw new ArgumentException(
                            "All economic sectors must use the statement valuation unit and reference price year.",
                            nameof(sectors));
                    }

                    result.Add(sector);
                }
            }

            result.Sort((left, right) => left.Sector.CompareTo(right.Sector));
            return new ReadOnlyCollection<EconomicSectorStatement>(result);
        }
    }

    public sealed class MilitaryMaterielLine
    {
        public MilitaryMaterielLine(
            MilitaryMaterielKind kind,
            LedgerFlowLine flow,
            decimal serviceableClosing,
            decimal damagedAwaitingRepairClosing,
            decimal reservedClosing)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            LedgerContractGuard.RequireNonNegative(serviceableClosing, nameof(serviceableClosing));
            LedgerContractGuard.RequireNonNegative(
                damagedAwaitingRepairClosing,
                nameof(damagedAwaitingRepairClosing));
            LedgerContractGuard.RequireNonNegative(reservedClosing, nameof(reservedClosing));

            if (serviceableClosing + damagedAwaitingRepairClosing > flow.Closing + LedgerContractGuard.Tolerance)
            {
                throw new ArgumentException("Serviceable and damaged materiel cannot exceed closing stock.");
            }

            if (reservedClosing > serviceableClosing + LedgerContractGuard.Tolerance)
            {
                throw new ArgumentException("Reserved materiel must be part of serviceable stock.");
            }

            Kind = kind;
            Flow = flow;
            ServiceableClosing = serviceableClosing;
            DamagedAwaitingRepairClosing = damagedAwaitingRepairClosing;
            ReservedClosing = reservedClosing;
        }

        public MilitaryMaterielKind Kind { get; }

        public LedgerFlowLine Flow { get; }

        public decimal ServiceableClosing { get; }

        public decimal DamagedAwaitingRepairClosing { get; }

        public decimal ReservedClosing { get; }

        public decimal AvailableClosing => ServiceableClosing - ReservedClosing;

        public decimal ServiceabilityRate => Flow.Closing <= 0m ? 0m : ServiceableClosing / Flow.Closing;
    }

    public sealed class MilitaryMaterielStatement
    {
        public MilitaryMaterielStatement(
            IEnumerable<MilitaryMaterielLine> materiel,
            decimal troopStrength,
            decimal fitForDutyTroops,
            decimal monthlyMilitaryFoodRequirementKg,
            decimal monthlyFodderRequirementKg,
            decimal monthlyAmmunitionRequirement,
            decimal landTransportCapacityKg,
            decimal navalTransportCapacityKg)
        {
            LedgerContractGuard.RequireNonNegative(troopStrength, nameof(troopStrength));
            LedgerContractGuard.RequireNonNegative(fitForDutyTroops, nameof(fitForDutyTroops));
            LedgerContractGuard.RequireNonNegative(
                monthlyMilitaryFoodRequirementKg,
                nameof(monthlyMilitaryFoodRequirementKg));
            LedgerContractGuard.RequireNonNegative(monthlyFodderRequirementKg, nameof(monthlyFodderRequirementKg));
            LedgerContractGuard.RequireNonNegative(monthlyAmmunitionRequirement, nameof(monthlyAmmunitionRequirement));
            LedgerContractGuard.RequireNonNegative(landTransportCapacityKg, nameof(landTransportCapacityKg));
            LedgerContractGuard.RequireNonNegative(navalTransportCapacityKg, nameof(navalTransportCapacityKg));

            if (fitForDutyTroops > troopStrength + LedgerContractGuard.Tolerance)
            {
                throw new ArgumentException("Fit-for-duty troops cannot exceed total troop strength.");
            }

            Materiel = CopyMateriel(materiel);
            TroopStrength = troopStrength;
            FitForDutyTroops = fitForDutyTroops;
            MonthlyMilitaryFoodRequirementKg = monthlyMilitaryFoodRequirementKg;
            MonthlyFodderRequirementKg = monthlyFodderRequirementKg;
            MonthlyAmmunitionRequirement = monthlyAmmunitionRequirement;
            LandTransportCapacityKg = landTransportCapacityKg;
            NavalTransportCapacityKg = navalTransportCapacityKg;
        }

        public IReadOnlyList<MilitaryMaterielLine> Materiel { get; }

        public decimal TroopStrength { get; }

        public decimal FitForDutyTroops { get; }

        public decimal MonthlyMilitaryFoodRequirementKg { get; }

        public decimal MonthlyFodderRequirementKg { get; }

        public decimal MonthlyAmmunitionRequirement { get; }

        public decimal LandTransportCapacityKg { get; }

        public decimal NavalTransportCapacityKg { get; }

        public decimal FitForDutyRate => TroopStrength <= 0m ? 0m : FitForDutyTroops / TroopStrength;

        public static MilitaryMaterielStatement Empty => new MilitaryMaterielStatement(null, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

        public MilitaryMaterielLine GetRequiredMateriel(StableId metricId)
        {
            for (var index = 0; index < Materiel.Count; index++)
            {
                if (Materiel[index].Flow.MetricId.Equals(metricId))
                {
                    return Materiel[index];
                }
            }

            throw new KeyNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "Military materiel '{0}' was not found.", metricId));
        }

        private static IReadOnlyList<MilitaryMaterielLine> CopyMateriel(
            IEnumerable<MilitaryMaterielLine> materiel)
        {
            var result = new List<MilitaryMaterielLine>();
            var metricIds = new HashSet<StableId>();
            if (materiel != null)
            {
                foreach (var line in materiel)
                {
                    if (line == null)
                    {
                        throw new ArgumentException("Military materiel cannot contain null.", nameof(materiel));
                    }

                    if (!metricIds.Add(line.Flow.MetricId))
                    {
                        throw new ArgumentException("Military materiel metrics must be unique.", nameof(materiel));
                    }

                    result.Add(line);
                }
            }

            result.Sort((left, right) => string.CompareOrdinal(left.Flow.MetricId.Value, right.Flow.MetricId.Value));
            return new ReadOnlyCollection<MilitaryMaterielLine>(result);
        }
    }

    public sealed class CapacityAndStockStatement
    {
        public CapacityAndStockStatement(
            IEnumerable<LedgerMetric> endStocks = null,
            IEnumerable<LedgerMetric> decisionIndicators = null)
        {
            EndStocks = CopyMetrics(endStocks, nameof(endStocks));
            DecisionIndicators = CopyMetrics(decisionIndicators, nameof(decisionIndicators));
        }

        public IReadOnlyList<LedgerMetric> EndStocks { get; }

        public IReadOnlyList<LedgerMetric> DecisionIndicators { get; }

        public static CapacityAndStockStatement Empty => new CapacityAndStockStatement();

        private static IReadOnlyList<LedgerMetric> CopyMetrics(
            IEnumerable<LedgerMetric> metrics,
            string parameterName)
        {
            var result = new List<LedgerMetric>();
            var ids = new HashSet<StableId>();
            if (metrics != null)
            {
                foreach (var metric in metrics)
                {
                    if (metric == null)
                    {
                        throw new ArgumentException("Ledger metrics cannot contain null.", parameterName);
                    }

                    if (!ids.Add(metric.MetricId))
                    {
                        throw new ArgumentException(
                            string.Format(CultureInfo.InvariantCulture, "Metric '{0}' is duplicated.", metric.MetricId),
                            parameterName);
                    }

                    result.Add(metric);
                }
            }

            result.Sort((left, right) => string.CompareOrdinal(left.MetricId.Value, right.MetricId.Value));
            return new ReadOnlyCollection<LedgerMetric>(result);
        }
    }

    public sealed class ActorImpactRecord
    {
        public ActorImpactRecord(
            StableId impactId,
            StableId actorId,
            StableId actionId,
            StableId scopeId,
            ActorInfluenceChannel channel,
            StableId affectedMetricId,
            string resourceUnit,
            decimal resourcesCommitted,
            decimal directDelta,
            decimal propagatedDelta,
            decimal legalAuthority,
            decimal controlShare,
            decimal organizationReach,
            decimal executionRate,
            decimal geographicMarketRelevance,
            StableId? authoritySourceId = null,
            IEnumerable<StableId> causalChainIds = null)
        {
            LedgerContractGuard.RequireId(impactId, nameof(impactId));
            LedgerContractGuard.RequireId(actorId, nameof(actorId));
            LedgerContractGuard.RequireId(actionId, nameof(actionId));
            LedgerContractGuard.RequireId(scopeId, nameof(scopeId));
            LedgerContractGuard.RequireId(affectedMetricId, nameof(affectedMetricId));
            LedgerContractGuard.RequireText(resourceUnit, nameof(resourceUnit));
            LedgerContractGuard.RequireNullableId(authoritySourceId, nameof(authoritySourceId));
            LedgerContractGuard.RequireNonNegative(resourcesCommitted, nameof(resourcesCommitted));
            LedgerContractGuard.RequireRatio(legalAuthority, nameof(legalAuthority));
            LedgerContractGuard.RequireRatio(controlShare, nameof(controlShare));
            LedgerContractGuard.RequireRatio(organizationReach, nameof(organizationReach));
            LedgerContractGuard.RequireRatio(executionRate, nameof(executionRate));
            LedgerContractGuard.RequireRatio(geographicMarketRelevance, nameof(geographicMarketRelevance));

            ImpactId = impactId;
            ActorId = actorId;
            ActionId = actionId;
            ScopeId = scopeId;
            Channel = channel;
            AffectedMetricId = affectedMetricId;
            ResourceUnit = resourceUnit;
            ResourcesCommitted = resourcesCommitted;
            DirectDelta = directDelta;
            PropagatedDelta = propagatedDelta;
            LegalAuthority = legalAuthority;
            ControlShare = controlShare;
            OrganizationReach = organizationReach;
            ExecutionRate = executionRate;
            GeographicMarketRelevance = geographicMarketRelevance;
            AuthoritySourceId = authoritySourceId;
            CausalChainIds = LedgerContractGuard.CopyUniqueIds(causalChainIds, nameof(causalChainIds));
        }

        public StableId ImpactId { get; }

        public StableId ActorId { get; }

        public StableId ActionId { get; }

        public StableId ScopeId { get; }

        public ActorInfluenceChannel Channel { get; }

        public StableId AffectedMetricId { get; }

        public string ResourceUnit { get; }

        public decimal ResourcesCommitted { get; }

        public decimal DirectDelta { get; }

        public decimal PropagatedDelta { get; }

        public decimal LegalAuthority { get; }

        public decimal ControlShare { get; }

        public decimal OrganizationReach { get; }

        public decimal ExecutionRate { get; }

        public decimal GeographicMarketRelevance { get; }

        public StableId? AuthoritySourceId { get; }

        public IReadOnlyList<StableId> CausalChainIds { get; }

        public decimal EffectiveInfluence =>
            LegalAuthority *
            ControlShare *
            OrganizationReach *
            ExecutionRate *
            GeographicMarketRelevance;
    }

    public sealed class LedgerCloseResult
    {
        internal LedgerCloseResult(int flowLineCount, decimal maximumBalanceError)
        {
            FlowLineCount = flowLineCount;
            MaximumBalanceError = maximumBalanceError;
        }

        public int FlowLineCount { get; }

        public decimal MaximumBalanceError { get; }

        public bool AllInvariantsPassed => MaximumBalanceError <= LedgerContractGuard.Tolerance;
    }

    public sealed class SettlementLedger
    {
        internal SettlementLedger(
            SettlementLedgerHeader header,
            IEnumerable<LedgerFlowLine> flowLines,
            FiscalStatement fiscal,
            IEnumerable<ObligationAging> obligations,
            CapacityAndStockStatement capacityAndStock,
            EconomicOutputStatement economicOutput,
            MilitaryMaterielStatement militaryMateriel,
            IEnumerable<SimulationDriverRecord> appliedDrivers,
            IEnumerable<ActorImpactRecord> actorImpacts)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            if (header.Period.Kind != LedgerPeriodKind.Monthly)
            {
                throw new ArgumentException("A SettlementLedger must close one monthly period.", nameof(header));
            }

            Header = header;
            FlowLines = CopyFlowLines(flowLines);
            Fiscal = fiscal ?? FiscalStatement.Zero();
            Obligations = CopyItems(obligations, nameof(obligations));
            CapacityAndStock = capacityAndStock ?? CapacityAndStockStatement.Empty;
            EconomicOutput = economicOutput ?? EconomicOutputStatement.Empty();
            MilitaryMateriel = militaryMateriel ?? MilitaryMaterielStatement.Empty;
            AppliedDrivers = CopyDriverRecords(appliedDrivers);
            ActorImpacts = CopyItems(actorImpacts, nameof(actorImpacts));

            ValidateMilitaryFlowReferences(FlowLines, MilitaryMateriel);

            var maximumError = Math.Abs(Fiscal.BalanceError);
            for (var index = 0; index < FlowLines.Count; index++)
            {
                maximumError = Math.Max(maximumError, Math.Abs(FlowLines[index].BalanceError));
            }

            CloseResult = new LedgerCloseResult(FlowLines.Count, maximumError);
            if (!CloseResult.AllInvariantsPassed)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Ledger '{0}' cannot close because its maximum balance error is {1}.",
                        header.LedgerId,
                        maximumError));
            }
        }

        public SettlementLedgerHeader Header { get; }

        public IReadOnlyList<LedgerFlowLine> FlowLines { get; }

        public FiscalStatement Fiscal { get; }

        public IReadOnlyList<ObligationAging> Obligations { get; }

        public CapacityAndStockStatement CapacityAndStock { get; }

        public EconomicOutputStatement EconomicOutput { get; }

        public MilitaryMaterielStatement MilitaryMateriel { get; }

        public IReadOnlyList<SimulationDriverRecord> AppliedDrivers { get; }

        public IReadOnlyList<ActorImpactRecord> ActorImpacts { get; }

        public LedgerCloseResult CloseResult { get; }

        public LedgerFlowLine GetRequiredFlowLine(StableId metricId)
        {
            for (var index = 0; index < FlowLines.Count; index++)
            {
                if (FlowLines[index].MetricId.Equals(metricId))
                {
                    return FlowLines[index];
                }
            }

            throw new KeyNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "Ledger metric '{0}' was not found.", metricId));
        }

        private static IReadOnlyList<LedgerFlowLine> CopyFlowLines(IEnumerable<LedgerFlowLine> flowLines)
        {
            var result = new List<LedgerFlowLine>();
            var ids = new HashSet<StableId>();
            if (flowLines != null)
            {
                foreach (var line in flowLines)
                {
                    if (line == null)
                    {
                        throw new ArgumentException("Flow lines cannot contain null.", nameof(flowLines));
                    }

                    if (!ids.Add(line.MetricId))
                    {
                        throw new ArgumentException(
                            string.Format(CultureInfo.InvariantCulture, "Flow metric '{0}' is duplicated.", line.MetricId),
                            nameof(flowLines));
                    }

                    result.Add(line);
                }
            }

            result.Sort((left, right) => string.CompareOrdinal(left.MetricId.Value, right.MetricId.Value));
            return new ReadOnlyCollection<LedgerFlowLine>(result);
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
                        throw new ArgumentException("Ledger collections cannot contain null.", parameterName);
                    }

                    result.Add(item);
                }
            }

            return new ReadOnlyCollection<T>(result);
        }

        private static void ValidateMilitaryFlowReferences(
            IReadOnlyList<LedgerFlowLine> flowLines,
            MilitaryMaterielStatement militaryMateriel)
        {
            for (var materielIndex = 0; materielIndex < militaryMateriel.Materiel.Count; materielIndex++)
            {
                var materiel = militaryMateriel.Materiel[materielIndex];
                var found = false;
                for (var flowIndex = 0; flowIndex < flowLines.Count; flowIndex++)
                {
                    var flow = flowLines[flowIndex];
                    if (!flow.MetricId.Equals(materiel.Flow.MetricId))
                    {
                        continue;
                    }

                    found = true;
                    if (!string.Equals(flow.Unit, materiel.Flow.Unit, StringComparison.Ordinal) ||
                        Math.Abs(flow.Opening - materiel.Flow.Opening) > LedgerContractGuard.Tolerance ||
                        Math.Abs(flow.ExternalInflow - materiel.Flow.ExternalInflow) > LedgerContractGuard.Tolerance ||
                        Math.Abs(flow.InternalInflow - materiel.Flow.InternalInflow) > LedgerContractGuard.Tolerance ||
                        Math.Abs(flow.Produced - materiel.Flow.Produced) > LedgerContractGuard.Tolerance ||
                        Math.Abs(flow.ExternalOutflow - materiel.Flow.ExternalOutflow) > LedgerContractGuard.Tolerance ||
                        Math.Abs(flow.InternalOutflow - materiel.Flow.InternalOutflow) > LedgerContractGuard.Tolerance ||
                        Math.Abs(flow.Consumed - materiel.Flow.Consumed) > LedgerContractGuard.Tolerance ||
                        Math.Abs(flow.LostOrDestroyed - materiel.Flow.LostOrDestroyed) > LedgerContractGuard.Tolerance ||
                        Math.Abs(flow.Closing - materiel.Flow.Closing) > LedgerContractGuard.Tolerance)
                    {
                        throw new ArgumentException(
                            "Military materiel must reference the authoritative matching ledger flow line.",
                            nameof(militaryMateriel));
                    }

                    break;
                }

                if (!found)
                {
                    throw new ArgumentException(
                        "Every military materiel item must exist in the ledger's hard-resource flow lines.",
                        nameof(militaryMateriel));
                }
            }
        }

        private static IReadOnlyList<SimulationDriverRecord> CopyDriverRecords(
            IEnumerable<SimulationDriverRecord> drivers)
        {
            var result = new List<SimulationDriverRecord>();
            var ids = new HashSet<StableId>();
            if (drivers != null)
            {
                foreach (var driver in drivers)
                {
                    if (driver == null)
                    {
                        throw new ArgumentException("Applied drivers cannot contain null.", nameof(drivers));
                    }

                    if (!ids.Add(driver.DriverId))
                    {
                        continue;
                    }

                    result.Add(driver);
                }
            }

            result.Sort((left, right) => string.CompareOrdinal(left.DriverId.Value, right.DriverId.Value));
            return new ReadOnlyCollection<SimulationDriverRecord>(result);
        }
    }

    public sealed class ConsolidationAdjustment
    {
        public ConsolidationAdjustment(
            StableId adjustmentId,
            ConsolidationAdjustmentKind kind,
            StableId metricId,
            string unit,
            decimal amount,
            StableId sourceLedgerId,
            StableId destinationLedgerId,
            string reason)
        {
            LedgerContractGuard.RequireId(adjustmentId, nameof(adjustmentId));
            LedgerContractGuard.RequireId(metricId, nameof(metricId));
            LedgerContractGuard.RequireText(unit, nameof(unit));
            LedgerContractGuard.RequireNonNegative(amount, nameof(amount));
            LedgerContractGuard.RequireId(sourceLedgerId, nameof(sourceLedgerId));
            LedgerContractGuard.RequireId(destinationLedgerId, nameof(destinationLedgerId));
            LedgerContractGuard.RequireText(reason, nameof(reason));

            if (sourceLedgerId.Equals(destinationLedgerId))
            {
                throw new ArgumentException("A consolidation adjustment requires two different ledgers.");
            }

            AdjustmentId = adjustmentId;
            Kind = kind;
            MetricId = metricId;
            Unit = unit;
            Amount = amount;
            SourceLedgerId = sourceLedgerId;
            DestinationLedgerId = destinationLedgerId;
            Reason = reason;
        }

        public StableId AdjustmentId { get; }

        public ConsolidationAdjustmentKind Kind { get; }

        public StableId MetricId { get; }

        public string Unit { get; }

        public decimal Amount { get; }

        public StableId SourceLedgerId { get; }

        public StableId DestinationLedgerId { get; }

        public string Reason { get; }
    }

    public sealed class AnnualClosingLedger
    {
        internal AnnualClosingLedger(
            StableId ledgerId,
            StableId jurisdictionId,
            StableId? parentJurisdictionId,
            int year,
            string ruleVersion,
            IReadOnlyList<StableId> monthlyLedgerIds,
            IReadOnlyList<SimulationResolution> monthlyResolutions,
            IReadOnlyList<StableId> monthlySettlementModelIds,
            IReadOnlyList<LedgerFlowLine> flowLines,
            FiscalStatement fiscal,
            IReadOnlyList<ObligationAging> obligations,
            CapacityAndStockStatement capacityAndStock,
            EconomicOutputStatement economicOutput,
            MilitaryMaterielStatement militaryMateriel,
            IReadOnlyList<SimulationDriverRecord> appliedDrivers,
            IReadOnlyList<ActorImpactRecord> actorImpacts,
            string stateFingerprint)
        {
            LedgerId = ledgerId;
            JurisdictionId = jurisdictionId;
            ParentJurisdictionId = parentJurisdictionId;
            Year = year;
            RuleVersion = ruleVersion;
            MonthlyLedgerIds = monthlyLedgerIds;
            MonthlyResolutions = monthlyResolutions;
            MonthlySettlementModelIds = monthlySettlementModelIds;
            FlowLines = flowLines;
            Fiscal = fiscal;
            Obligations = obligations;
            CapacityAndStock = capacityAndStock;
            EconomicOutput = economicOutput;
            MilitaryMateriel = militaryMateriel;
            AppliedDrivers = appliedDrivers;
            ActorImpacts = actorImpacts;
            StateFingerprint = stateFingerprint;
            CloseResult = new LedgerCloseResult(flowLines.Count, GetMaximumError(flowLines, fiscal));
        }

        public StableId LedgerId { get; }

        public StableId JurisdictionId { get; }

        public StableId? ParentJurisdictionId { get; }

        public int Year { get; }

        public string RuleVersion { get; }

        public IReadOnlyList<StableId> MonthlyLedgerIds { get; }

        public IReadOnlyList<SimulationResolution> MonthlyResolutions { get; }

        public IReadOnlyList<StableId> MonthlySettlementModelIds { get; }

        public IReadOnlyList<LedgerFlowLine> FlowLines { get; }

        public FiscalStatement Fiscal { get; }

        public IReadOnlyList<ObligationAging> Obligations { get; }

        public CapacityAndStockStatement CapacityAndStock { get; }

        public EconomicOutputStatement EconomicOutput { get; }

        public MilitaryMaterielStatement MilitaryMateriel { get; }

        public IReadOnlyList<SimulationDriverRecord> AppliedDrivers { get; }

        public IReadOnlyList<ActorImpactRecord> ActorImpacts { get; }

        public string StateFingerprint { get; }

        public LedgerCloseResult CloseResult { get; }

        public SimulationResolution ClosingResolution => MonthlyResolutions[MonthlyResolutions.Count - 1];

        public LedgerFlowLine GetRequiredFlowLine(StableId metricId)
        {
            for (var index = 0; index < FlowLines.Count; index++)
            {
                if (FlowLines[index].MetricId.Equals(metricId))
                {
                    return FlowLines[index];
                }
            }

            throw new KeyNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "Annual ledger metric '{0}' was not found.", metricId));
        }

        private static decimal GetMaximumError(IReadOnlyList<LedgerFlowLine> lines, FiscalStatement fiscal)
        {
            var maximum = Math.Abs(fiscal.BalanceError);
            for (var index = 0; index < lines.Count; index++)
            {
                maximum = Math.Max(maximum, Math.Abs(lines[index].BalanceError));
            }

            return maximum;
        }
    }

    public sealed class SettlementLedgerService
    {
        public const int MonthsInEconomicYear = 12;

        public SettlementLedger CloseMonth(
            SettlementLedgerHeader header,
            IEnumerable<LedgerFlowLine> flowLines,
            FiscalStatement fiscal = null,
            IEnumerable<ObligationAging> obligations = null,
            CapacityAndStockStatement capacityAndStock = null,
            EconomicOutputStatement economicOutput = null,
            MilitaryMaterielStatement militaryMateriel = null,
            IEnumerable<SimulationDriverRecord> appliedDrivers = null,
            IEnumerable<ActorImpactRecord> actorImpacts = null)
        {
            return new SettlementLedger(
                header,
                flowLines,
                fiscal,
                obligations,
                capacityAndStock,
                economicOutput,
                militaryMateriel,
                appliedDrivers,
                actorImpacts);
        }

        public SettlementLedger ConsolidateChildLedgers(
            SettlementLedgerHeader parentHeader,
            IEnumerable<SettlementLedger> childLedgers,
            SettlementLedger residualLedger,
            IEnumerable<ConsolidationAdjustment> adjustments = null)
        {
            if (parentHeader == null)
            {
                throw new ArgumentNullException(nameof(parentHeader));
            }

            if (childLedgers == null)
            {
                throw new ArgumentNullException(nameof(childLedgers));
            }

            if (residualLedger == null)
            {
                throw new ArgumentNullException(nameof(residualLedger));
            }

            var components = new List<SettlementLedger>();
            foreach (var child in childLedgers)
            {
                if (child == null)
                {
                    throw new ArgumentException("Child ledgers cannot contain null.", nameof(childLedgers));
                }

                components.Add(child);
            }

            components.Add(residualLedger);
            ValidateConsolidationHeader(parentHeader, components, residualLedger);

            var adjustmentList = CopyAdjustments(adjustments, components);
            var flowLines = ConsolidateFlowLines(components, adjustmentList);
            var fiscal = ConsolidateFiscal(components, adjustmentList);
            var obligations = new List<ObligationAging>();
            var actorImpacts = new List<ActorImpactRecord>();
            var appliedDrivers = new List<SimulationDriverRecord>();
            var appliedDriverIds = new HashSet<StableId>();
            for (var index = 0; index < components.Count; index++)
            {
                obligations.AddRange(components[index].Obligations);
                actorImpacts.AddRange(components[index].ActorImpacts);
                for (var driverIndex = 0; driverIndex < components[index].AppliedDrivers.Count; driverIndex++)
                {
                    var driver = components[index].AppliedDrivers[driverIndex];
                    if (appliedDriverIds.Add(driver.DriverId))
                    {
                        appliedDrivers.Add(driver);
                    }
                }
            }

            var capacity = ConsolidateCapacityAndStock(components);
            var economicOutput = ConsolidateEconomicOutput(components, adjustmentList);
            var militaryMateriel = ConsolidateMilitaryMateriel(components, flowLines);
            return CloseMonth(
                parentHeader,
                flowLines,
                fiscal,
                obligations,
                capacity,
                economicOutput,
                militaryMateriel,
                appliedDrivers,
                actorImpacts);
        }

        public AnnualClosingLedger CloseYear(
            StableId annualLedgerId,
            int year,
            IEnumerable<SettlementLedger> monthlyLedgers)
        {
            LedgerContractGuard.RequireId(annualLedgerId, nameof(annualLedgerId));
            if (year <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(year));
            }

            if (monthlyLedgers == null)
            {
                throw new ArgumentNullException(nameof(monthlyLedgers));
            }

            var months = new List<SettlementLedger>();
            foreach (var ledger in monthlyLedgers)
            {
                if (ledger == null)
                {
                    throw new ArgumentException("Monthly ledgers cannot contain null.", nameof(monthlyLedgers));
                }

                months.Add(ledger);
            }

            months.Sort((left, right) => left.Header.Period.Month.CompareTo(right.Header.Period.Month));
            ValidateAnnualSequence(year, months);

            var annualLines = CloseAnnualFlowLines(months);
            var annualFiscal = CloseAnnualFiscal(months);
            var annualEconomicOutput = CloseAnnualEconomicOutput(months);
            var annualMilitaryMateriel = CloseAnnualMilitaryMateriel(months, annualLines);
            var monthIds = new List<StableId>();
            var resolutions = new List<SimulationResolution>();
            var modelIds = new List<StableId>();
            var impacts = new List<ActorImpactRecord>();
            var appliedDrivers = new List<SimulationDriverRecord>();
            var appliedDriverIds = new HashSet<StableId>();
            for (var index = 0; index < months.Count; index++)
            {
                monthIds.Add(months[index].Header.LedgerId);
                resolutions.Add(months[index].Header.Resolution);
                modelIds.Add(months[index].Header.SettlementModelId);
                impacts.AddRange(months[index].ActorImpacts);
                for (var driverIndex = 0; driverIndex < months[index].AppliedDrivers.Count; driverIndex++)
                {
                    var driver = months[index].AppliedDrivers[driverIndex];
                    if (appliedDriverIds.Add(driver.DriverId))
                    {
                        appliedDrivers.Add(driver);
                    }
                }
            }

            var last = months[months.Count - 1];
            var fingerprint = BuildAnnualFingerprint(
                annualLedgerId,
                year,
                annualLines,
                annualFiscal,
                annualEconomicOutput,
                annualMilitaryMateriel,
                appliedDrivers,
                monthIds,
                resolutions,
                modelIds);

            return new AnnualClosingLedger(
                annualLedgerId,
                last.Header.JurisdictionId,
                last.Header.ParentJurisdictionId,
                year,
                last.Header.RuleVersion,
                new ReadOnlyCollection<StableId>(monthIds),
                new ReadOnlyCollection<SimulationResolution>(resolutions),
                new ReadOnlyCollection<StableId>(modelIds),
                new ReadOnlyCollection<LedgerFlowLine>(annualLines),
                annualFiscal,
                last.Obligations,
                last.CapacityAndStock,
                annualEconomicOutput,
                annualMilitaryMateriel,
                new ReadOnlyCollection<SimulationDriverRecord>(appliedDrivers),
                new ReadOnlyCollection<ActorImpactRecord>(impacts),
                fingerprint);
        }

        private static void ValidateConsolidationHeader(
            SettlementLedgerHeader parentHeader,
            IList<SettlementLedger> components,
            SettlementLedger residualLedger)
        {
            var componentIds = new HashSet<StableId>();
            for (var index = 0; index < components.Count; index++)
            {
                var component = components[index];
                if (!component.Header.Period.Equals(parentHeader.Period))
                {
                    throw new ArgumentException("All consolidation ledgers must close the same period.");
                }

                if (!componentIds.Add(component.Header.LedgerId))
                {
                    throw new ArgumentException("A ledger cannot be consolidated twice.");
                }
            }

            if (!parentHeader.ResidualLedgerId.HasValue ||
                !parentHeader.ResidualLedgerId.Value.Equals(residualLedger.Header.LedgerId))
            {
                throw new ArgumentException("The parent header must identify the supplied residual ledger.", nameof(parentHeader));
            }

            var expectedChildren = new HashSet<StableId>(parentHeader.ChildLedgerIds);
            for (var index = 0; index < components.Count - 1; index++)
            {
                if (!expectedChildren.Remove(components[index].Header.LedgerId))
                {
                    throw new ArgumentException("The parent header and child ledger IDs do not match.", nameof(parentHeader));
                }
            }

            if (expectedChildren.Count != 0)
            {
                throw new ArgumentException("The parent header refers to child ledgers that were not supplied.", nameof(parentHeader));
            }
        }

        private static List<ConsolidationAdjustment> CopyAdjustments(
            IEnumerable<ConsolidationAdjustment> adjustments,
            IList<SettlementLedger> components)
        {
            var result = new List<ConsolidationAdjustment>();
            var componentIds = new HashSet<StableId>();
            for (var index = 0; index < components.Count; index++)
            {
                componentIds.Add(components[index].Header.LedgerId);
            }

            if (adjustments != null)
            {
                foreach (var adjustment in adjustments)
                {
                    if (adjustment == null)
                    {
                        throw new ArgumentException("Consolidation adjustments cannot contain null.", nameof(adjustments));
                    }

                    if (!componentIds.Contains(adjustment.SourceLedgerId) ||
                        !componentIds.Contains(adjustment.DestinationLedgerId))
                    {
                        throw new ArgumentException("Every adjustment must point to two component ledgers.", nameof(adjustments));
                    }

                    result.Add(adjustment);
                }
            }

            return result;
        }

        private static List<LedgerFlowLine> ConsolidateFlowLines(
            IList<SettlementLedger> components,
            IList<ConsolidationAdjustment> adjustments)
        {
            var accumulators = new Dictionary<StableId, FlowAccumulator>();
            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                var lines = components[componentIndex].FlowLines;
                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    var line = lines[lineIndex];
                    FlowAccumulator accumulator;
                    if (!accumulators.TryGetValue(line.MetricId, out accumulator))
                    {
                        accumulator = new FlowAccumulator(line.MetricId, line.Unit);
                        accumulators.Add(line.MetricId, accumulator);
                    }
                    else if (!string.Equals(accumulator.Unit, line.Unit, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("A metric cannot be consolidated across different units.");
                    }

                    accumulator.Add(line);
                }
            }

            for (var index = 0; index < adjustments.Count; index++)
            {
                var adjustment = adjustments[index];
                if (adjustment.Kind != ConsolidationAdjustmentKind.ResourceInternalTransfer)
                {
                    continue;
                }

                FlowAccumulator accumulator;
                if (!accumulators.TryGetValue(adjustment.MetricId, out accumulator))
                {
                    throw new InvalidOperationException("A resource adjustment refers to a missing flow metric.");
                }

                if (!string.Equals(accumulator.Unit, adjustment.Unit, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A resource adjustment uses the wrong unit.");
                }

                accumulator.EliminateInternalTransfer(adjustment.Amount);
            }

            var result = new List<LedgerFlowLine>();
            foreach (var pair in accumulators)
            {
                result.Add(pair.Value.ToLine());
            }

            result.Sort((left, right) => string.CompareOrdinal(left.MetricId.Value, right.MetricId.Value));
            return result;
        }

        private static FiscalStatement ConsolidateFiscal(
            IList<SettlementLedger> components,
            IList<ConsolidationAdjustment> adjustments)
        {
            var opening = 0m;
            var assessed = 0m;
            var collected = 0m;
            var received = 0m;
            var borrowed = 0m;
            var mandatory = 0m;
            var discretionary = 0m;
            var debtService = 0m;
            var sent = 0m;
            var closing = 0m;
            var receivable = 0m;
            var arrears = 0m;
            var inTransit = 0m;
            var debt = 0m;

            for (var index = 0; index < components.Count; index++)
            {
                var fiscal = components[index].Fiscal;
                opening += fiscal.OpeningTreasury;
                assessed += fiscal.AssessedRevenue;
                collected += fiscal.CollectedRevenue;
                received += fiscal.TransfersReceived;
                borrowed += fiscal.BorrowingReceived;
                mandatory += fiscal.MandatoryExpensesPaid;
                discretionary += fiscal.DiscretionaryExpensesPaid;
                debtService += fiscal.DebtServicePaid;
                sent += fiscal.TransfersSent;
                closing += fiscal.ClosingTreasury;
                receivable += fiscal.RevenueReceivableClosing;
                arrears += fiscal.PaymentArrearsClosing;
                inTransit += fiscal.RevenueInTransitClosing;
                debt += fiscal.DebtOutstandingClosing;
            }

            for (var index = 0; index < adjustments.Count; index++)
            {
                if (adjustments[index].Kind != ConsolidationAdjustmentKind.FiscalInternalTransfer)
                {
                    continue;
                }

                var amount = adjustments[index].Amount;
                if (received + LedgerContractGuard.Tolerance < amount || sent + LedgerContractGuard.Tolerance < amount)
                {
                    throw new InvalidOperationException("A fiscal adjustment exceeds the matching internal transfers.");
                }

                received -= amount;
                sent -= amount;
            }

            return new FiscalStatement(
                opening,
                assessed,
                collected,
                received,
                borrowed,
                mandatory,
                discretionary,
                debtService,
                sent,
                closing,
                receivable,
                arrears,
                inTransit,
                debt);
        }

        private static CapacityAndStockStatement ConsolidateCapacityAndStock(IList<SettlementLedger> components)
        {
            var stocks = ConsolidateMetrics(components, false);
            var indicators = ConsolidateMetrics(components, true);
            return new CapacityAndStockStatement(stocks, indicators);
        }

        private static EconomicOutputStatement ConsolidateEconomicOutput(
            IList<SettlementLedger> components,
            IList<ConsolidationAdjustment> adjustments)
        {
            var first = components[0].EconomicOutput;
            var sectorAccumulators = new Dictionary<EconomicSectorKind, EconomicSectorAccumulator>();
            var household = 0m;
            var government = 0m;
            var capital = 0m;
            var inventory = 0m;
            var exports = 0m;
            var imports = 0m;
            var discrepancy = 0m;

            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                var statement = components[componentIndex].EconomicOutput;
                if (!string.Equals(first.ValuationUnit, statement.ValuationUnit, StringComparison.Ordinal) ||
                    first.ReferencePriceYear != statement.ReferencePriceYear)
                {
                    throw new InvalidOperationException(
                        "Economic output statements require one valuation unit and reference price year when consolidated.");
                }

                household += statement.HouseholdFinalConsumption;
                government += statement.GovernmentAndMilitaryFinalConsumption;
                capital += statement.GrossFixedCapitalFormation;
                inventory += statement.InventoryChange;
                exports += statement.ExternalExports;
                imports += statement.ExternalImports;
                discrepancy += statement.StatisticalDiscrepancy;

                for (var sectorIndex = 0; sectorIndex < statement.Sectors.Count; sectorIndex++)
                {
                    var sector = statement.Sectors[sectorIndex];
                    EconomicSectorAccumulator accumulator;
                    if (!sectorAccumulators.TryGetValue(sector.Sector, out accumulator))
                    {
                        accumulator = new EconomicSectorAccumulator(sector);
                        sectorAccumulators.Add(sector.Sector, accumulator);
                    }
                    else
                    {
                        accumulator.Add(sector);
                    }
                }
            }

            for (var adjustmentIndex = 0; adjustmentIndex < adjustments.Count; adjustmentIndex++)
            {
                var adjustment = adjustments[adjustmentIndex];
                if (adjustment.Kind != ConsolidationAdjustmentKind.EconomicInternalTrade)
                {
                    continue;
                }

                if (!string.Equals(adjustment.Unit, first.ValuationUnit, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("An economic trade adjustment uses the wrong valuation unit.");
                }

                if (exports + LedgerContractGuard.Tolerance < adjustment.Amount ||
                    imports + LedgerContractGuard.Tolerance < adjustment.Amount)
                {
                    throw new InvalidOperationException("An economic trade adjustment exceeds internal exports or imports.");
                }

                exports -= adjustment.Amount;
                imports -= adjustment.Amount;
            }

            var sectors = new List<EconomicSectorStatement>();
            foreach (var pair in sectorAccumulators)
            {
                sectors.Add(pair.Value.ToStatement());
            }

            sectors.Sort((left, right) => left.Sector.CompareTo(right.Sector));
            return new EconomicOutputStatement(
                sectors,
                first.ValuationUnit,
                first.ReferencePriceYear,
                household,
                government,
                capital,
                inventory,
                exports,
                imports,
                discrepancy);
        }

        private static MilitaryMaterielStatement ConsolidateMilitaryMateriel(
            IList<SettlementLedger> components,
            IList<LedgerFlowLine> consolidatedFlowLines)
        {
            var positions = new Dictionary<StableId, MilitaryPositionAccumulator>();
            var troopStrength = 0m;
            var fitTroops = 0m;
            var foodRequirement = 0m;
            var fodderRequirement = 0m;
            var ammunitionRequirement = 0m;
            var landTransport = 0m;
            var navalTransport = 0m;

            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                var military = components[componentIndex].MilitaryMateriel;
                troopStrength += military.TroopStrength;
                fitTroops += military.FitForDutyTroops;
                foodRequirement += military.MonthlyMilitaryFoodRequirementKg;
                fodderRequirement += military.MonthlyFodderRequirementKg;
                ammunitionRequirement += military.MonthlyAmmunitionRequirement;
                landTransport += military.LandTransportCapacityKg;
                navalTransport += military.NavalTransportCapacityKg;

                for (var itemIndex = 0; itemIndex < military.Materiel.Count; itemIndex++)
                {
                    var item = military.Materiel[itemIndex];
                    MilitaryPositionAccumulator accumulator;
                    if (!positions.TryGetValue(item.Flow.MetricId, out accumulator))
                    {
                        accumulator = new MilitaryPositionAccumulator(item);
                        positions.Add(item.Flow.MetricId, accumulator);
                    }
                    else
                    {
                        accumulator.Add(item);
                    }
                }
            }

            var materiel = new List<MilitaryMaterielLine>();
            foreach (var pair in positions)
            {
                var flow = FindRequiredFlowLine(consolidatedFlowLines, pair.Key);
                materiel.Add(pair.Value.ToLine(flow));
            }

            return new MilitaryMaterielStatement(
                materiel,
                troopStrength,
                fitTroops,
                foodRequirement,
                fodderRequirement,
                ammunitionRequirement,
                landTransport,
                navalTransport);
        }

        private static List<LedgerMetric> ConsolidateMetrics(IList<SettlementLedger> components, bool indicators)
        {
            var groups = new Dictionary<StableId, MetricAccumulator>();
            for (var index = 0; index < components.Count; index++)
            {
                var metrics = indicators
                    ? components[index].CapacityAndStock.DecisionIndicators
                    : components[index].CapacityAndStock.EndStocks;
                for (var metricIndex = 0; metricIndex < metrics.Count; metricIndex++)
                {
                    var metric = metrics[metricIndex];
                    MetricAccumulator accumulator;
                    if (!groups.TryGetValue(metric.MetricId, out accumulator))
                    {
                        accumulator = new MetricAccumulator(metric);
                        groups.Add(metric.MetricId, accumulator);
                    }
                    else
                    {
                        accumulator.Add(metric);
                    }
                }
            }

            var result = new List<LedgerMetric>();
            foreach (var pair in groups)
            {
                result.Add(pair.Value.ToMetric());
            }

            result.Sort((left, right) => string.CompareOrdinal(left.MetricId.Value, right.MetricId.Value));
            return result;
        }

        private static EconomicOutputStatement CloseAnnualEconomicOutput(IList<SettlementLedger> months)
        {
            var first = months[0].EconomicOutput;
            var sectorAccumulators = new Dictionary<EconomicSectorKind, EconomicSectorAccumulator>();
            var household = 0m;
            var government = 0m;
            var capital = 0m;
            var inventory = 0m;
            var exports = 0m;
            var imports = 0m;
            var discrepancy = 0m;

            for (var monthIndex = 0; monthIndex < months.Count; monthIndex++)
            {
                var statement = months[monthIndex].EconomicOutput;
                if (!string.Equals(first.ValuationUnit, statement.ValuationUnit, StringComparison.Ordinal) ||
                    first.ReferencePriceYear != statement.ReferencePriceYear)
                {
                    throw new InvalidOperationException(
                        "An annual economic account cannot change valuation unit or reference price year mid-year.");
                }

                household += statement.HouseholdFinalConsumption;
                government += statement.GovernmentAndMilitaryFinalConsumption;
                capital += statement.GrossFixedCapitalFormation;
                inventory += statement.InventoryChange;
                exports += statement.ExternalExports;
                imports += statement.ExternalImports;
                discrepancy += statement.StatisticalDiscrepancy;

                for (var sectorIndex = 0; sectorIndex < statement.Sectors.Count; sectorIndex++)
                {
                    var sector = statement.Sectors[sectorIndex];
                    EconomicSectorAccumulator accumulator;
                    if (!sectorAccumulators.TryGetValue(sector.Sector, out accumulator))
                    {
                        accumulator = new EconomicSectorAccumulator(sector);
                        sectorAccumulators.Add(sector.Sector, accumulator);
                    }
                    else
                    {
                        accumulator.Add(sector);
                    }
                }
            }

            var sectors = new List<EconomicSectorStatement>();
            foreach (var pair in sectorAccumulators)
            {
                sectors.Add(pair.Value.ToStatement());
            }

            return new EconomicOutputStatement(
                sectors,
                first.ValuationUnit,
                first.ReferencePriceYear,
                household,
                government,
                capital,
                inventory,
                exports,
                imports,
                discrepancy);
        }

        private static MilitaryMaterielStatement CloseAnnualMilitaryMateriel(
            IList<SettlementLedger> months,
            IList<LedgerFlowLine> annualFlowLines)
        {
            var last = months[months.Count - 1].MilitaryMateriel;
            var materiel = new List<MilitaryMaterielLine>();
            for (var index = 0; index < last.Materiel.Count; index++)
            {
                var closingItem = last.Materiel[index];
                var annualFlow = FindRequiredFlowLine(annualFlowLines, closingItem.Flow.MetricId);
                materiel.Add(new MilitaryMaterielLine(
                    closingItem.Kind,
                    annualFlow,
                    closingItem.ServiceableClosing,
                    closingItem.DamagedAwaitingRepairClosing,
                    closingItem.ReservedClosing));
            }

            return new MilitaryMaterielStatement(
                materiel,
                last.TroopStrength,
                last.FitForDutyTroops,
                last.MonthlyMilitaryFoodRequirementKg,
                last.MonthlyFodderRequirementKg,
                last.MonthlyAmmunitionRequirement,
                last.LandTransportCapacityKg,
                last.NavalTransportCapacityKg);
        }

        private static LedgerFlowLine FindRequiredFlowLine(
            IList<LedgerFlowLine> flowLines,
            StableId metricId)
        {
            for (var index = 0; index < flowLines.Count; index++)
            {
                if (flowLines[index].MetricId.Equals(metricId))
                {
                    return flowLines[index];
                }
            }

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "Flow metric '{0}' is missing.", metricId));
        }

        private static void ValidateAnnualSequence(int year, IList<SettlementLedger> months)
        {
            if (months.Count != MonthsInEconomicYear)
            {
                throw new ArgumentException("An annual closing requires exactly twelve monthly ledgers.", nameof(months));
            }

            var jurisdictionId = months[0].Header.JurisdictionId;
            var ruleVersion = months[0].Header.RuleVersion;
            for (var index = 0; index < months.Count; index++)
            {
                var header = months[index].Header;
                if (header.Period.Year != year || header.Period.Month != index + 1)
                {
                    throw new ArgumentException("Annual ledgers must contain each month exactly once and in the requested year.");
                }

                if (!header.JurisdictionId.Equals(jurisdictionId))
                {
                    throw new ArgumentException("Annual ledgers must belong to one jurisdiction.");
                }

                if (!string.Equals(header.RuleVersion, ruleVersion, StringComparison.Ordinal))
                {
                    throw new ArgumentException("A rule-version change requires an explicit migration before annual closing.");
                }
            }
        }

        private static List<LedgerFlowLine> CloseAnnualFlowLines(IList<SettlementLedger> months)
        {
            var firstLines = months[0].FlowLines;
            var metricIds = new List<StableId>();
            for (var index = 0; index < firstLines.Count; index++)
            {
                metricIds.Add(firstLines[index].MetricId);
            }

            var result = new List<LedgerFlowLine>();
            for (var metricIndex = 0; metricIndex < metricIds.Count; metricIndex++)
            {
                var metricId = metricIds[metricIndex];
                var first = months[0].GetRequiredFlowLine(metricId);
                var previousClosing = first.Opening;
                var externalIn = 0m;
                var internalIn = 0m;
                var produced = 0m;
                var externalOut = 0m;
                var internalOut = 0m;
                var consumed = 0m;
                var lost = 0m;
                LedgerFlowLine current = null;

                for (var monthIndex = 0; monthIndex < months.Count; monthIndex++)
                {
                    current = months[monthIndex].GetRequiredFlowLine(metricId);
                    if (!string.Equals(first.Unit, current.Unit, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("A yearly flow metric changed units.");
                    }

                    if (Math.Abs(current.Opening - previousClosing) > LedgerContractGuard.Tolerance)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Metric '{0}' is not continuous between monthly ledgers.",
                                metricId));
                    }

                    externalIn += current.ExternalInflow;
                    internalIn += current.InternalInflow;
                    produced += current.Produced;
                    externalOut += current.ExternalOutflow;
                    internalOut += current.InternalOutflow;
                    consumed += current.Consumed;
                    lost += current.LostOrDestroyed;
                    previousClosing = current.Closing;
                }

                result.Add(new LedgerFlowLine(
                    metricId,
                    first.Unit,
                    first.Opening,
                    externalIn,
                    internalIn,
                    produced,
                    externalOut,
                    internalOut,
                    consumed,
                    lost,
                    current.Closing));
            }

            for (var monthIndex = 1; monthIndex < months.Count; monthIndex++)
            {
                if (months[monthIndex].FlowLines.Count != metricIds.Count)
                {
                    throw new InvalidOperationException("Every month must expose the same flow metrics, including zero-flow lines.");
                }
            }

            return result;
        }

        private static FiscalStatement CloseAnnualFiscal(IList<SettlementLedger> months)
        {
            var opening = months[0].Fiscal.OpeningTreasury;
            var previousClosing = opening;
            var assessed = 0m;
            var collected = 0m;
            var received = 0m;
            var borrowed = 0m;
            var mandatory = 0m;
            var discretionary = 0m;
            var debtService = 0m;
            var sent = 0m;

            for (var index = 0; index < months.Count; index++)
            {
                var fiscal = months[index].Fiscal;
                if (Math.Abs(fiscal.OpeningTreasury - previousClosing) > LedgerContractGuard.Tolerance)
                {
                    throw new InvalidOperationException("Treasury balances are not continuous between monthly ledgers.");
                }

                assessed += fiscal.AssessedRevenue;
                collected += fiscal.CollectedRevenue;
                received += fiscal.TransfersReceived;
                borrowed += fiscal.BorrowingReceived;
                mandatory += fiscal.MandatoryExpensesPaid;
                discretionary += fiscal.DiscretionaryExpensesPaid;
                debtService += fiscal.DebtServicePaid;
                sent += fiscal.TransfersSent;
                previousClosing = fiscal.ClosingTreasury;
            }

            var last = months[months.Count - 1].Fiscal;
            return new FiscalStatement(
                opening,
                assessed,
                collected,
                received,
                borrowed,
                mandatory,
                discretionary,
                debtService,
                sent,
                last.ClosingTreasury,
                last.RevenueReceivableClosing,
                last.PaymentArrearsClosing,
                last.RevenueInTransitClosing,
                last.DebtOutstandingClosing);
        }

        private static string BuildAnnualFingerprint(
            StableId annualLedgerId,
            int year,
            IList<LedgerFlowLine> lines,
            FiscalStatement fiscal,
            EconomicOutputStatement economicOutput,
            MilitaryMaterielStatement militaryMateriel,
            IList<SimulationDriverRecord> appliedDrivers,
            IList<StableId> monthIds,
            IList<SimulationResolution> resolutions,
            IList<StableId> modelIds)
        {
            var builder = new StringBuilder();
            builder.Append(annualLedgerId.Value).Append('|').Append(year.ToString(CultureInfo.InvariantCulture));
            for (var index = 0; index < monthIds.Count; index++)
            {
                builder.Append('|').Append(monthIds[index].Value);
                builder.Append(':').Append(((int)resolutions[index]).ToString(CultureInfo.InvariantCulture));
                builder.Append(':').Append(modelIds[index].Value);
            }

            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                builder.Append('|').Append(line.MetricId.Value);
                AppendDecimal(builder, line.Opening);
                AppendDecimal(builder, line.ExternalInflow);
                AppendDecimal(builder, line.InternalInflow);
                AppendDecimal(builder, line.Produced);
                AppendDecimal(builder, line.ExternalOutflow);
                AppendDecimal(builder, line.InternalOutflow);
                AppendDecimal(builder, line.Consumed);
                AppendDecimal(builder, line.LostOrDestroyed);
                AppendDecimal(builder, line.Closing);
            }

            AppendDecimal(builder, fiscal.OpeningTreasury);
            AppendDecimal(builder, fiscal.AssessedRevenue);
            AppendDecimal(builder, fiscal.CollectedRevenue);
            AppendDecimal(builder, fiscal.TransfersReceived);
            AppendDecimal(builder, fiscal.TransfersSent);
            AppendDecimal(builder, fiscal.ClosingTreasury);
            AppendDecimal(builder, fiscal.RevenueReceivableClosing);
            AppendDecimal(builder, economicOutput.NominalGrossOutput);
            AppendDecimal(builder, economicOutput.NominalIntermediateConsumption);
            AppendDecimal(builder, economicOutput.NominalValueAdded);
            AppendDecimal(builder, economicOutput.RealValueAddedAtReferencePrices);
            for (var index = 0; index < militaryMateriel.Materiel.Count; index++)
            {
                var item = militaryMateriel.Materiel[index];
                builder.Append('|').Append(item.Flow.MetricId.Value);
                builder.Append(':').Append(((int)item.Kind).ToString(CultureInfo.InvariantCulture));
                AppendDecimal(builder, item.ServiceableClosing);
                AppendDecimal(builder, item.DamagedAwaitingRepairClosing);
                AppendDecimal(builder, item.ReservedClosing);
            }

            var sortedDrivers = new List<SimulationDriverRecord>(appliedDrivers);
            sortedDrivers.Sort((left, right) => string.CompareOrdinal(left.DriverId.Value, right.DriverId.Value));
            for (var index = 0; index < sortedDrivers.Count; index++)
            {
                builder.Append('|').Append(sortedDrivers[index].DriverId.Value);
            }

            return DeterministicHash(builder.ToString());
        }

        private static void AppendDecimal(StringBuilder builder, decimal value)
        {
            builder.Append(':').Append(value.ToString("0.############################", CultureInfo.InvariantCulture));
        }

        private static string DeterministicHash(string value)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                var hash = offset;
                for (var index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= prime;
                }

                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        private sealed class EconomicSectorAccumulator
        {
            private readonly EconomicSectorKind _sector;
            private readonly string _valuationUnit;
            private readonly int _referencePriceYear;
            private decimal _nominalGrossOutput;
            private decimal _nominalIntermediateConsumption;
            private decimal _realGrossOutput;
            private decimal _realIntermediateConsumption;
            private decimal _laborCompensation;
            private decimal _rent;
            private decimal _netProductionTaxes;
            private decimal _mixedIncomeAndSurplus;
            private decimal _laborPersonMonths;
            private decimal _weightedCapacityUtilization;
            private decimal _capacityWeight;
            private decimal _salesValue;
            private decimal _inventoryChange;

            public EconomicSectorAccumulator(EconomicSectorStatement sector)
            {
                _sector = sector.Sector;
                _valuationUnit = sector.ValuationUnit;
                _referencePriceYear = sector.ReferencePriceYear;
                Add(sector);
            }

            public void Add(EconomicSectorStatement sector)
            {
                if (sector.Sector != _sector ||
                    !string.Equals(sector.ValuationUnit, _valuationUnit, StringComparison.Ordinal) ||
                    sector.ReferencePriceYear != _referencePriceYear)
                {
                    throw new InvalidOperationException("An economic sector changed identity or valuation basis.");
                }

                _nominalGrossOutput += sector.NominalGrossOutput;
                _nominalIntermediateConsumption += sector.NominalIntermediateConsumption;
                _realGrossOutput += sector.RealGrossOutputAtReferencePrices;
                _realIntermediateConsumption += sector.RealIntermediateConsumptionAtReferencePrices;
                _laborCompensation += sector.LaborCompensation;
                _rent += sector.LandAndAssetRent;
                _netProductionTaxes += sector.NetProductionTaxes;
                _mixedIncomeAndSurplus += sector.HouseholdMixedIncomeAndOperatingSurplus;
                _laborPersonMonths += sector.LaborPersonMonths;
                var weight = sector.LaborPersonMonths > 0m ? sector.LaborPersonMonths : 1m;
                _weightedCapacityUtilization += sector.CapacityUtilization * weight;
                _capacityWeight += weight;
                _salesValue += sector.SalesValue;
                _inventoryChange += sector.InventoryChangeValue;
            }

            public EconomicSectorStatement ToStatement()
            {
                return new EconomicSectorStatement(
                    _sector,
                    _valuationUnit,
                    _referencePriceYear,
                    _nominalGrossOutput,
                    _nominalIntermediateConsumption,
                    _realGrossOutput,
                    _realIntermediateConsumption,
                    _laborCompensation,
                    _rent,
                    _netProductionTaxes,
                    _mixedIncomeAndSurplus,
                    _laborPersonMonths,
                    _capacityWeight <= 0m ? 0m : _weightedCapacityUtilization / _capacityWeight,
                    _salesValue,
                    _inventoryChange);
            }
        }

        private sealed class MilitaryPositionAccumulator
        {
            private readonly MilitaryMaterielKind _kind;
            private decimal _serviceable;
            private decimal _damaged;
            private decimal _reserved;

            public MilitaryPositionAccumulator(MilitaryMaterielLine line)
            {
                _kind = line.Kind;
                Add(line);
            }

            public void Add(MilitaryMaterielLine line)
            {
                if (line.Kind != _kind)
                {
                    throw new InvalidOperationException("One military metric cannot change materiel kind.");
                }

                _serviceable += line.ServiceableClosing;
                _damaged += line.DamagedAwaitingRepairClosing;
                _reserved += line.ReservedClosing;
            }

            public MilitaryMaterielLine ToLine(LedgerFlowLine flow)
            {
                return new MilitaryMaterielLine(_kind, flow, _serviceable, _damaged, _reserved);
            }
        }

        private sealed class FlowAccumulator
        {
            public FlowAccumulator(StableId metricId, string unit)
            {
                MetricId = metricId;
                Unit = unit;
            }

            public StableId MetricId { get; }

            public string Unit { get; }

            public decimal Opening;
            public decimal ExternalInflow;
            public decimal InternalInflow;
            public decimal Produced;
            public decimal ExternalOutflow;
            public decimal InternalOutflow;
            public decimal Consumed;
            public decimal Lost;
            public decimal Closing;

            public void Add(LedgerFlowLine line)
            {
                Opening += line.Opening;
                ExternalInflow += line.ExternalInflow;
                InternalInflow += line.InternalInflow;
                Produced += line.Produced;
                ExternalOutflow += line.ExternalOutflow;
                InternalOutflow += line.InternalOutflow;
                Consumed += line.Consumed;
                Lost += line.LostOrDestroyed;
                Closing += line.Closing;
            }

            public void EliminateInternalTransfer(decimal amount)
            {
                if (InternalInflow + LedgerContractGuard.Tolerance < amount ||
                    InternalOutflow + LedgerContractGuard.Tolerance < amount)
                {
                    throw new InvalidOperationException("A resource adjustment exceeds the matching internal transfers.");
                }

                InternalInflow -= amount;
                InternalOutflow -= amount;
            }

            public LedgerFlowLine ToLine()
            {
                return new LedgerFlowLine(
                    MetricId,
                    Unit,
                    Opening,
                    ExternalInflow,
                    InternalInflow,
                    Produced,
                    ExternalOutflow,
                    InternalOutflow,
                    Consumed,
                    Lost,
                    Closing);
            }
        }

        private sealed class MetricAccumulator
        {
            private readonly StableId _metricId;
            private readonly LedgerMetricDomain _domain;
            private readonly string _unit;
            private readonly MetricAggregationMode _mode;
            private decimal _value;
            private decimal _weight;
            private int _count;

            public MetricAccumulator(LedgerMetric metric)
            {
                _metricId = metric.MetricId;
                _domain = metric.Domain;
                _unit = metric.Unit;
                _mode = metric.AggregationMode;
                Add(metric);
            }

            public void Add(LedgerMetric metric)
            {
                if (metric.Domain != _domain ||
                    metric.AggregationMode != _mode ||
                    !string.Equals(metric.Unit, _unit, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A summary metric changed its domain, unit or aggregation rule.");
                }

                if (_count == 0)
                {
                    _value = metric.Value;
                    _weight = metric.Weight;
                    _count = 1;
                    return;
                }

                switch (_mode)
                {
                    case MetricAggregationMode.Sum:
                        _value += metric.Value;
                        _weight += metric.Weight;
                        break;
                    case MetricAggregationMode.WeightedAverage:
                        _value = (_value * _weight + metric.Value * metric.Weight) / (_weight + metric.Weight);
                        _weight += metric.Weight;
                        break;
                    case MetricAggregationMode.Minimum:
                        _value = Math.Min(_value, metric.Value);
                        _weight += metric.Weight;
                        break;
                    case MetricAggregationMode.Maximum:
                        _value = Math.Max(_value, metric.Value);
                        _weight += metric.Weight;
                        break;
                    case MetricAggregationMode.Latest:
                        _value = metric.Value;
                        _weight = metric.Weight;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _count++;
            }

            public LedgerMetric ToMetric()
            {
                return new LedgerMetric(_metricId, _domain, _unit, _value, _mode, Math.Max(_weight, 1m));
            }
        }
    }

    internal static class LedgerContractGuard
    {
        public const decimal Tolerance = 0.000001m;

        public static void RequireId(StableId id, string parameterName)
        {
            if (string.IsNullOrEmpty(id.Value))
            {
                throw new ArgumentException("A stable ID is required.", parameterName);
            }
        }

        public static void RequireNullableId(StableId? id, string parameterName)
        {
            if (id.HasValue && string.IsNullOrEmpty(id.Value.Value))
            {
                throw new ArgumentException("A nullable stable ID cannot contain an empty value.", parameterName);
            }
        }

        public static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Text is required.", parameterName);
            }
        }

        public static void RequireNonNegative(decimal value, string parameterName)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        public static void RequireRatio(decimal value, string parameterName)
        {
            if (value < 0m || value > 1m)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        public static IReadOnlyList<StableId> CopyUniqueIds(IEnumerable<StableId> source, string parameterName)
        {
            var result = new List<StableId>();
            var ids = new HashSet<StableId>();
            if (source != null)
            {
                foreach (var id in source)
                {
                    RequireId(id, parameterName);
                    if (!ids.Add(id))
                    {
                        throw new ArgumentException("Stable IDs must be unique within the collection.", parameterName);
                    }

                    result.Add(id);
                }
            }

            return new ReadOnlyCollection<StableId>(result);
        }
    }
}
