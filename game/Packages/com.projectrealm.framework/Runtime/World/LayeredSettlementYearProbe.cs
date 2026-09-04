using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ProjectRealm.World
{
    public sealed class SettlementOwnershipRegistry
    {
        private readonly Dictionary<StableId, StableId> _owners = new Dictionary<StableId, StableId>();

        public int Count => _owners.Count;

        public void Claim(StableId entityId, StableId ledgerId)
        {
            if (string.IsNullOrEmpty(entityId.Value))
            {
                throw new ArgumentException("A settlement claim requires an entity ID.", nameof(entityId));
            }

            if (string.IsNullOrEmpty(ledgerId.Value))
            {
                throw new ArgumentException("A settlement claim requires a ledger ID.", nameof(ledgerId));
            }

            if (_owners.TryGetValue(entityId, out var existingOwner))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityId}' is already settled by ledger '{existingOwner}' and cannot also be settled by '{ledgerId}'.");
            }

            _owners.Add(entityId, ledgerId);
        }

        public bool TryGetOwner(StableId entityId, out StableId ledgerId)
        {
            return _owners.TryGetValue(entityId, out ledgerId);
        }
    }

    /// <summary>
    /// A deliberately small, deterministic scenario used to exercise twelve monthly
    /// settlements before the production world simulation exists. Historical rule
    /// values are identified by their static rule IDs; opening stocks, production
    /// volumes, local price, household reserve and seasonal splits are probe inputs,
    /// not historical claims or final balance values.
    /// </summary>
    public sealed class LayeredSettlementYearScenario
    {
        private LayeredSettlementYearScenario()
        {
        }

        public string ScenarioId { get; private set; }

        public decimal VillageAdultEquivalentPopulation { get; private set; }

        public decimal CountyAdultEquivalentPopulation { get; private set; }

        public decimal RegionalAdultEquivalentPopulation { get; private set; }

        public decimal VillageOpeningGrainKg { get; private set; }

        public decimal CountyOpeningGrainKg { get; private set; }

        public decimal RegionalOpeningGrainKg { get; private set; }

        public decimal VillageOpeningSilver { get; private set; }

        public decimal CountyMarketOpeningSilver { get; private set; }

        public decimal CountyTreasuryOpeningSilver { get; private set; }

        public decimal RegionalTreasuryOpeningSilver { get; private set; }

        public decimal AnnualFoodKgPerAdultEquivalent { get; private set; }

        public decimal AnnualStorageLossRate { get; private set; }

        public decimal LandTaxRate { get; private set; }

        public decimal TaxCollectionEfficiency { get; private set; }

        public decimal CountyRemittanceShare { get; private set; }

        public decimal GrainKgPerSilver { get; private set; }

        public decimal MonthlyDebtInterestRate { get; private set; }

        public decimal HouseholdReserveMonths { get; private set; }

        public decimal VillageAnnualProductionKg => 44_000m;

        public decimal CountyAnnualProductionKg => 90_000m;

        public decimal RegionalAnnualProductionKg => 260_000m;

        public decimal VillageAnnualReproductionUseKg => 5_000m;

        public decimal CountyAnnualReproductionUseKg => 18_000m;

        public decimal RegionalAnnualReproductionUseKg => 52_000m;

        public decimal VillageAnnualRentKg => 8_000m;

        public decimal CollectibleTaxCallSilver =>
            (VillageAnnualProductionKg - VillageAnnualReproductionUseKg) *
            LandTaxRate *
            TaxCollectionEfficiency /
            GrainKgPerSilver;

        public static LayeredSettlementYearScenario CreateDroughtSettlementProbe()
        {
            return new LayeredSettlementYearScenario
            {
                ScenarioId = "drought_settlement_probe_v1",
                VillageAdultEquivalentPopulation = 150m,
                CountyAdultEquivalentPopulation = 300m,
                RegionalAdultEquivalentPopulation = 1_000m,
                VillageOpeningGrainKg = 8_000m,
                CountyOpeningGrainKg = 80_000m,
                RegionalOpeningGrainKg = 300_000m,
                VillageOpeningSilver = 20m,
                CountyMarketOpeningSilver = 5_000m,
                CountyTreasuryOpeningSilver = 10m,
                RegionalTreasuryOpeningSilver = 100m,

                // AG-CONSUME-002: normal adult-equivalent food baseline.
                AnnualFoodKgPerAdultEquivalent = 220m,

                // AG-LOSS-001: middle annual storage-loss calibration.
                AnnualStorageLossRate = 0.08m,

                // AF-FISCAL-002 and AF-FISCAL-006: middle land-tax and collection calibration.
                LandTaxRate = 0.06m,
                TaxCollectionEfficiency = 0.82m,

                // AF-FISCAL-005: national remittance calibration used only by this probe county.
                CountyRemittanceShare = 0.5731m,

                // Local scenario price and credit behavior; these are not national historical constants.
                GrainKgPerSilver = 50m,
                MonthlyDebtInterestRate = 0.01m,
                HouseholdReserveMonths = 3m
            };
        }

        public decimal GetVillageProductionKg(int month)
        {
            switch (month)
            {
                case 6:
                    return 15_400m;
                case 10:
                    return 28_600m;
                default:
                    return 0m;
            }
        }

        public decimal GetCountyProductionKg(int month)
        {
            switch (month)
            {
                case 6:
                    return 31_500m;
                case 10:
                    return 58_500m;
                default:
                    return 0m;
            }
        }

        public decimal GetRegionalProductionKg(int month)
        {
            switch (month)
            {
                case 6:
                    return 91_000m;
                case 10:
                    return 169_000m;
                default:
                    return 0m;
            }
        }

        public decimal GetVillageReproductionUseKg(int month)
        {
            switch (month)
            {
                case 3:
                    return 2_000m;
                case 7:
                    return 3_000m;
                default:
                    return 0m;
            }
        }

        public decimal GetCountyReproductionUseKg(int month)
        {
            switch (month)
            {
                case 3:
                    return 7_200m;
                case 7:
                    return 10_800m;
                default:
                    return 0m;
            }
        }

        public decimal GetRegionalReproductionUseKg(int month)
        {
            switch (month)
            {
                case 3:
                    return 20_800m;
                case 7:
                    return 31_200m;
                default:
                    return 0m;
            }
        }

        public decimal GetVillageRentKg(int month)
        {
            switch (month)
            {
                case 6:
                    return 2_800m;
                case 10:
                    return 5_200m;
                default:
                    return 0m;
            }
        }

        public decimal GetTaxCallSilver(int month)
        {
            switch (month)
            {
                case 7:
                    return CollectibleTaxCallSilver * 0.40m;
                case 11:
                    return CollectibleTaxCallSilver * 0.60m;
                default:
                    return 0m;
            }
        }
    }

    public sealed class MonthlySettlementSnapshot
    {
        internal MonthlySettlementSnapshot(
            int month,
            int settlementOwnerCount,
            decimal villageProductionKg,
            decimal villagePurchasedKg,
            decimal villageSoldKg,
            decimal villageConsumedKg,
            decimal villageFoodShortfallKg,
            decimal villageGrainKg,
            decimal villageDebtSilver,
            decimal villageTaxArrearsSilver,
            decimal countyGrainKg,
            decimal regionalGrainKg,
            decimal taxPaidSilver,
            decimal taxRemittedSilver,
            decimal worldGrainKg,
            decimal grainConservationErrorKg,
            decimal worldSilver,
            decimal silverConservationError,
            decimal debtCounterpartError,
            decimal taxCounterpartError)
        {
            Month = month;
            SettlementOwnerCount = settlementOwnerCount;
            VillageProductionKg = villageProductionKg;
            VillagePurchasedKg = villagePurchasedKg;
            VillageSoldKg = villageSoldKg;
            VillageConsumedKg = villageConsumedKg;
            VillageFoodShortfallKg = villageFoodShortfallKg;
            VillageGrainKg = villageGrainKg;
            VillageDebtSilver = villageDebtSilver;
            VillageTaxArrearsSilver = villageTaxArrearsSilver;
            CountyGrainKg = countyGrainKg;
            RegionalGrainKg = regionalGrainKg;
            TaxPaidSilver = taxPaidSilver;
            TaxRemittedSilver = taxRemittedSilver;
            WorldGrainKg = worldGrainKg;
            GrainConservationErrorKg = grainConservationErrorKg;
            WorldSilver = worldSilver;
            SilverConservationError = silverConservationError;
            DebtCounterpartError = debtCounterpartError;
            TaxCounterpartError = taxCounterpartError;
        }

        public int Month { get; }

        public int SettlementOwnerCount { get; }

        public decimal VillageProductionKg { get; }

        public decimal VillagePurchasedKg { get; }

        public decimal VillageSoldKg { get; }

        public decimal VillageConsumedKg { get; }

        public decimal VillageFoodShortfallKg { get; }

        public decimal VillageGrainKg { get; }

        public decimal VillageDebtSilver { get; }

        public decimal VillageTaxArrearsSilver { get; }

        public decimal CountyGrainKg { get; }

        public decimal RegionalGrainKg { get; }

        public decimal TaxPaidSilver { get; }

        public decimal TaxRemittedSilver { get; }

        public decimal WorldGrainKg { get; }

        public decimal GrainConservationErrorKg { get; }

        public decimal WorldSilver { get; }

        public decimal SilverConservationError { get; }

        public decimal DebtCounterpartError { get; }

        public decimal TaxCounterpartError { get; }
    }

    public sealed class LayeredSettlementYearResult
    {
        internal LayeredSettlementYearResult(
            LayeredSettlementYearScenario scenario,
            IList<MonthlySettlementSnapshot> months,
            decimal openingWorldGrainKg,
            decimal closingWorldGrainKg,
            decimal openingWorldSilver,
            decimal closingWorldSilver,
            decimal totalProductionKg,
            decimal totalConsumptionKg,
            decimal totalReproductionUseKg,
            decimal totalStorageLossKg,
            decimal totalVillagePurchasedKg,
            decimal totalVillageSoldKg,
            decimal totalVillageRentKg,
            decimal totalDebtInterestSilver,
            decimal peakVillageDebtSilver,
            decimal taxPaidSilver,
            decimal taxArrearsSilver,
            decimal taxRemittedSilver,
            decimal countyTaxRetainedSilver,
            decimal finalVillageGrainKg,
            decimal finalVillageDebtSilver,
            decimal finalCountyGrainKg,
            decimal finalRegionalGrainKg,
            decimal grainConservationErrorKg,
            decimal silverConservationError,
            decimal debtCounterpartError,
            decimal taxCounterpartError,
            decimal totalFoodShortfallKg,
            LayeredSettlementYearAccountingSummary accountingSummary)
        {
            Scenario = scenario;
            Months = new ReadOnlyCollection<MonthlySettlementSnapshot>(months);
            OpeningWorldGrainKg = openingWorldGrainKg;
            ClosingWorldGrainKg = closingWorldGrainKg;
            OpeningWorldSilver = openingWorldSilver;
            ClosingWorldSilver = closingWorldSilver;
            TotalProductionKg = totalProductionKg;
            TotalConsumptionKg = totalConsumptionKg;
            TotalReproductionUseKg = totalReproductionUseKg;
            TotalStorageLossKg = totalStorageLossKg;
            TotalVillagePurchasedKg = totalVillagePurchasedKg;
            TotalVillageSoldKg = totalVillageSoldKg;
            TotalVillageRentKg = totalVillageRentKg;
            TotalDebtInterestSilver = totalDebtInterestSilver;
            PeakVillageDebtSilver = peakVillageDebtSilver;
            TaxPaidSilver = taxPaidSilver;
            TaxArrearsSilver = taxArrearsSilver;
            TaxRemittedSilver = taxRemittedSilver;
            CountyTaxRetainedSilver = countyTaxRetainedSilver;
            FinalVillageGrainKg = finalVillageGrainKg;
            FinalVillageDebtSilver = finalVillageDebtSilver;
            FinalCountyGrainKg = finalCountyGrainKg;
            FinalRegionalGrainKg = finalRegionalGrainKg;
            GrainConservationErrorKg = grainConservationErrorKg;
            SilverConservationError = silverConservationError;
            DebtCounterpartError = debtCounterpartError;
            TaxCounterpartError = taxCounterpartError;
            TotalFoodShortfallKg = totalFoodShortfallKg;
            AccountingSummary = accountingSummary ?? throw new ArgumentNullException(nameof(accountingSummary));

            StateFingerprint = string.Join(
                "|",
                Scenario.ScenarioId,
                Format(ClosingWorldGrainKg),
                Format(ClosingWorldSilver),
                Format(FinalVillageGrainKg),
                Format(FinalVillageDebtSilver),
                Format(TaxArrearsSilver),
                Format(FinalCountyGrainKg),
                Format(FinalRegionalGrainKg));
        }

        public LayeredSettlementYearScenario Scenario { get; }

        public IReadOnlyList<MonthlySettlementSnapshot> Months { get; }

        public decimal OpeningWorldGrainKg { get; }

        public decimal ClosingWorldGrainKg { get; }

        public decimal OpeningWorldSilver { get; }

        public decimal ClosingWorldSilver { get; }

        public decimal TotalProductionKg { get; }

        public decimal TotalConsumptionKg { get; }

        public decimal TotalReproductionUseKg { get; }

        public decimal TotalStorageLossKg { get; }

        public decimal TotalVillagePurchasedKg { get; }

        public decimal TotalVillageSoldKg { get; }

        public decimal TotalVillageRentKg { get; }

        public decimal TotalDebtInterestSilver { get; }

        public decimal PeakVillageDebtSilver { get; }

        public decimal TaxPaidSilver { get; }

        public decimal TaxArrearsSilver { get; }

        public decimal TaxRemittedSilver { get; }

        public decimal CountyTaxRetainedSilver { get; }

        public decimal FinalVillageGrainKg { get; }

        public decimal FinalVillageDebtSilver { get; }

        public decimal FinalCountyGrainKg { get; }

        public decimal FinalRegionalGrainKg { get; }

        public decimal GrainConservationErrorKg { get; }

        public decimal SilverConservationError { get; }

        public decimal DebtCounterpartError { get; }

        public decimal TaxCounterpartError { get; }

        public decimal TotalFoodShortfallKg { get; }

        public LayeredSettlementYearAccountingSummary AccountingSummary { get; }

        public string StateFingerprint { get; }

        public bool AllInvariantsPassed =>
            Math.Abs(GrainConservationErrorKg) <= 0.000001m &&
            Math.Abs(SilverConservationError) <= 0.000001m &&
            Math.Abs(DebtCounterpartError) <= 0.000001m &&
            Math.Abs(TaxCounterpartError) <= 0.000001m;

        private static string Format(decimal value)
        {
            return decimal.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }
    }

    public sealed class LayeredSettlementYearProbe
    {
        public const int MonthsInEconomicYear = 12;

        private static readonly StableId VillageEntityId = new StableId("PROBE-VILLAGE");
        private static readonly StableId CountyResidualEntityId = new StableId("PROBE-COUNTY-RESIDUAL");
        private static readonly StableId RegionalResidualEntityId = new StableId("PROBE-REGIONAL-RESIDUAL");
        private static readonly StableId VillageLedgerId = new StableId("LEDGER-VILLAGE-DETAILED");
        private static readonly StableId CountyLedgerId = new StableId("LEDGER-COUNTY-COHORT");
        private static readonly StableId RegionalLedgerId = new StableId("LEDGER-REGIONAL-AGGREGATE");

        private readonly LayeredSettlementYearScenario _scenario;

        public LayeredSettlementYearProbe(LayeredSettlementYearScenario scenario)
        {
            _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        }

        public LayeredSettlementYearResult Run()
        {
            var state = new ProbeState
            {
                VillageGrainKg = _scenario.VillageOpeningGrainKg,
                VillageSilver = _scenario.VillageOpeningSilver,
                CountyGrainKg = _scenario.CountyOpeningGrainKg,
                CountyMarketSilver = _scenario.CountyMarketOpeningSilver,
                CountyTreasurySilver = _scenario.CountyTreasuryOpeningSilver,
                RegionalGrainKg = _scenario.RegionalOpeningGrainKg,
                RegionalTreasurySilver = _scenario.RegionalTreasuryOpeningSilver
            };

            var openingWorldGrainKg = GetWorldGrainKg(state);
            var openingWorldSilver = GetWorldSilver(state);
            var snapshots = new List<MonthlySettlementSnapshot>(MonthsInEconomicYear);

            for (var month = 1; month <= MonthsInEconomicYear; month++)
            {
                snapshots.Add(SettleMonth(month, state, openingWorldGrainKg, openingWorldSilver));
            }

            var accountingSummary = LayeredSettlementYearAccountingBuilder.Build(_scenario, snapshots);

            return new LayeredSettlementYearResult(
                _scenario,
                snapshots,
                openingWorldGrainKg,
                GetWorldGrainKg(state),
                openingWorldSilver,
                GetWorldSilver(state),
                state.TotalProductionKg,
                state.TotalConsumptionKg,
                state.TotalReproductionUseKg,
                state.TotalStorageLossKg,
                state.TotalVillagePurchasedKg,
                state.TotalVillageSoldKg,
                state.TotalVillageRentKg,
                state.TotalDebtInterestSilver,
                state.PeakVillageDebtSilver,
                state.TaxPaidSilver,
                state.VillageTaxArrearsSilver,
                state.TaxRemittedSilver,
                state.TaxPaidSilver - state.TaxRemittedSilver,
                state.VillageGrainKg,
                state.VillageDebtSilver,
                state.CountyGrainKg,
                state.RegionalGrainKg,
                GetGrainConservationError(state, openingWorldGrainKg),
                GetWorldSilver(state) - openingWorldSilver,
                state.VillageDebtSilver - state.CountyDebtReceivableSilver,
                state.VillageTaxArrearsSilver - state.CountyTaxReceivableSilver,
                state.TotalFoodShortfallKg,
                accountingSummary);
        }

        private MonthlySettlementSnapshot SettleMonth(
            int month,
            ProbeState state,
            decimal openingWorldGrainKg,
            decimal openingWorldSilver)
        {
            var ownership = new SettlementOwnershipRegistry();
            ownership.Claim(VillageEntityId, VillageLedgerId);
            ownership.Claim(CountyResidualEntityId, CountyLedgerId);
            ownership.Claim(RegionalResidualEntityId, RegionalLedgerId);

            var debtInterest = state.VillageDebtSilver * _scenario.MonthlyDebtInterestRate;
            state.VillageDebtSilver += debtInterest;
            state.CountyDebtReceivableSilver += debtInterest;
            state.TotalDebtInterestSilver += debtInterest;

            var villageProductionKg = _scenario.GetVillageProductionKg(month);
            var countyProductionKg = _scenario.GetCountyProductionKg(month);
            var regionalProductionKg = _scenario.GetRegionalProductionKg(month);
            state.VillageGrainKg += villageProductionKg;
            state.CountyGrainKg += countyProductionKg;
            state.RegionalGrainKg += regionalProductionKg;
            state.TotalProductionKg += villageProductionKg + countyProductionKg + regionalProductionKg;

            var villageReproductionUseKg = _scenario.GetVillageReproductionUseKg(month);
            var countyReproductionUseKg = _scenario.GetCountyReproductionUseKg(month);
            var regionalReproductionUseKg = _scenario.GetRegionalReproductionUseKg(month);
            ConsumeRequired(ref state.VillageGrainKg, villageReproductionUseKg, "village reproduction use");
            ConsumeRequired(ref state.CountyGrainKg, countyReproductionUseKg, "county reproduction use");
            ConsumeRequired(ref state.RegionalGrainKg, regionalReproductionUseKg, "regional reproduction use");
            state.TotalReproductionUseKg +=
                villageReproductionUseKg + countyReproductionUseKg + regionalReproductionUseKg;

            var rentKg = _scenario.GetVillageRentKg(month);
            TransferRequired(ref state.VillageGrainKg, ref state.CountyGrainKg, rentKg, "village rent");
            state.TotalVillageRentKg += rentKg;

            var villageDemandKg = AllocateAnnualWholeUnits(
                _scenario.VillageAdultEquivalentPopulation * _scenario.AnnualFoodKgPerAdultEquivalent,
                month);
            var villagePurchasedKg = Math.Max(0m, villageDemandKg - state.VillageGrainKg);
            if (villagePurchasedKg > 0m)
            {
                TransferRequired(
                    ref state.CountyGrainKg,
                    ref state.VillageGrainKg,
                    villagePurchasedKg,
                    "village food purchase");

                var purchaseCostSilver = villagePurchasedKg / _scenario.GrainKgPerSilver;
                var cashPaidSilver = Math.Min(state.VillageSilver, purchaseCostSilver);
                state.VillageSilver -= cashPaidSilver;
                state.CountyMarketSilver += cashPaidSilver;

                var creditSilver = purchaseCostSilver - cashPaidSilver;
                state.VillageDebtSilver += creditSilver;
                state.CountyDebtReceivableSilver += creditSilver;
                state.TotalVillagePurchasedKg += villagePurchasedKg;
            }

            var villageConsumedKg = ConsumeAvailable(ref state.VillageGrainKg, villageDemandKg);
            var villageFoodShortfallKg = villageDemandKg - villageConsumedKg;

            var countyDemandKg = AllocateAnnualWholeUnits(
                _scenario.CountyAdultEquivalentPopulation * _scenario.AnnualFoodKgPerAdultEquivalent,
                month);
            var countyConsumedKg = ConsumeAvailable(ref state.CountyGrainKg, countyDemandKg);

            var regionalDemandKg = AllocateAnnualWholeUnits(
                _scenario.RegionalAdultEquivalentPopulation * _scenario.AnnualFoodKgPerAdultEquivalent,
                month);
            var regionalConsumedKg = ConsumeAvailable(ref state.RegionalGrainKg, regionalDemandKg);

            var foodShortfallKg =
                villageFoodShortfallKg +
                (countyDemandKg - countyConsumedKg) +
                (regionalDemandKg - regionalConsumedKg);
            state.TotalFoodShortfallKg += foodShortfallKg;
            state.TotalConsumptionKg += villageConsumedKg + countyConsumedKg + regionalConsumedKg;

            var taxCallSilver = _scenario.GetTaxCallSilver(month);
            state.VillageTaxArrearsSilver += taxCallSilver;
            state.CountyTaxReceivableSilver += taxCallSilver;

            var villageSoldKg = 0m;
            var taxPaidSilver = 0m;
            if (month == 6 || month == 10 || month == 11)
            {
                var monthlyVillageDemandKg =
                    _scenario.VillageAdultEquivalentPopulation *
                    _scenario.AnnualFoodKgPerAdultEquivalent /
                    MonthsInEconomicYear;
                var reserveKg = monthlyVillageDemandKg * _scenario.HouseholdReserveMonths;
                var obligationsSilver = state.VillageDebtSilver + state.VillageTaxArrearsSilver;
                villageSoldKg = Min(
                    Math.Max(0m, state.VillageGrainKg - reserveKg),
                    obligationsSilver * _scenario.GrainKgPerSilver,
                    state.CountyMarketSilver * _scenario.GrainKgPerSilver);

                if (villageSoldKg > 0m)
                {
                    TransferRequired(
                        ref state.VillageGrainKg,
                        ref state.CountyGrainKg,
                        villageSoldKg,
                        "village obligation sale");

                    var saleRevenueSilver = villageSoldKg / _scenario.GrainKgPerSilver;
                    TransferRequired(
                        ref state.CountyMarketSilver,
                        ref state.VillageSilver,
                        saleRevenueSilver,
                        "village sale proceeds");
                    state.TotalVillageSoldKg += villageSoldKg;
                }

                var debtPaidSilver = Math.Min(state.VillageSilver, state.VillageDebtSilver);
                TransferRequired(
                    ref state.VillageSilver,
                    ref state.CountyMarketSilver,
                    debtPaidSilver,
                    "village debt payment");
                state.VillageDebtSilver -= debtPaidSilver;
                state.CountyDebtReceivableSilver -= debtPaidSilver;

                taxPaidSilver = Math.Min(state.VillageSilver, state.VillageTaxArrearsSilver);
                TransferRequired(
                    ref state.VillageSilver,
                    ref state.CountyTreasurySilver,
                    taxPaidSilver,
                    "village tax payment");
                state.VillageTaxArrearsSilver -= taxPaidSilver;
                state.CountyTaxReceivableSilver -= taxPaidSilver;
                state.TaxPaidSilver += taxPaidSilver;
            }

            var taxRemittedSilver = 0m;
            if (month == 11)
            {
                taxRemittedSilver =
                    state.TaxPaidSilver * _scenario.CountyRemittanceShare - state.TaxRemittedSilver;
                TransferRequired(
                    ref state.CountyTreasurySilver,
                    ref state.RegionalTreasurySilver,
                    taxRemittedSilver,
                    "county tax remittance");
                state.TaxRemittedSilver += taxRemittedSilver;
            }

            var monthlyStorageLossRate = _scenario.AnnualStorageLossRate / MonthsInEconomicYear;
            var villageStorageLossKg = state.VillageGrainKg * monthlyStorageLossRate;
            var countyStorageLossKg = state.CountyGrainKg * monthlyStorageLossRate;
            var regionalStorageLossKg = state.RegionalGrainKg * monthlyStorageLossRate;
            state.VillageGrainKg -= villageStorageLossKg;
            state.CountyGrainKg -= countyStorageLossKg;
            state.RegionalGrainKg -= regionalStorageLossKg;
            state.TotalStorageLossKg += villageStorageLossKg + countyStorageLossKg + regionalStorageLossKg;

            state.PeakVillageDebtSilver = Math.Max(state.PeakVillageDebtSilver, state.VillageDebtSilver);

            var worldGrainKg = GetWorldGrainKg(state);
            var worldSilver = GetWorldSilver(state);
            return new MonthlySettlementSnapshot(
                month,
                ownership.Count,
                villageProductionKg,
                villagePurchasedKg,
                villageSoldKg,
                villageConsumedKg,
                villageFoodShortfallKg,
                state.VillageGrainKg,
                state.VillageDebtSilver,
                state.VillageTaxArrearsSilver,
                state.CountyGrainKg,
                state.RegionalGrainKg,
                taxPaidSilver,
                taxRemittedSilver,
                worldGrainKg,
                GetGrainConservationError(state, openingWorldGrainKg),
                worldSilver,
                worldSilver - openingWorldSilver,
                state.VillageDebtSilver - state.CountyDebtReceivableSilver,
                state.VillageTaxArrearsSilver - state.CountyTaxReceivableSilver);
        }

        private static decimal AllocateAnnualWholeUnits(decimal annualUnits, int month)
        {
            if (month < 1 || month > MonthsInEconomicYear)
            {
                throw new ArgumentOutOfRangeException(nameof(month));
            }

            if (annualUnits != decimal.Truncate(annualUnits))
            {
                throw new ArgumentException("The probe distributes only whole annual units.", nameof(annualUnits));
            }

            var baseUnits = decimal.Floor(annualUnits / MonthsInEconomicYear);
            var remainder = (int)(annualUnits - baseUnits * MonthsInEconomicYear);
            return baseUnits + (month <= remainder ? 1m : 0m);
        }

        private static decimal ConsumeAvailable(ref decimal inventory, decimal requested)
        {
            var consumed = Math.Min(inventory, requested);
            inventory -= consumed;
            return consumed;
        }

        private static void ConsumeRequired(ref decimal inventory, decimal amount, string reason)
        {
            if (amount < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (inventory < amount)
            {
                throw new InvalidOperationException($"Insufficient inventory for {reason}.");
            }

            inventory -= amount;
        }

        private static void TransferRequired(
            ref decimal source,
            ref decimal destination,
            decimal amount,
            string reason)
        {
            if (amount < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (source < amount)
            {
                throw new InvalidOperationException($"Insufficient source balance for {reason}.");
            }

            source -= amount;
            destination += amount;
        }

        private static decimal Min(decimal first, decimal second, decimal third)
        {
            return Math.Min(first, Math.Min(second, third));
        }

        private static decimal GetWorldGrainKg(ProbeState state)
        {
            return state.VillageGrainKg + state.CountyGrainKg + state.RegionalGrainKg;
        }

        private static decimal GetWorldSilver(ProbeState state)
        {
            return
                state.VillageSilver +
                state.CountyMarketSilver +
                state.CountyTreasurySilver +
                state.RegionalTreasurySilver;
        }

        private static decimal GetGrainConservationError(ProbeState state, decimal openingWorldGrainKg)
        {
            return
                openingWorldGrainKg +
                state.TotalProductionKg -
                state.TotalConsumptionKg -
                state.TotalReproductionUseKg -
                state.TotalStorageLossKg -
                GetWorldGrainKg(state);
        }

        private sealed class ProbeState
        {
            public decimal VillageGrainKg;
            public decimal VillageSilver;
            public decimal VillageDebtSilver;
            public decimal VillageTaxArrearsSilver;
            public decimal CountyGrainKg;
            public decimal CountyMarketSilver;
            public decimal CountyTreasurySilver;
            public decimal CountyDebtReceivableSilver;
            public decimal CountyTaxReceivableSilver;
            public decimal RegionalGrainKg;
            public decimal RegionalTreasurySilver;
            public decimal TotalProductionKg;
            public decimal TotalConsumptionKg;
            public decimal TotalReproductionUseKg;
            public decimal TotalStorageLossKg;
            public decimal TotalVillagePurchasedKg;
            public decimal TotalVillageSoldKg;
            public decimal TotalVillageRentKg;
            public decimal TotalDebtInterestSilver;
            public decimal PeakVillageDebtSilver;
            public decimal TaxPaidSilver;
            public decimal TaxRemittedSilver;
            public decimal TotalFoodShortfallKg;
        }
    }
}
