using System;
using System.Globalization;
using NUnit.Framework;
using ProjectRealm.Domain;

namespace ProjectRealm.Tests.Unit
{
    public sealed class LayeredSettlementYearProbeTests
    {
        [Test]
        public void DroughtProbeRunsTwelveMonthsWithConservedLayeredLedgers()
        {
            var scenario = LayeredSettlementYearScenario.CreateDroughtSettlementProbe();
            var result = new LayeredSettlementYearProbe(scenario).Run();

            Assert.That(result.Months, Has.Count.EqualTo(12));
            Assert.That(result.AllInvariantsPassed, Is.True);
            Assert.That(result.TotalFoodShortfallKg, Is.EqualTo(0m));
            Assert.That(result.FinalVillageDebtSilver, Is.EqualTo(0m).Within(0.000001m));
            Assert.That(result.PeakVillageDebtSilver, Is.EqualTo(171.19m).Within(0.01m));
            Assert.That(result.FinalVillageGrainKg, Is.EqualTo(5_408.70m).Within(0.01m));
            Assert.That(result.TaxPaidSilver, Is.EqualTo(18.60m).Within(0.01m));
            Assert.That(result.TaxArrearsSilver, Is.EqualTo(19.78m).Within(0.01m));
            Assert.That(
                result.TaxPaidSilver + result.TaxArrearsSilver,
                Is.EqualTo(scenario.CollectibleTaxCallSilver).Within(0.000001m));
            Assert.That(result.TaxRemittedSilver, Is.EqualTo(10.66m).Within(0.01m));
            Assert.That(result.ClosingWorldGrainKg, Is.EqualTo(363_879.66m).Within(0.01m));
            Assert.That(result.ClosingWorldSilver, Is.EqualTo(result.OpeningWorldSilver).Within(0.000001m));
            Assert.That(
                result.ClosingWorldGrainKg,
                Is.EqualTo(result.FinalVillageGrainKg + result.FinalCountyGrainKg + result.FinalRegionalGrainKg)
                    .Within(0.000001m));

            foreach (var month in result.Months)
            {
                Assert.That(month.SettlementOwnerCount, Is.EqualTo(3));
                Assert.That(month.GrainConservationErrorKg, Is.EqualTo(0m).Within(0.000001m));
                Assert.That(month.SilverConservationError, Is.EqualTo(0m).Within(0.000001m));
                Assert.That(month.DebtCounterpartError, Is.EqualTo(0m).Within(0.000001m));
                Assert.That(month.TaxCounterpartError, Is.EqualTo(0m).Within(0.000001m));
            }

            WriteReport(result);
        }

        [Test]
        public void ProbeProducesTheSameFingerprintForTheSameInputs()
        {
            var scenario = LayeredSettlementYearScenario.CreateDroughtSettlementProbe();

            var first = new LayeredSettlementYearProbe(scenario).Run();
            var second = new LayeredSettlementYearProbe(scenario).Run();

            Assert.That(second.StateFingerprint, Is.EqualTo(first.StateFingerprint));
            for (var index = 0; index < first.AccountingSummary.AnnualLedgers.Count; index++)
            {
                Assert.That(
                    second.AccountingSummary.AnnualLedgers[index].StateFingerprint,
                    Is.EqualTo(first.AccountingSummary.AnnualLedgers[index].StateFingerprint));
            }
        }

        [Test]
        public void DroughtProbeClosesSevenAnnualLedgersAndUsesActualRemittanceForPlanning()
        {
            var result = new LayeredSettlementYearProbe(
                LayeredSettlementYearScenario.CreateDroughtSettlementProbe()).Run();
            var accounting = result.AccountingSummary;

            Assert.That(accounting.AnnualLedgers, Has.Count.EqualTo(7));
            foreach (SimulationResolution resolution in Enum.GetValues(typeof(SimulationResolution)))
            {
                var ledger = accounting.GetRequiredLedger(resolution);
                Assert.That(ledger.MonthlyLedgerIds, Has.Count.EqualTo(12));
                Assert.That(ledger.CloseResult.AllInvariantsPassed, Is.True);
            }

            var county = accounting.GetRequiredLedger(SimulationResolution.CountyFull);
            var si = accounting.GetRequiredLedger(SimulationResolution.SiStrategic);
            var realm = accounting.GetRequiredLedger(SimulationResolution.RealmCentral);

            Assert.That(county.Fiscal.AssessedRevenue, Is.EqualTo(38.376m).Within(0.000001m));
            Assert.That(county.Fiscal.CollectedRevenue, Is.EqualTo(18.60m).Within(0.01m));
            Assert.That(county.Fiscal.RevenueReceivableClosing, Is.EqualTo(19.78m).Within(0.01m));
            Assert.That(county.Fiscal.TransfersSent, Is.EqualTo(10.66m).Within(0.01m));
            Assert.That(realm.Fiscal.TransfersReceived, Is.EqualTo(10.66m).Within(0.01m));
            Assert.That(
                si.GetRequiredFlowLine(new StableId("commodity.grain.kg")).Closing,
                Is.EqualTo(result.ClosingWorldGrainKg).Within(0.000001m));
            Assert.That(si.EconomicOutput.NominalGrossOutput, Is.EqualTo(7_880m).Within(0.000001m));
            Assert.That(si.EconomicOutput.NominalIntermediateConsumption, Is.EqualTo(1_500m).Within(0.000001m));
            Assert.That(si.EconomicOutput.NominalValueAdded, Is.EqualTo(6_380m).Within(0.000001m));

            Assert.That(accounting.GovernmentReport.Fiscal.AssessedRevenue, Is.EqualTo(38.376m).Within(0.000001m));
            Assert.That(accounting.NextYearDecisionPacket.ConfirmedRevenueReceived, Is.EqualTo(10.66m).Within(0.01m));
            Assert.That(
                accounting.NextYearDecisionPacket.ActualTreasuryCash -
                accounting.NextYearDecisionPacket.MinimumCashReserve,
                Is.EqualTo(10.66m).Within(0.01m));
            Assert.That(accounting.NextYearGovernmentPlan.FundedActions, Is.Empty);
            Assert.That(accounting.NextYearGovernmentPlan.DeferredActions, Has.Count.EqualTo(1));
            Assert.That(accounting.NextYearGovernmentPlan.UnallocatedCash, Is.EqualTo(0.66m).Within(0.01m));
        }

        [Test]
        public void SettlementOwnershipRejectsDoubleClaims()
        {
            var registry = new SettlementOwnershipRegistry();
            var entityId = new StableId("PROBE-ENTITY");
            var villageLedgerId = new StableId("PROBE-VILLAGE-LEDGER");
            var countyLedgerId = new StableId("PROBE-COUNTY-LEDGER");

            registry.Claim(entityId, villageLedgerId);

            var exception = Assert.Throws<InvalidOperationException>(
                () => registry.Claim(entityId, countyLedgerId));
            Assert.That(exception.Message, Does.Contain(villageLedgerId.Value));
            Assert.That(exception.Message, Does.Contain(countyLedgerId.Value));
        }

        private static void WriteReport(LayeredSettlementYearResult result)
        {
            TestContext.Out.WriteLine($"ONE_YEAR_PROBE scenario={result.Scenario.ScenarioId}");
            TestContext.Out.WriteLine(
                "month,village_production_kg,village_purchase_kg,village_sale_kg," +
                "village_grain_kg,village_debt_silver,tax_arrears_silver," +
                "county_grain_kg,regional_grain_kg,grain_error_kg,silver_error");

            foreach (var month in result.Months)
            {
                TestContext.Out.WriteLine(string.Join(
                    ",",
                    month.Month.ToString(CultureInfo.InvariantCulture),
                    Format(month.VillageProductionKg),
                    Format(month.VillagePurchasedKg),
                    Format(month.VillageSoldKg),
                    Format(month.VillageGrainKg),
                    Format(month.VillageDebtSilver),
                    Format(month.VillageTaxArrearsSilver),
                    Format(month.CountyGrainKg),
                    Format(month.RegionalGrainKg),
                    Format(month.GrainConservationErrorKg),
                    Format(month.SilverConservationError)));
            }

            TestContext.Out.WriteLine(
                "ONE_YEAR_SUMMARY " +
                $"opening_grain_kg={Format(result.OpeningWorldGrainKg)} " +
                $"production_kg={Format(result.TotalProductionKg)} " +
                $"consumption_kg={Format(result.TotalConsumptionKg)} " +
                $"reproduction_kg={Format(result.TotalReproductionUseKg)} " +
                $"storage_loss_kg={Format(result.TotalStorageLossKg)} " +
                $"closing_grain_kg={Format(result.ClosingWorldGrainKg)} " +
                $"village_purchase_kg={Format(result.TotalVillagePurchasedKg)} " +
                $"village_sale_kg={Format(result.TotalVillageSoldKg)} " +
                $"village_rent_kg={Format(result.TotalVillageRentKg)} " +
                $"peak_debt_silver={Format(result.PeakVillageDebtSilver)} " +
                $"debt_interest_silver={Format(result.TotalDebtInterestSilver)} " +
                $"tax_paid_silver={Format(result.TaxPaidSilver)} " +
                $"tax_arrears_silver={Format(result.TaxArrearsSilver)} " +
                $"tax_remitted_silver={Format(result.TaxRemittedSilver)} " +
                $"food_shortfall_kg={Format(result.TotalFoodShortfallKg)} " +
                $"fingerprint={result.StateFingerprint}");

            TestContext.Out.WriteLine(
                "ANNUAL_LEDGER resolution,opening_grain_kg,produced_grain_kg," +
                "consumed_grain_kg,lost_grain_kg,closing_grain_kg," +
                "economic_gross_output,economic_intermediate_consumption,economic_value_added," +
                "tax_assessed_silver,tax_collected_silver,tax_sent_or_received_silver,treasury_closing_silver");
            foreach (var ledger in result.AccountingSummary.AnnualLedgers)
            {
                var hasGrain = ledger.FlowLines.Count > 0;
                var grain = hasGrain
                    ? ledger.GetRequiredFlowLine(new StableId("commodity.grain.kg"))
                    : null;
                TestContext.Out.WriteLine(string.Join(
                    ",",
                    ledger.ClosingResolution.ToString(),
                    Format(hasGrain ? grain.Opening : 0m),
                    Format(hasGrain ? grain.Produced : 0m),
                    Format(hasGrain ? grain.Consumed : 0m),
                    Format(hasGrain ? grain.LostOrDestroyed : 0m),
                    Format(hasGrain ? grain.Closing : 0m),
                    Format(ledger.EconomicOutput.NominalGrossOutput),
                    Format(ledger.EconomicOutput.NominalIntermediateConsumption),
                    Format(ledger.EconomicOutput.NominalValueAdded),
                    Format(ledger.Fiscal.AssessedRevenue),
                    Format(ledger.Fiscal.CollectedRevenue),
                    Format(ledger.Fiscal.TransfersSent + ledger.Fiscal.TransfersReceived),
                    Format(ledger.Fiscal.ClosingTreasury)));
            }

            var packet = result.AccountingSummary.NextYearDecisionPacket;
            var plan = result.AccountingSummary.NextYearGovernmentPlan;
            TestContext.Out.WriteLine(
                "NEXT_YEAR_GOVERNMENT_PLAN " +
                $"actual_treasury_silver={Format(packet.ActualTreasuryCash)} " +
                $"confirmed_revenue_silver={Format(packet.ConfirmedRevenueReceived)} " +
                $"nominal_reported_revenue_silver={Format(packet.NominalReportedRevenue)} " +
                $"reported_arrears_silver={Format(packet.ReportedRevenueArrears)} " +
                $"reserve_silver={Format(plan.ReserveSetAside)} " +
                $"financing_gap_silver={Format(plan.FinancingGap)} " +
                $"unallocated_silver={Format(plan.UnallocatedCash)} " +
                $"funded_actions={plan.FundedActions.Count} " +
                $"deferred_actions={plan.DeferredActions.Count}");
        }

        private static string Format(decimal value)
        {
            return decimal.Round(value, 2).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
