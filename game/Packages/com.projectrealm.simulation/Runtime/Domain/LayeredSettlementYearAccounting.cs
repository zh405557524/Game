using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ProjectRealm.Domain
{
    public sealed class LayeredSettlementYearAccountingSummary
    {
        internal LayeredSettlementYearAccountingSummary(
            IReadOnlyList<AnnualClosingLedger> annualLedgers,
            GovernmentReport governmentReport,
            GovernmentDecisionPacket nextYearDecisionPacket,
            AnnualGovernmentPlan nextYearGovernmentPlan)
        {
            AnnualLedgers = annualLedgers;
            GovernmentReport = governmentReport;
            NextYearDecisionPacket = nextYearDecisionPacket;
            NextYearGovernmentPlan = nextYearGovernmentPlan;
        }

        public IReadOnlyList<AnnualClosingLedger> AnnualLedgers { get; }

        public GovernmentReport GovernmentReport { get; }

        public GovernmentDecisionPacket NextYearDecisionPacket { get; }

        public AnnualGovernmentPlan NextYearGovernmentPlan { get; }

        public AnnualClosingLedger GetRequiredLedger(SimulationResolution resolution)
        {
            for (var index = 0; index < AnnualLedgers.Count; index++)
            {
                if (AnnualLedgers[index].ClosingResolution == resolution)
                {
                    return AnnualLedgers[index];
                }
            }

            throw new KeyNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "Annual ledger '{0}' was not found.", resolution));
        }
    }

    internal static class LayeredSettlementYearAccountingBuilder
    {
        private const int EconomicYear = 1628;
        private const string RuleVersion = "year-probe-rules.v2";
        private const string SilverEquivalentUnit = "silver-equivalent";

        private static readonly StableId GrainMetricId = new StableId("commodity.grain.kg");
        private static readonly StableId TaxObligationId = new StableId("obligation.land-tax.silver");
        private static readonly StableId VillageScopeId = new StableId("probe.scope.village");
        private static readonly StableId TownshipScopeId = new StableId("probe.scope.township");
        private static readonly StableId CountyScopeId = new StableId("probe.scope.county");
        private static readonly StableId ZhouScopeId = new StableId("probe.scope.zhou");
        private static readonly StableId FuScopeId = new StableId("probe.scope.fu");
        private static readonly StableId SiScopeId = new StableId("probe.scope.si");
        private static readonly StableId RealmScopeId = new StableId("probe.scope.realm");
        private static readonly StableId RealmAuthorityId = new StableId("probe.authority.realm");

        public static LayeredSettlementYearAccountingSummary Build(
            LayeredSettlementYearScenario scenario,
            IList<MonthlySettlementSnapshot> snapshots)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (snapshots == null || snapshots.Count != SettlementLedgerService.MonthsInEconomicYear)
            {
                throw new ArgumentException("The accounting probe requires twelve monthly snapshots.", nameof(snapshots));
            }

            var service = new SettlementLedgerService();
            var monthlyByResolution = CreateMonthlyBuckets();
            var villageOpening = scenario.VillageOpeningGrainKg;
            var countyOpening = scenario.CountyOpeningGrainKg;
            var regionalOpening = scenario.RegionalOpeningGrainKg;
            var countyTreasuryOpening = scenario.CountyTreasuryOpeningSilver;
            var realmTreasuryOpening = scenario.RegionalTreasuryOpeningSilver;
            var drivers = CreateDroughtDrivers();

            for (var index = 0; index < snapshots.Count; index++)
            {
                var snapshot = snapshots[index];
                var month = snapshot.Month;
                var period = LedgerPeriod.Monthly(EconomicYear, month);
                var villageProduction = scenario.GetVillageProductionKg(month);
                var countyProduction = scenario.GetCountyProductionKg(month);
                var regionalProduction = scenario.GetRegionalProductionKg(month);
                var villageReproduction = scenario.GetVillageReproductionUseKg(month);
                var countyReproduction = scenario.GetCountyReproductionUseKg(month);
                var regionalReproduction = scenario.GetRegionalReproductionUseKg(month);
                var rent = scenario.GetVillageRentKg(month);
                var countyConsumption = CalculateConsumption(
                    countyOpening + countyProduction - countyReproduction + rent - snapshot.VillagePurchasedKg,
                    scenario.CountyAdultEquivalentPopulation,
                    scenario.AnnualFoodKgPerAdultEquivalent,
                    month);
                var regionalConsumption = CalculateConsumption(
                    regionalOpening + regionalProduction - regionalReproduction,
                    scenario.RegionalAdultEquivalentPopulation,
                    scenario.AnnualFoodKgPerAdultEquivalent,
                    month);
                var villageLoss = ClampZero(
                    villageOpening +
                    villageProduction +
                    snapshot.VillagePurchasedKg -
                    rent -
                    snapshot.VillageSoldKg -
                    snapshot.VillageConsumedKg -
                    villageReproduction -
                    snapshot.VillageGrainKg);
                var countyLoss = ClampZero(
                    countyOpening +
                    countyProduction +
                    rent +
                    snapshot.VillageSoldKg -
                    snapshot.VillagePurchasedKg -
                    countyConsumption -
                    countyReproduction -
                    snapshot.CountyGrainKg);
                var regionalLoss = ClampZero(
                    regionalOpening +
                    regionalProduction -
                    regionalConsumption -
                    regionalReproduction -
                    snapshot.RegionalGrainKg);
                var taxCall = scenario.GetTaxCallSilver(month);
                var countyTreasuryClosing =
                    countyTreasuryOpening + snapshot.TaxPaidSilver - snapshot.TaxRemittedSilver;
                var realmTreasuryClosing = realmTreasuryOpening + snapshot.TaxRemittedSilver;

                var villageLedger = service.CloseMonth(
                    CreateHeader(
                        LedgerId("village", month),
                        VillageScopeId,
                        CountyScopeId,
                        SimulationResolution.VillageDetailed,
                        period,
                        "model.village-detailed.v1"),
                    new[]
                    {
                        CreateGrainFlow(
                            villageOpening,
                            snapshot.VillagePurchasedKg,
                            villageProduction,
                            rent + snapshot.VillageSoldKg,
                            snapshot.VillageConsumedKg + villageReproduction,
                            villageLoss,
                            snapshot.VillageGrainKg)
                    },
                    obligations: new[]
                    {
                        new ObligationAging(
                            TaxObligationId,
                            CountyScopeId,
                            "silver",
                            snapshot.TaxPaidSilver + snapshot.VillageTaxArrearsSilver,
                            snapshot.TaxPaidSilver,
                            snapshot.VillageTaxArrearsSilver,
                            0m,
                            0m,
                            0m)
                    },
                    capacityAndStock: CreateScopeSummary(
                        scenario.VillageAdultEquivalentPopulation,
                        snapshot.VillageGrainKg,
                        snapshot.VillageDebtSilver,
                        snapshot.VillageTaxArrearsSilver,
                        scenario.AnnualFoodKgPerAdultEquivalent),
                    economicOutput: CreateAgricultureEconomy(
                        scenario,
                        villageProduction,
                        villageReproduction,
                        snapshot.VillageConsumedKg,
                        snapshot.VillagePurchasedKg,
                        rent + snapshot.VillageSoldKg,
                        snapshot.VillageSoldKg,
                        villageOpening,
                        snapshot.VillageGrainKg),
                    appliedDrivers: new[] { drivers[0] });

                var townshipLedger = service.CloseMonth(
                    CreateHeader(
                        LedgerId("township", month),
                        TownshipScopeId,
                        CountyScopeId,
                        SimulationResolution.TownshipNode,
                        period,
                        "model.township-node.v1"),
                    new[] { CreateGrainFlow(0m, 0m, 0m, 0m, 0m, 0m, 0m) });

                var countyResidualLedger = service.CloseMonth(
                    CreateHeader(
                        LedgerId("county-residual", month),
                        CountyScopeId,
                        ZhouScopeId,
                        SimulationResolution.CountyFull,
                        period,
                        "model.county-residual.v1"),
                    new[]
                    {
                        CreateGrainFlow(
                            countyOpening,
                            rent + snapshot.VillageSoldKg,
                            countyProduction,
                            snapshot.VillagePurchasedKg,
                            countyConsumption + countyReproduction,
                            countyLoss,
                            snapshot.CountyGrainKg)
                    },
                    new FiscalStatement(
                        countyTreasuryOpening,
                        taxCall,
                        snapshot.TaxPaidSilver,
                        0m,
                        0m,
                        0m,
                        0m,
                        0m,
                        snapshot.TaxRemittedSilver,
                        countyTreasuryClosing,
                        snapshot.VillageTaxArrearsSilver,
                        0m,
                        0m,
                        0m),
                    capacityAndStock: CreateScopeSummary(
                        scenario.CountyAdultEquivalentPopulation,
                        snapshot.CountyGrainKg,
                        0m,
                        snapshot.VillageTaxArrearsSilver,
                        scenario.AnnualFoodKgPerAdultEquivalent),
                    economicOutput: CreateAgricultureEconomy(
                        scenario,
                        countyProduction,
                        countyReproduction,
                        countyConsumption,
                        rent + snapshot.VillageSoldKg,
                        snapshot.VillagePurchasedKg,
                        snapshot.VillagePurchasedKg,
                        countyOpening,
                        snapshot.CountyGrainKg),
                    appliedDrivers: new[] { drivers[1] });

                var countyHeader = CreateConsolidatedHeader(
                    LedgerId("county", month),
                    CountyScopeId,
                    ZhouScopeId,
                    SimulationResolution.CountyFull,
                    period,
                    "model.county-full.v1",
                    new[] { villageLedger.Header.LedgerId, townshipLedger.Header.LedgerId },
                    countyResidualLedger.Header.LedgerId,
                    SettlementLedgerDataKind.AuthoritativeTruth);
                var countyAdjustments = CreateCountyAdjustments(
                    scenario,
                    month,
                    villageLedger.Header.LedgerId,
                    countyResidualLedger.Header.LedgerId,
                    rent + snapshot.VillageSoldKg + snapshot.VillagePurchasedKg);
                var countyLedger = service.ConsolidateChildLedgers(
                    countyHeader,
                    new[] { villageLedger, townshipLedger },
                    countyResidualLedger,
                    countyAdjustments);

                var zhouResidual = CreateZeroResidual(
                    service,
                    "zhou-residual",
                    ZhouScopeId,
                    FuScopeId,
                    SimulationResolution.ZhouAggregate,
                    period,
                    month);
                var zhouLedger = service.ConsolidateChildLedgers(
                    CreateConsolidatedHeader(
                        LedgerId("zhou", month),
                        ZhouScopeId,
                        FuScopeId,
                        SimulationResolution.ZhouAggregate,
                        period,
                        "model.zhou-aggregate.v1",
                        new[] { countyLedger.Header.LedgerId },
                        zhouResidual.Header.LedgerId,
                        SettlementLedgerDataKind.EstimatedAggregate),
                    new[] { countyLedger },
                    zhouResidual);

                var fuResidual = CreateZeroResidual(
                    service,
                    "fu-residual",
                    FuScopeId,
                    SiScopeId,
                    SimulationResolution.FuAggregate,
                    period,
                    month);
                var fuLedger = service.ConsolidateChildLedgers(
                    CreateConsolidatedHeader(
                        LedgerId("fu", month),
                        FuScopeId,
                        SiScopeId,
                        SimulationResolution.FuAggregate,
                        period,
                        "model.fu-aggregate.v1",
                        new[] { zhouLedger.Header.LedgerId },
                        fuResidual.Header.LedgerId,
                        SettlementLedgerDataKind.EstimatedAggregate),
                    new[] { zhouLedger },
                    fuResidual);

                var siResidual = service.CloseMonth(
                    CreateHeader(
                        LedgerId("si-residual", month),
                        SiScopeId,
                        RealmScopeId,
                        SimulationResolution.SiStrategic,
                        period,
                        "model.si-residual.v1",
                        SettlementLedgerDataKind.EstimatedAggregate),
                    new[]
                    {
                        CreateGrainFlow(
                            regionalOpening,
                            0m,
                            regionalProduction,
                            0m,
                            regionalConsumption + regionalReproduction,
                            regionalLoss,
                            snapshot.RegionalGrainKg)
                    },
                    capacityAndStock: CreateScopeSummary(
                        scenario.RegionalAdultEquivalentPopulation,
                        snapshot.RegionalGrainKg,
                        0m,
                        0m,
                        scenario.AnnualFoodKgPerAdultEquivalent),
                    economicOutput: CreateAgricultureEconomy(
                        scenario,
                        regionalProduction,
                        regionalReproduction,
                        regionalConsumption,
                        0m,
                        0m,
                        0m,
                        regionalOpening,
                        snapshot.RegionalGrainKg),
                    appliedDrivers: new[] { drivers[2] });
                var siLedger = service.ConsolidateChildLedgers(
                    CreateConsolidatedHeader(
                        LedgerId("si", month),
                        SiScopeId,
                        RealmScopeId,
                        SimulationResolution.SiStrategic,
                        period,
                        "model.si-strategic.v1",
                        new[] { fuLedger.Header.LedgerId },
                        siResidual.Header.LedgerId,
                        SettlementLedgerDataKind.EstimatedAggregate),
                    new[] { fuLedger },
                    siResidual);

                var realmLedger = service.CloseMonth(
                    CreateHeader(
                        LedgerId("realm", month),
                        RealmScopeId,
                        null,
                        SimulationResolution.RealmCentral,
                        period,
                        "model.realm-central.v1"),
                    null,
                    new FiscalStatement(
                        realmTreasuryOpening,
                        0m,
                        0m,
                        snapshot.TaxRemittedSilver,
                        0m,
                        0m,
                        0m,
                        0m,
                        0m,
                        realmTreasuryClosing,
                        0m,
                        0m,
                        0m,
                        0m));

                monthlyByResolution[SimulationResolution.VillageDetailed].Add(villageLedger);
                monthlyByResolution[SimulationResolution.TownshipNode].Add(townshipLedger);
                monthlyByResolution[SimulationResolution.CountyFull].Add(countyLedger);
                monthlyByResolution[SimulationResolution.ZhouAggregate].Add(zhouLedger);
                monthlyByResolution[SimulationResolution.FuAggregate].Add(fuLedger);
                monthlyByResolution[SimulationResolution.SiStrategic].Add(siLedger);
                monthlyByResolution[SimulationResolution.RealmCentral].Add(realmLedger);

                villageOpening = snapshot.VillageGrainKg;
                countyOpening = snapshot.CountyGrainKg;
                regionalOpening = snapshot.RegionalGrainKg;
                countyTreasuryOpening = countyTreasuryClosing;
                realmTreasuryOpening = realmTreasuryClosing;
            }

            var annualLedgers = new List<AnnualClosingLedger>();
            foreach (SimulationResolution resolution in Enum.GetValues(typeof(SimulationResolution)))
            {
                annualLedgers.Add(service.CloseYear(
                    new StableId("probe.annual." + resolution.ToString().ToLowerInvariant()),
                    EconomicYear,
                    monthlyByResolution[resolution]));
            }

            var countyAnnual = GetRequiredLedger(annualLedgers, SimulationResolution.CountyFull);
            var realmAnnual = GetRequiredLedger(annualLedgers, SimulationResolution.RealmCentral);
            var report = new GovernmentReportService().BuildGovernmentReport(
                countyAnnual,
                new StableId("probe.report.county-to-realm.1628"),
                RealmAuthorityId,
                LedgerPeriod.Monthly(1629, 1),
                new GovernmentReportingPolicy(1, 1m, 1m, 1m));
            var decisionService = new GovernmentDecisionService();
            var packet = decisionService.BuildDecisionPacket(
                new StableId("probe.packet.realm.1629"),
                RealmAuthorityId,
                RealmScopeId,
                1629,
                realmAnnual.Fiscal.ClosingTreasury,
                realmAnnual.Fiscal.TransfersReceived,
                report.Fiscal.AssessedRevenue,
                report.Fiscal.RevenueInTransit,
                report.Fiscal.RevenueReceivable,
                0m,
                scenario.RegionalTreasuryOpeningSilver,
                2m,
                1,
                new[]
                {
                    new BudgetDemand(
                        new StableId("probe.demand.central-administration"),
                        GovernmentBudgetCategory.Administration,
                        6m,
                        0,
                        true,
                        "Minimum central administration"),
                    new BudgetDemand(
                        new StableId("probe.demand.central-military"),
                        GovernmentBudgetCategory.Military,
                        4m,
                        1,
                        true,
                        "Minimum military readiness")
                },
                new[]
                {
                    new GovernmentActionCandidate(
                        new StableId("probe.action.new-project"),
                        RealmScopeId,
                        GovernmentActionKind.Construction,
                        GovernmentBudgetCategory.Infrastructure,
                        5m,
                        1m,
                        10m,
                        0.8m,
                        0.2m,
                        true)
                },
                new[] { report },
                new[]
                {
                    new GovernmentRiskSignal(
                        new StableId("probe.risk.tax-arrears"),
                        GovernmentRiskKind.FiscalShortfall,
                        CountyScopeId,
                        Math.Min(1m, report.Fiscal.RevenueReceivable / Math.Max(report.Fiscal.AssessedRevenue, 1m)),
                        report.Confidence,
                        report.ReportId)
                });
            var plan = decisionService.PlanNextYear(new StableId("probe.plan.realm.1629"), packet);

            return new LayeredSettlementYearAccountingSummary(
                new ReadOnlyCollection<AnnualClosingLedger>(annualLedgers),
                report,
                packet,
                plan);
        }

        private static Dictionary<SimulationResolution, List<SettlementLedger>> CreateMonthlyBuckets()
        {
            var result = new Dictionary<SimulationResolution, List<SettlementLedger>>();
            foreach (SimulationResolution resolution in Enum.GetValues(typeof(SimulationResolution)))
            {
                result.Add(resolution, new List<SettlementLedger>());
            }

            return result;
        }

        private static SimulationDriverRecord[] CreateDroughtDrivers()
        {
            return new[]
            {
                CreateDroughtDriver("village", VillageScopeId),
                CreateDroughtDriver("county", CountyScopeId),
                CreateDroughtDriver("si", SiScopeId)
            };
        }

        private static SimulationDriverRecord CreateDroughtDriver(string suffix, StableId scopeId)
        {
            return new SimulationDriverRecord(
                new StableId("probe.driver.drought." + suffix),
                scopeId,
                SimulationDriverOrigin.ExternalCondition,
                SimulationDriverKind.Weather,
                LedgerPeriod.Monthly(EconomicYear, 1),
                LedgerPeriod.Monthly(EconomicYear, 1),
                LedgerPeriod.Monthly(EconomicYear, 12),
                1m,
                new[]
                {
                    new SimulationDriverEffect(
                        new StableId("probe.effect.drought." + suffix),
                        SimulationEffectKind.ProductionMultiplier,
                        0.85m,
                        targetSector: EconomicSectorKind.Agriculture)
                });
        }

        private static SettlementLedger CreateZeroResidual(
            SettlementLedgerService service,
            string name,
            StableId jurisdictionId,
            StableId parentJurisdictionId,
            SimulationResolution resolution,
            LedgerPeriod period,
            int month)
        {
            return service.CloseMonth(
                CreateHeader(
                    LedgerId(name, month),
                    jurisdictionId,
                    parentJurisdictionId,
                    resolution,
                    period,
                    "model." + name + ".v1",
                    SettlementLedgerDataKind.EstimatedAggregate),
                new[] { CreateGrainFlow(0m, 0m, 0m, 0m, 0m, 0m, 0m) });
        }

        private static IEnumerable<ConsolidationAdjustment> CreateCountyAdjustments(
            LayeredSettlementYearScenario scenario,
            int month,
            StableId villageLedgerId,
            StableId countyResidualLedgerId,
            decimal internalGrainKg)
        {
            if (internalGrainKg <= LedgerContractGuard.Tolerance)
            {
                return null;
            }

            return new[]
            {
                new ConsolidationAdjustment(
                    new StableId("probe.adjust.grain." + month),
                    ConsolidationAdjustmentKind.ResourceInternalTransfer,
                    GrainMetricId,
                    "kg",
                    internalGrainKg,
                    villageLedgerId,
                    countyResidualLedgerId,
                    "Village and county internal grain transfers"),
                new ConsolidationAdjustment(
                    new StableId("probe.adjust.trade." + month),
                    ConsolidationAdjustmentKind.EconomicInternalTrade,
                    new StableId("economic.internal-grain-trade"),
                    SilverEquivalentUnit,
                    internalGrainKg / scenario.GrainKgPerSilver,
                    villageLedgerId,
                    countyResidualLedgerId,
                    "Eliminate village-county internal trade value")
            };
        }

        private static CapacityAndStockStatement CreateScopeSummary(
            decimal adultEquivalentPopulation,
            decimal grainKg,
            decimal debtSilver,
            decimal taxArrearsSilver,
            decimal annualFoodKgPerAdultEquivalent)
        {
            var annualFood = adultEquivalentPopulation * annualFoodKgPerAdultEquivalent;
            var coverageMonths = annualFood <= 0m ? 0m : grainKg / annualFood * 12m;
            return new CapacityAndStockStatement(
                new[]
                {
                    new LedgerMetric(
                        new StableId("population.adult-equivalent"),
                        LedgerMetricDomain.PopulationAndLabor,
                        "adult-equivalent",
                        adultEquivalentPopulation,
                        MetricAggregationMode.Sum),
                    new LedgerMetric(
                        new StableId("credit.debt.silver"),
                        LedgerMetricDomain.MoneyAndCredit,
                        "silver",
                        debtSilver,
                        MetricAggregationMode.Sum),
                    new LedgerMetric(
                        new StableId("fiscal.tax-arrears.silver"),
                        LedgerMetricDomain.GovernmentFinance,
                        "silver",
                        taxArrearsSilver,
                        MetricAggregationMode.Sum)
                },
                new[]
                {
                    new LedgerMetric(
                        new StableId("food.coverage.months"),
                        LedgerMetricDomain.ProductionAndNeeds,
                        "month",
                        coverageMonths,
                        MetricAggregationMode.WeightedAverage,
                        Math.Max(adultEquivalentPopulation, 1m))
                });
        }

        private static EconomicOutputStatement CreateAgricultureEconomy(
            LayeredSettlementYearScenario scenario,
            decimal productionKg,
            decimal reproductionUseKg,
            decimal householdConsumptionKg,
            decimal importsKg,
            decimal exportsKg,
            decimal salesKg,
            decimal openingGrainKg,
            decimal closingGrainKg)
        {
            var grossOutput = productionKg / scenario.GrainKgPerSilver;
            var intermediate = reproductionUseKg / scenario.GrainKgPerSilver;
            var valueAdded = grossOutput - intermediate;
            var sector = new EconomicSectorStatement(
                EconomicSectorKind.Agriculture,
                SilverEquivalentUnit,
                EconomicYear,
                grossOutput,
                intermediate,
                grossOutput,
                intermediate,
                0m,
                0m,
                0m,
                valueAdded,
                0m,
                productionKg > 0m ? 1m : 0m,
                salesKg / scenario.GrainKgPerSilver,
                (closingGrainKg - openingGrainKg) / scenario.GrainKgPerSilver);
            return new EconomicAccountingService().BuildOutputStatement(
                new[] { sector },
                SilverEquivalentUnit,
                EconomicYear,
                householdConsumptionKg / scenario.GrainKgPerSilver,
                0m,
                0m,
                (closingGrainKg - openingGrainKg) / scenario.GrainKgPerSilver,
                exportsKg / scenario.GrainKgPerSilver,
                importsKg / scenario.GrainKgPerSilver);
        }

        private static SettlementLedgerHeader CreateHeader(
            StableId ledgerId,
            StableId jurisdictionId,
            StableId? parentJurisdictionId,
            SimulationResolution resolution,
            LedgerPeriod period,
            string modelId,
            SettlementLedgerDataKind dataKind = SettlementLedgerDataKind.AuthoritativeTruth)
        {
            return new SettlementLedgerHeader(
                ledgerId,
                jurisdictionId,
                parentJurisdictionId,
                period,
                resolution,
                new StableId(modelId),
                RuleVersion,
                new StableId("owner." + ledgerId.Value),
                dataKind: dataKind,
                inputHash: "probe-input." + ledgerId.Value,
                outputHash: "probe-output." + ledgerId.Value);
        }

        private static SettlementLedgerHeader CreateConsolidatedHeader(
            StableId ledgerId,
            StableId jurisdictionId,
            StableId? parentJurisdictionId,
            SimulationResolution resolution,
            LedgerPeriod period,
            string modelId,
            IEnumerable<StableId> childLedgerIds,
            StableId residualLedgerId,
            SettlementLedgerDataKind dataKind)
        {
            return new SettlementLedgerHeader(
                ledgerId,
                jurisdictionId,
                parentJurisdictionId,
                period,
                resolution,
                new StableId(modelId),
                RuleVersion,
                new StableId("owner." + ledgerId.Value),
                childLedgerIds,
                residualLedgerId,
                dataKind,
                inputHash: "probe-input." + ledgerId.Value,
                outputHash: "probe-output." + ledgerId.Value);
        }

        private static LedgerFlowLine CreateGrainFlow(
            decimal opening,
            decimal internalInflow,
            decimal produced,
            decimal internalOutflow,
            decimal consumed,
            decimal lost,
            decimal closing)
        {
            return new LedgerFlowLine(
                GrainMetricId,
                "kg",
                opening,
                0m,
                internalInflow,
                produced,
                0m,
                internalOutflow,
                consumed,
                lost,
                closing);
        }

        private static decimal CalculateConsumption(
            decimal available,
            decimal adultEquivalentPopulation,
            decimal annualFoodKgPerAdultEquivalent,
            int month)
        {
            var annualDemand = adultEquivalentPopulation * annualFoodKgPerAdultEquivalent;
            var baseUnits = decimal.Floor(annualDemand / 12m);
            var remainder = (int)(annualDemand - baseUnits * 12m);
            var demand = baseUnits + (month <= remainder ? 1m : 0m);
            return Math.Min(Math.Max(available, 0m), demand);
        }

        private static decimal ClampZero(decimal value)
        {
            return Math.Abs(value) <= LedgerContractGuard.Tolerance ? 0m : value;
        }

        private static StableId LedgerId(string name, int month)
        {
            return new StableId(
                string.Format(CultureInfo.InvariantCulture, "probe.ledger.{0}.{1:D2}", name, month));
        }

        private static AnnualClosingLedger GetRequiredLedger(
            IList<AnnualClosingLedger> ledgers,
            SimulationResolution resolution)
        {
            for (var index = 0; index < ledgers.Count; index++)
            {
                if (ledgers[index].ClosingResolution == resolution)
                {
                    return ledgers[index];
                }
            }

            throw new InvalidOperationException("The probe did not produce every required annual ledger.");
        }
    }
}
