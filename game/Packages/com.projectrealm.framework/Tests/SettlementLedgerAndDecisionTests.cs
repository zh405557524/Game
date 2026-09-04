using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectRealm.World;

namespace ProjectRealm.Tests.Unit
{
    public sealed class SettlementLedgerAndDecisionTests
    {
        private static readonly StableId GrainMetricId = new StableId("commodity.grain.kg");
        private static readonly StableId FiscalCashMetricId = new StableId("fiscal.cash.silver");
        private readonly SettlementLedgerService _ledgerService = new SettlementLedgerService();

        [Test]
        public void EveryResolutionClosesMonthlyAndAnnualLedgersIncludingZeroActivity()
        {
            foreach (SimulationResolution resolution in Enum.GetValues(typeof(SimulationResolution)))
            {
                var jurisdictionId = new StableId("scope." + resolution.ToString().ToLowerInvariant());
                var monthly = new List<SettlementLedger>();
                for (var month = 1; month <= 12; month++)
                {
                    monthly.Add(_ledgerService.CloseMonth(
                        CreateHeader(
                            new StableId("ledger." + resolution.ToString().ToLowerInvariant() + "." + month),
                            jurisdictionId,
                            resolution,
                            month),
                        new[] { CreateFlow(GrainMetricId, "kg", 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m) }));
                }

                var annual = _ledgerService.CloseYear(
                    new StableId("annual." + resolution.ToString().ToLowerInvariant()),
                    1628,
                    monthly);

                Assert.That(annual.MonthlyLedgerIds, Has.Count.EqualTo(12));
                Assert.That(annual.ClosingResolution, Is.EqualTo(resolution));
                Assert.That(annual.CloseResult.AllInvariantsPassed, Is.True);
                Assert.That(annual.StateFingerprint, Is.Not.Empty);
            }
        }

        [Test]
        public void AnnualCloseSumsFlowsButUsesFirstOpeningAndLastClosingStock()
        {
            var monthly = new List<SettlementLedger>();
            var grain = 100m;
            var treasury = 10m;
            for (var month = 1; month <= 12; month++)
            {
                var grainOpening = grain;
                grain += 5m;
                var treasuryOpening = treasury;
                treasury += 1m;
                monthly.Add(_ledgerService.CloseMonth(
                    CreateHeader(new StableId("ledger.county." + month), new StableId("county.alpha"), SimulationResolution.CountyFull, month),
                    new[] { CreateFlow(GrainMetricId, "kg", grainOpening, 0m, 0m, 10m, 0m, 0m, 5m, 0m, grain) },
                    new FiscalStatement(
                        treasuryOpening,
                        2m,
                        2m,
                        0m,
                        0m,
                        1m,
                        0m,
                        0m,
                        0m,
                        treasury,
                        0m,
                        0m,
                        0m,
                        0m)));
            }

            var annual = _ledgerService.CloseYear(new StableId("annual.county.alpha"), 1628, monthly);
            var grainLine = annual.GetRequiredFlowLine(GrainMetricId);

            Assert.That(grainLine.Opening, Is.EqualTo(100m));
            Assert.That(grainLine.Produced, Is.EqualTo(120m));
            Assert.That(grainLine.Consumed, Is.EqualTo(60m));
            Assert.That(grainLine.Closing, Is.EqualTo(160m));
            Assert.That(annual.Fiscal.OpeningTreasury, Is.EqualTo(10m));
            Assert.That(annual.Fiscal.CollectedRevenue, Is.EqualTo(24m));
            Assert.That(annual.Fiscal.MandatoryExpensesPaid, Is.EqualTo(12m));
            Assert.That(annual.Fiscal.ClosingTreasury, Is.EqualTo(22m));
        }

        [Test]
        public void ParentConsolidationEliminatesInternalResourceFiscalAndTradeFlows()
        {
            var period = LedgerPeriod.Monthly(1628, 1);
            var childA = _ledgerService.CloseMonth(
                CreateHeader(new StableId("ledger.child.a"), new StableId("scope.a"), SimulationResolution.VillageDetailed, 1),
                new[] { CreateFlow(GrainMetricId, "kg", 100m, 0m, 0m, 0m, 0m, 10m, 0m, 0m, 90m) },
                new FiscalStatement(10m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 5m, 5m, 0m, 0m, 0m, 0m),
                economicOutput: CreateTradeEconomy(2m, 0m));
            var childB = _ledgerService.CloseMonth(
                CreateHeader(new StableId("ledger.child.b"), new StableId("scope.b"), SimulationResolution.TownshipNode, 1),
                new[] { CreateFlow(GrainMetricId, "kg", 20m, 0m, 10m, 0m, 0m, 0m, 0m, 0m, 30m) },
                new FiscalStatement(0m, 0m, 0m, 5m, 0m, 0m, 0m, 0m, 0m, 5m, 0m, 0m, 0m, 0m),
                economicOutput: CreateTradeEconomy(0m, 2m));
            var residual = _ledgerService.CloseMonth(
                CreateHeader(new StableId("ledger.parent.residual"), new StableId("scope.parent"), SimulationResolution.CountyFull, 1),
                new[] { CreateFlow(GrainMetricId, "kg", 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m) });
            var parentHeader = new SettlementLedgerHeader(
                new StableId("ledger.parent"),
                new StableId("scope.parent"),
                null,
                period,
                SimulationResolution.CountyFull,
                new StableId("model.county.full.v1"),
                "rules.v1",
                new StableId("owner.parent"),
                new[] { childA.Header.LedgerId, childB.Header.LedgerId },
                residual.Header.LedgerId,
                outputHash: "parent-output");

            var parent = _ledgerService.ConsolidateChildLedgers(
                parentHeader,
                new[] { childA, childB },
                residual,
                new[]
                {
                    new ConsolidationAdjustment(
                        new StableId("adjust.grain"),
                        ConsolidationAdjustmentKind.ResourceInternalTransfer,
                        GrainMetricId,
                        "kg",
                        10m,
                        childA.Header.LedgerId,
                        childB.Header.LedgerId,
                        "Internal grain delivery"),
                    new ConsolidationAdjustment(
                        new StableId("adjust.cash"),
                        ConsolidationAdjustmentKind.FiscalInternalTransfer,
                        FiscalCashMetricId,
                        "silver",
                        5m,
                        childA.Header.LedgerId,
                        childB.Header.LedgerId,
                        "Internal fiscal transfer"),
                    new ConsolidationAdjustment(
                        new StableId("adjust.trade"),
                        ConsolidationAdjustmentKind.EconomicInternalTrade,
                        new StableId("economic.trade.value"),
                        "silver-equivalent",
                        2m,
                        childA.Header.LedgerId,
                        childB.Header.LedgerId,
                        "Internal trade value")
                });

            var grain = parent.GetRequiredFlowLine(GrainMetricId);
            Assert.That(grain.Opening, Is.EqualTo(120m));
            Assert.That(grain.InternalInflow, Is.Zero);
            Assert.That(grain.InternalOutflow, Is.Zero);
            Assert.That(grain.Closing, Is.EqualTo(120m));
            Assert.That(parent.Fiscal.TransfersReceived, Is.Zero);
            Assert.That(parent.Fiscal.TransfersSent, Is.Zero);
            Assert.That(parent.Fiscal.ClosingTreasury, Is.EqualTo(10m));
            Assert.That(parent.EconomicOutput.ExternalExports, Is.Zero);
            Assert.That(parent.EconomicOutput.ExternalImports, Is.Zero);
        }

        [Test]
        public void MilitaryMaterielUsesAuthoritativeFlowsForWeaponsAmmunitionShipsWagonsAndHorses()
        {
            var weapon = CreateFlow(new StableId("materiel.spear"), "piece", 10m, 0m, 0m, 2m, 0m, 0m, 0m, 1m, 11m);
            var ammunition = CreateFlow(new StableId("materiel.arrow"), "piece", 100m, 0m, 0m, 20m, 0m, 0m, 30m, 0m, 90m);
            var ship = CreateFlow(new StableId("materiel.war-vessel"), "vessel", 5m, 0m, 0m, 0m, 0m, 0m, 0m, 1m, 4m);
            var wagon = CreateFlow(new StableId("materiel.wagon"), "vehicle", 8m, 0m, 0m, 1m, 0m, 0m, 0m, 0m, 9m);
            var horse = CreateFlow(new StableId("materiel.war-horse"), "animal", 20m, 2m, 0m, 0m, 0m, 0m, 0m, 1m, 21m);
            var military = new MilitaryMaterielStatement(
                new[]
                {
                    new MilitaryMaterielLine(MilitaryMaterielKind.MeleeWeapon, weapon, 9m, 2m, 1m),
                    new MilitaryMaterielLine(MilitaryMaterielKind.Ammunition, ammunition, 90m, 0m, 20m),
                    new MilitaryMaterielLine(MilitaryMaterielKind.WarVessel, ship, 3m, 1m, 1m),
                    new MilitaryMaterielLine(MilitaryMaterielKind.Wagon, wagon, 8m, 1m, 2m),
                    new MilitaryMaterielLine(MilitaryMaterielKind.WarHorse, horse, 18m, 3m, 4m)
                },
                120m,
                105m,
                3_000m,
                1_500m,
                2_000m,
                8_000m,
                20_000m);

            var ledger = _ledgerService.CloseMonth(
                CreateHeader(new StableId("ledger.military"), new StableId("scope.garrison"), SimulationResolution.CountyFull, 1),
                new[] { weapon, ammunition, ship, wagon, horse },
                militaryMateriel: military);

            Assert.That(ledger.MilitaryMateriel.Materiel, Has.Count.EqualTo(5));
            Assert.That(ledger.MilitaryMateriel.FitForDutyRate, Is.EqualTo(0.875m));
            Assert.That(
                ledger.MilitaryMateriel.GetRequiredMateriel(new StableId("materiel.arrow")).Flow.Consumed,
                Is.EqualTo(30m));
        }

        [Test]
        public void ExternalDroughtChangesPhysicalOutputBeforeEconomicValueAddedIsCalculated()
        {
            var scopeId = new StableId("scope.farm-village");
            var activity = new SectorProductionActivity(
                new StableId("activity.rice-field"),
                new StableId("household.farmer"),
                scopeId,
                EconomicSectorKind.Agriculture,
                new[] { new ValuedCommodityQuantity(GrainMetricId, "kg", 100m, 0.02m, 0.02m) },
                new[] { new ValuedCommodityQuantity(new StableId("commodity.seed.kg"), "kg", 20m, 0.02m, 0.02m) },
                0.5m,
                0.2m,
                0.1m,
                12m,
                1m,
                1.2m,
                0.2m);
            var drought = new SimulationDriverRecord(
                new StableId("driver.drought"),
                scopeId,
                SimulationDriverOrigin.ExternalCondition,
                SimulationDriverKind.Weather,
                LedgerPeriod.Monthly(1628, 1),
                LedgerPeriod.Monthly(1628, 1),
                LedgerPeriod.Monthly(1628, 6),
                1m,
                new[]
                {
                    new SimulationDriverEffect(
                        new StableId("effect.drought.production"),
                        SimulationEffectKind.ProductionMultiplier,
                        0.7m,
                        targetSector: EconomicSectorKind.Agriculture),
                    new SimulationDriverEffect(
                        new StableId("effect.drought.loss"),
                        SimulationEffectKind.LossRateAdditive,
                        0.1m,
                        targetMetricId: GrainMetricId)
                });

            var settled = new ProductionSettlementService().Settle(
                activity,
                LedgerPeriod.Monthly(1628, 3),
                new[] { drought });
            var sector = new EconomicAccountingService().BuildSectorStatement(
                EconomicSectorKind.Agriculture,
                "silver-equivalent",
                1628,
                new[] { settled });

            Assert.That(settled.Outputs[0].GrossQuantity, Is.EqualTo(70m));
            Assert.That(settled.Outputs[0].LostQuantity, Is.EqualTo(7m));
            Assert.That(settled.Outputs[0].UsableQuantity, Is.EqualTo(63m));
            Assert.That(sector.NominalGrossOutput, Is.EqualTo(1.26m));
            Assert.That(sector.NominalIntermediateConsumption, Is.EqualTo(0.4m));
            Assert.That(sector.NominalValueAdded, Is.EqualTo(0.86m));
            Assert.That(settled.AppliedDriverIds, Does.Contain(drought.DriverId));
        }

        [Test]
        public void GovernmentOrActorDecisionCannotRecursivelyChangeTheSameMonth()
        {
            Assert.Throws<ArgumentException>(() => new SimulationDriverRecord(
                new StableId("driver.same-month-edict"),
                new StableId("scope.county"),
                SimulationDriverOrigin.GovernmentPolicy,
                SimulationDriverKind.EdictExecution,
                LedgerPeriod.Monthly(1628, 1),
                LedgerPeriod.Monthly(1628, 1),
                null,
                1m,
                new[]
                {
                    new SimulationDriverEffect(
                        new StableId("effect.same-month"),
                        SimulationEffectKind.ProductionMultiplier,
                        1.1m,
                        targetSector: EconomicSectorKind.Agriculture)
                }));
        }

        [Test]
        public void GovernmentReportSeparatesNominalTaxFromActualCashUsedByNextYearPlan()
        {
            var countyAnnual = CreateFiscalAnnualLedger();
            var authorityId = new StableId("authority.realm");
            var report = new GovernmentReportService().BuildGovernmentReport(
                countyAnnual,
                new StableId("report.county.1628"),
                authorityId,
                LedgerPeriod.Monthly(1629, 1),
                new GovernmentReportingPolicy(1, 1m, 1m, 1m));
            var action = new GovernmentActionCandidate(
                new StableId("action.new-project"),
                new StableId("scope.realm"),
                GovernmentActionKind.Construction,
                GovernmentBudgetCategory.Infrastructure,
                5m,
                1m,
                10m,
                0.8m,
                0.1m,
                true);
            var decisionService = new GovernmentDecisionService();
            var packet = decisionService.BuildDecisionPacket(
                new StableId("packet.realm.1629"),
                authorityId,
                new StableId("scope.realm"),
                1629,
                10.66m,
                10.66m,
                report.Fiscal.AssessedRevenue,
                report.Fiscal.RevenueInTransit,
                report.Fiscal.RevenueReceivable,
                0m,
                2m,
                10m,
                1,
                new[]
                {
                    new BudgetDemand(
                        new StableId("demand.administration"),
                        GovernmentBudgetCategory.Administration,
                        8m,
                        0,
                        true,
                        "Maintain minimum administration")
                },
                new[] { action },
                new[] { report },
                null);
            var plan = decisionService.PlanNextYear(new StableId("plan.realm.1629"), packet);

            Assert.That(report.Fiscal.AssessedRevenue, Is.EqualTo(38.376m).Within(0.000001m));
            Assert.That(report.Fiscal.CollectedRevenue, Is.EqualTo(18.60m).Within(0.000001m));
            Assert.That(report.Fiscal.RevenueReceivable, Is.EqualTo(19.776m).Within(0.000001m));
            Assert.That(report.Fiscal.TransfersReportedSent, Is.EqualTo(10.66m).Within(0.000001m));
            Assert.That(packet.ActualTreasuryCash, Is.EqualTo(10.66m));
            Assert.That(packet.NominalReportedRevenue, Is.GreaterThan(packet.ActualTreasuryCash));
            Assert.That(plan.MandatoryAllocations[0].FundedCash, Is.EqualTo(8m));
            Assert.That(plan.FundedActions, Is.Empty);
            Assert.That(plan.DeferredActions, Does.Contain(action));
            Assert.That(plan.UnallocatedCash, Is.EqualTo(0.66m).Within(0.000001m));
            Assert.That(typeof(GovernmentReport).GetProperty("SourceLedger"), Is.Null);
        }

        [Test]
        public void MonthlyNoiseKeepsAnnualPlanButMajorCrisisCreatesTraceableEmergencyReplan()
        {
            var decisionService = new GovernmentDecisionService();
            var authorityId = new StableId("authority.county");
            var edict = new GovernmentActionCandidate(
                new StableId("action.irrigation-edict"),
                new StableId("scope.county"),
                GovernmentActionKind.Edict,
                GovernmentBudgetCategory.EconomicDevelopment,
                20m,
                1m,
                20m,
                0.8m,
                0.1m,
                true);
            var packet = decisionService.BuildDecisionPacket(
                new StableId("packet.county"),
                authorityId,
                new StableId("scope.county"),
                1629,
                100m,
                20m,
                20m,
                0m,
                0m,
                10_000m,
                10m,
                10m,
                2,
                new[]
                {
                    new BudgetDemand(
                        new StableId("demand.base"),
                        GovernmentBudgetCategory.Administration,
                        20m,
                        0,
                        true,
                        "Base administration")
                },
                new[] { edict },
                null,
                null);
            var plan = decisionService.PlanNextYear(new StableId("plan.county"), packet);
            var policy = new MonthlyAdjustmentPolicy(0.15m, 0.15m, 0.20m);
            var ordinary = decisionService.AdjustMonthlyPlan(
                new StableId("adjust.ordinary"),
                plan,
                new MonthlyVarianceReport(
                    1629,
                    2,
                    -0.05m,
                    0.04m,
                    -0.05m,
                    70m,
                    20m,
                    4m,
                    2m,
                    4m,
                    2m,
                    MonthlyCrisisKind.None,
                    0m,
                    "Small monthly noise"),
                policy);
            var emergency = decisionService.AdjustMonthlyPlan(
                new StableId("adjust.crisis"),
                plan,
                new MonthlyVarianceReport(
                    1629,
                    5,
                    -0.40m,
                    0.30m,
                    -0.50m,
                    5m,
                    20m,
                    0.5m,
                    2m,
                    0.5m,
                    2m,
                    MonthlyCrisisKind.MajorDisaster,
                    80m,
                    "Flood destroyed granaries and roads"),
                policy);

            Assert.That(ordinary.Kind, Is.EqualTo(MonthlyAdjustmentKind.None));
            Assert.That(emergency.Kind, Is.EqualTo(MonthlyAdjustmentKind.EmergencyReplan));
            Assert.That(emergency.PausedActionIds, Does.Contain(edict.ActionId));
            Assert.That(emergency.Reason, Does.Contain("Flood"));
        }

        [Test]
        public void FundedEdictSchedulesEffectsForTheNextCompleteSettlementPeriod()
        {
            var service = new GovernmentDecisionService();
            var edict = new GovernmentActionCandidate(
                new StableId("action.school-edict"),
                new StableId("scope.county"),
                GovernmentActionKind.Edict,
                GovernmentBudgetCategory.Education,
                5m,
                1m,
                10m,
                1m,
                0m,
                true);
            var packet = service.BuildDecisionPacket(
                new StableId("packet.school"),
                new StableId("authority.magistrate"),
                new StableId("scope.county"),
                1629,
                20m,
                20m,
                20m,
                0m,
                0m,
                0m,
                2m,
                2m,
                1,
                null,
                new[] { edict },
                null,
                null);
            var plan = service.PlanNextYear(new StableId("plan.school"), packet);
            var driver = service.ScheduleFundedEdictEffect(
                plan,
                edict.ActionId,
                new StableId("driver.school-edict"),
                new StableId("edict.school.instance"),
                LedgerPeriod.Monthly(1629, 1),
                LedgerPeriod.Monthly(1629, 2),
                LedgerPeriod.Monthly(1629, 12),
                0.6m,
                new[]
                {
                    new SimulationDriverEffect(
                        new StableId("effect.school.capacity"),
                        SimulationEffectKind.EducationCapacityMultiplier,
                        1.2m,
                        targetSector: EconomicSectorKind.PublicServices)
                });

            Assert.That(driver.Origin, Is.EqualTo(SimulationDriverOrigin.GovernmentPolicy));
            Assert.That(driver.SourceDecisionId, Is.EqualTo(edict.ActionId));
            Assert.That(driver.IsActive(edict.TargetScopeId, LedgerPeriod.Monthly(1629, 1)), Is.False);
            Assert.That(driver.IsActive(edict.TargetScopeId, LedgerPeriod.Monthly(1629, 2)), Is.True);
        }

        [Test]
        public void ActorInfluenceIsDerivedFromRealChannelsInsteadOfAWorldPowerBonus()
        {
            var impact = new ActorImpactRecord(
                new StableId("impact.merchant-road"),
                new StableId("actor.merchant"),
                new StableId("action.caravan"),
                new StableId("scope.county"),
                ActorInfluenceChannel.Market,
                new StableId("metric.trade-capacity"),
                "kg-per-month",
                1_000m,
                100m,
                25m,
                0.5m,
                0.4m,
                0.5m,
                0.8m,
                0.5m,
                causalChainIds: new[] { new StableId("shipment.caravan") });

            Assert.That(impact.EffectiveInfluence, Is.EqualTo(0.04m));
            Assert.That(impact.DirectDelta, Is.EqualTo(100m));
            Assert.That(impact.PropagatedDelta, Is.EqualTo(25m));
        }

        private AnnualClosingLedger CreateFiscalAnnualLedger()
        {
            var months = new List<SettlementLedger>();
            var treasury = 0m;
            for (var month = 1; month <= 12; month++)
            {
                var opening = treasury;
                var assessed = month == 11 ? 38.376m : 0m;
                var collected = month == 11 ? 18.60m : 0m;
                var remitted = month == 11 ? 10.66m : 0m;
                treasury += collected - remitted;
                months.Add(_ledgerService.CloseMonth(
                    CreateHeader(
                        new StableId("ledger.fiscal.county." + month),
                        new StableId("scope.fiscal-county"),
                        SimulationResolution.CountyFull,
                        month),
                    new[] { CreateFlow(GrainMetricId, "kg", 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m) },
                    new FiscalStatement(
                        opening,
                        assessed,
                        collected,
                        0m,
                        0m,
                        0m,
                        0m,
                        0m,
                        remitted,
                        treasury,
                        month >= 11 ? 19.776m : 0m,
                        0m,
                        0m,
                        0m)));
            }

            return _ledgerService.CloseYear(new StableId("annual.fiscal.county"), 1628, months);
        }

        private static EconomicOutputStatement CreateTradeEconomy(decimal exports, decimal imports)
        {
            return new EconomicAccountingService().BuildOutputStatement(
                null,
                "silver-equivalent",
                1628,
                0m,
                0m,
                0m,
                0m,
                exports,
                imports);
        }

        private static SettlementLedgerHeader CreateHeader(
            StableId ledgerId,
            StableId jurisdictionId,
            SimulationResolution resolution,
            int month)
        {
            return new SettlementLedgerHeader(
                ledgerId,
                jurisdictionId,
                null,
                LedgerPeriod.Monthly(1628, month),
                resolution,
                new StableId("model." + resolution.ToString().ToLowerInvariant() + ".v1"),
                "rules.v1",
                new StableId("owner." + jurisdictionId.Value),
                outputHash: "output." + ledgerId.Value);
        }

        private static LedgerFlowLine CreateFlow(
            StableId metricId,
            string unit,
            decimal opening,
            decimal externalInflow,
            decimal internalInflow,
            decimal produced,
            decimal externalOutflow,
            decimal internalOutflow,
            decimal consumed,
            decimal lost,
            decimal closing)
        {
            return new LedgerFlowLine(
                metricId,
                unit,
                opening,
                externalInflow,
                internalInflow,
                produced,
                externalOutflow,
                internalOutflow,
                consumed,
                lost,
                closing);
        }
    }
}
