using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ProjectRealm.Domain
{
    public enum SimulationDriverOrigin
    {
        ExternalCondition = 0,
        GovernmentPolicy = 1,
        ActorAction = 2,
        WorldStateFeedback = 3
    }

    public enum SimulationDriverKind
    {
        Weather = 0,
        NaturalDisaster = 1,
        Epidemic = 2,
        War = 3,
        MarketDisruption = 4,
        EdictExecution = 5,
        TechnologyAdoption = 6,
        ConstructionCompletion = 7,
        ActorOperation = 8,
        ResourceShortage = 9
    }

    public enum SimulationEffectKind
    {
        ProductionMultiplier = 0,
        IntermediateInputMultiplier = 1,
        LossRateAdditive = 2,
        LaborAvailabilityMultiplier = 3,
        TransportCapacityMultiplier = 4,
        EducationCapacityMultiplier = 5,
        HealthRiskAdditive = 6,
        MilitaryReadinessMultiplier = 7
    }

    public sealed class SimulationDriverEffect
    {
        public SimulationDriverEffect(
            StableId effectId,
            SimulationEffectKind kind,
            decimal magnitude,
            StableId? targetMetricId = null,
            EconomicSectorKind? targetSector = null)
        {
            LedgerContractGuard.RequireId(effectId, nameof(effectId));
            LedgerContractGuard.RequireNullableId(targetMetricId, nameof(targetMetricId));
            if (!targetMetricId.HasValue && !targetSector.HasValue)
            {
                throw new ArgumentException("A simulation effect requires a metric or sector target.");
            }

            switch (kind)
            {
                case SimulationEffectKind.ProductionMultiplier:
                case SimulationEffectKind.IntermediateInputMultiplier:
                case SimulationEffectKind.LaborAvailabilityMultiplier:
                case SimulationEffectKind.TransportCapacityMultiplier:
                case SimulationEffectKind.EducationCapacityMultiplier:
                case SimulationEffectKind.MilitaryReadinessMultiplier:
                    LedgerContractGuard.RequireNonNegative(magnitude, nameof(magnitude));
                    break;
                case SimulationEffectKind.LossRateAdditive:
                case SimulationEffectKind.HealthRiskAdditive:
                    if (magnitude < -1m || magnitude > 1m)
                    {
                        throw new ArgumentOutOfRangeException(nameof(magnitude));
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            EffectId = effectId;
            Kind = kind;
            Magnitude = magnitude;
            TargetMetricId = targetMetricId;
            TargetSector = targetSector;
        }

        public StableId EffectId { get; }

        public SimulationEffectKind Kind { get; }

        /// <summary>
        /// Multiplier kinds use 1 as neutral. Additive kinds use 0 as neutral.
        /// The driver execution rate scales the distance from that neutral value.
        /// </summary>
        public decimal Magnitude { get; }

        public StableId? TargetMetricId { get; }

        public EconomicSectorKind? TargetSector { get; }

        public bool Matches(EconomicSectorKind sector, StableId metricId)
        {
            var sectorMatches = !TargetSector.HasValue || TargetSector.Value == sector;
            var metricMatches = !TargetMetricId.HasValue || TargetMetricId.Value.Equals(metricId);
            return sectorMatches && metricMatches;
        }
    }

    public sealed class SimulationDriverRecord
    {
        public SimulationDriverRecord(
            StableId driverId,
            StableId scopeId,
            SimulationDriverOrigin origin,
            SimulationDriverKind kind,
            LedgerPeriod createdPeriod,
            LedgerPeriod effectivePeriod,
            LedgerPeriod expiresAfterPeriod,
            decimal executionRate,
            IEnumerable<SimulationDriverEffect> effects,
            StableId? sourceDecisionId = null,
            StableId? sourceEdictId = null,
            IEnumerable<StableId> causalSourceIds = null)
        {
            LedgerContractGuard.RequireId(driverId, nameof(driverId));
            LedgerContractGuard.RequireId(scopeId, nameof(scopeId));
            LedgerContractGuard.RequireNullableId(sourceDecisionId, nameof(sourceDecisionId));
            LedgerContractGuard.RequireNullableId(sourceEdictId, nameof(sourceEdictId));
            LedgerContractGuard.RequireRatio(executionRate, nameof(executionRate));
            RequireMonthlyPeriod(createdPeriod, nameof(createdPeriod));
            RequireMonthlyPeriod(effectivePeriod, nameof(effectivePeriod));
            if (expiresAfterPeriod != null)
            {
                RequireMonthlyPeriod(expiresAfterPeriod, nameof(expiresAfterPeriod));
                if (ComparePeriods(expiresAfterPeriod, effectivePeriod) < 0)
                {
                    throw new ArgumentException("A driver cannot expire before it becomes effective.");
                }
            }

            if ((origin == SimulationDriverOrigin.GovernmentPolicy ||
                 origin == SimulationDriverOrigin.ActorAction) &&
                ComparePeriods(effectivePeriod, createdPeriod) <= 0)
            {
                throw new ArgumentException(
                    "Government and actor decisions can affect only a later complete settlement period.");
            }

            DriverId = driverId;
            ScopeId = scopeId;
            Origin = origin;
            Kind = kind;
            CreatedPeriod = createdPeriod;
            EffectivePeriod = effectivePeriod;
            ExpiresAfterPeriod = expiresAfterPeriod;
            ExecutionRate = executionRate;
            Effects = CopyEffects(effects);
            SourceDecisionId = sourceDecisionId;
            SourceEdictId = sourceEdictId;
            CausalSourceIds = LedgerContractGuard.CopyUniqueIds(causalSourceIds, nameof(causalSourceIds));
        }

        public StableId DriverId { get; }

        public StableId ScopeId { get; }

        public SimulationDriverOrigin Origin { get; }

        public SimulationDriverKind Kind { get; }

        public LedgerPeriod CreatedPeriod { get; }

        public LedgerPeriod EffectivePeriod { get; }

        public LedgerPeriod ExpiresAfterPeriod { get; }

        public decimal ExecutionRate { get; }

        public IReadOnlyList<SimulationDriverEffect> Effects { get; }

        public StableId? SourceDecisionId { get; }

        public StableId? SourceEdictId { get; }

        public IReadOnlyList<StableId> CausalSourceIds { get; }

        public bool IsActive(StableId scopeId, LedgerPeriod period)
        {
            if (!ScopeId.Equals(scopeId) || period == null || period.Kind != LedgerPeriodKind.Monthly)
            {
                return false;
            }

            return ComparePeriods(period, EffectivePeriod) >= 0 &&
                   (ExpiresAfterPeriod == null || ComparePeriods(period, ExpiresAfterPeriod) <= 0);
        }

        private static IReadOnlyList<SimulationDriverEffect> CopyEffects(
            IEnumerable<SimulationDriverEffect> effects)
        {
            var result = new List<SimulationDriverEffect>();
            var ids = new HashSet<StableId>();
            if (effects != null)
            {
                foreach (var effect in effects)
                {
                    if (effect == null)
                    {
                        throw new ArgumentException("Simulation effects cannot contain null.", nameof(effects));
                    }

                    if (!ids.Add(effect.EffectId))
                    {
                        throw new ArgumentException("Simulation effect IDs must be unique.", nameof(effects));
                    }

                    result.Add(effect);
                }
            }

            if (result.Count == 0)
            {
                throw new ArgumentException("A simulation driver requires at least one effect.", nameof(effects));
            }

            return new ReadOnlyCollection<SimulationDriverEffect>(result);
        }

        private static void RequireMonthlyPeriod(LedgerPeriod period, string parameterName)
        {
            if (period == null || period.Kind != LedgerPeriodKind.Monthly)
            {
                throw new ArgumentException("Simulation drivers require monthly periods.", parameterName);
            }
        }

        private static int ComparePeriods(LedgerPeriod left, LedgerPeriod right)
        {
            return (left.Year * 12 + left.Month).CompareTo(right.Year * 12 + right.Month);
        }
    }

    public sealed class ValuedCommodityQuantity
    {
        public ValuedCommodityQuantity(
            StableId metricId,
            string unit,
            decimal quantity,
            decimal currentUnitValue,
            decimal referenceUnitValue)
        {
            LedgerContractGuard.RequireId(metricId, nameof(metricId));
            LedgerContractGuard.RequireText(unit, nameof(unit));
            LedgerContractGuard.RequireNonNegative(quantity, nameof(quantity));
            LedgerContractGuard.RequireNonNegative(currentUnitValue, nameof(currentUnitValue));
            LedgerContractGuard.RequireNonNegative(referenceUnitValue, nameof(referenceUnitValue));

            MetricId = metricId;
            Unit = unit;
            Quantity = quantity;
            CurrentUnitValue = currentUnitValue;
            ReferenceUnitValue = referenceUnitValue;
        }

        public StableId MetricId { get; }

        public string Unit { get; }

        public decimal Quantity { get; }

        public decimal CurrentUnitValue { get; }

        public decimal ReferenceUnitValue { get; }

        public decimal CurrentValue => Quantity * CurrentUnitValue;

        public decimal ReferenceValue => Quantity * ReferenceUnitValue;
    }

    public sealed class SectorProductionActivity
    {
        public SectorProductionActivity(
            StableId activityId,
            StableId operatorId,
            StableId scopeId,
            EconomicSectorKind sector,
            IEnumerable<ValuedCommodityQuantity> baselineOutputs,
            IEnumerable<ValuedCommodityQuantity> plannedIntermediateInputs,
            decimal laborCompensation,
            decimal landAndAssetRent,
            decimal netProductionTaxes,
            decimal laborPersonMonths,
            decimal capacityUtilization,
            decimal plannedSalesValue,
            decimal plannedInventoryChangeValue)
        {
            LedgerContractGuard.RequireId(activityId, nameof(activityId));
            LedgerContractGuard.RequireId(operatorId, nameof(operatorId));
            LedgerContractGuard.RequireId(scopeId, nameof(scopeId));
            LedgerContractGuard.RequireNonNegative(laborCompensation, nameof(laborCompensation));
            LedgerContractGuard.RequireNonNegative(landAndAssetRent, nameof(landAndAssetRent));
            LedgerContractGuard.RequireNonNegative(laborPersonMonths, nameof(laborPersonMonths));
            LedgerContractGuard.RequireRatio(capacityUtilization, nameof(capacityUtilization));
            LedgerContractGuard.RequireNonNegative(plannedSalesValue, nameof(plannedSalesValue));

            ActivityId = activityId;
            OperatorId = operatorId;
            ScopeId = scopeId;
            Sector = sector;
            BaselineOutputs = CopyCommodityQuantities(baselineOutputs, nameof(baselineOutputs), true);
            PlannedIntermediateInputs = CopyCommodityQuantities(
                plannedIntermediateInputs,
                nameof(plannedIntermediateInputs),
                false);
            LaborCompensation = laborCompensation;
            LandAndAssetRent = landAndAssetRent;
            NetProductionTaxes = netProductionTaxes;
            LaborPersonMonths = laborPersonMonths;
            CapacityUtilization = capacityUtilization;
            PlannedSalesValue = plannedSalesValue;
            PlannedInventoryChangeValue = plannedInventoryChangeValue;
        }

        public StableId ActivityId { get; }

        public StableId OperatorId { get; }

        public StableId ScopeId { get; }

        public EconomicSectorKind Sector { get; }

        public IReadOnlyList<ValuedCommodityQuantity> BaselineOutputs { get; }

        public IReadOnlyList<ValuedCommodityQuantity> PlannedIntermediateInputs { get; }

        public decimal LaborCompensation { get; }

        public decimal LandAndAssetRent { get; }

        public decimal NetProductionTaxes { get; }

        public decimal LaborPersonMonths { get; }

        public decimal CapacityUtilization { get; }

        public decimal PlannedSalesValue { get; }

        public decimal PlannedInventoryChangeValue { get; }

        private static IReadOnlyList<ValuedCommodityQuantity> CopyCommodityQuantities(
            IEnumerable<ValuedCommodityQuantity> values,
            string parameterName,
            bool requireAtLeastOne)
        {
            var result = new List<ValuedCommodityQuantity>();
            if (values != null)
            {
                foreach (var value in values)
                {
                    if (value == null)
                    {
                        throw new ArgumentException("Commodity quantities cannot contain null.", parameterName);
                    }

                    result.Add(value);
                }
            }

            if (requireAtLeastOne && result.Count == 0)
            {
                throw new ArgumentException("A production activity requires at least one output.", parameterName);
            }

            return new ReadOnlyCollection<ValuedCommodityQuantity>(result);
        }
    }

    public sealed class SettledCommodityQuantity
    {
        internal SettledCommodityQuantity(
            ValuedCommodityQuantity baseline,
            decimal grossQuantity,
            decimal lostQuantity,
            decimal usableQuantity)
        {
            Baseline = baseline;
            GrossQuantity = grossQuantity;
            LostQuantity = lostQuantity;
            UsableQuantity = usableQuantity;
        }

        public ValuedCommodityQuantity Baseline { get; }

        public decimal GrossQuantity { get; }

        public decimal LostQuantity { get; }

        public decimal UsableQuantity { get; }

        public decimal CurrentUsableValue => UsableQuantity * Baseline.CurrentUnitValue;

        public decimal ReferenceUsableValue => UsableQuantity * Baseline.ReferenceUnitValue;
    }

    public sealed class ProductionActivitySettlement
    {
        internal ProductionActivitySettlement(
            SectorProductionActivity activity,
            IReadOnlyList<SettledCommodityQuantity> outputs,
            IReadOnlyList<SettledCommodityQuantity> intermediateInputs,
            IReadOnlyList<StableId> appliedDriverIds,
            decimal salesValue,
            decimal inventoryChangeValue)
        {
            Activity = activity;
            Outputs = outputs;
            IntermediateInputs = intermediateInputs;
            AppliedDriverIds = appliedDriverIds;
            SalesValue = salesValue;
            InventoryChangeValue = inventoryChangeValue;
        }

        public SectorProductionActivity Activity { get; }

        public IReadOnlyList<SettledCommodityQuantity> Outputs { get; }

        public IReadOnlyList<SettledCommodityQuantity> IntermediateInputs { get; }

        public IReadOnlyList<StableId> AppliedDriverIds { get; }

        public decimal SalesValue { get; }

        public decimal InventoryChangeValue { get; }

        public decimal NominalGrossOutput => Sum(Outputs, output => output.CurrentUsableValue);

        public decimal NominalIntermediateConsumption =>
            Sum(IntermediateInputs, input => input.CurrentUsableValue);

        public decimal RealGrossOutputAtReferencePrices =>
            Sum(Outputs, output => output.ReferenceUsableValue);

        public decimal RealIntermediateConsumptionAtReferencePrices =>
            Sum(IntermediateInputs, input => input.ReferenceUsableValue);

        public decimal NominalValueAdded => NominalGrossOutput - NominalIntermediateConsumption;

        public decimal MixedIncomeAndOperatingSurplus =>
            NominalValueAdded -
            Activity.LaborCompensation -
            Activity.LandAndAssetRent -
            Activity.NetProductionTaxes;

        private static decimal Sum(
            IReadOnlyList<SettledCommodityQuantity> values,
            Func<SettledCommodityQuantity, decimal> selector)
        {
            var total = 0m;
            for (var index = 0; index < values.Count; index++)
            {
                total += selector(values[index]);
            }

            return total;
        }
    }

    public sealed class ProductionSettlementService
    {
        public ProductionActivitySettlement Settle(
            SectorProductionActivity activity,
            LedgerPeriod period,
            IEnumerable<SimulationDriverRecord> drivers)
        {
            if (activity == null)
            {
                throw new ArgumentNullException(nameof(activity));
            }

            if (period == null || period.Kind != LedgerPeriodKind.Monthly)
            {
                throw new ArgumentException("Production activities settle in a monthly period.", nameof(period));
            }

            var activeDrivers = GetActiveDrivers(activity.ScopeId, period, drivers);
            var outputs = new List<SettledCommodityQuantity>();
            var baselineOutputValue = 0m;
            var settledOutputValue = 0m;
            for (var index = 0; index < activity.BaselineOutputs.Count; index++)
            {
                var baseline = activity.BaselineOutputs[index];
                var multiplier = GetCombinedMultiplier(
                    activity.Sector,
                    baseline.MetricId,
                    SimulationEffectKind.ProductionMultiplier,
                    activeDrivers);
                var lossRate = GetCombinedAdditive(
                    activity.Sector,
                    baseline.MetricId,
                    SimulationEffectKind.LossRateAdditive,
                    activeDrivers);
                lossRate = Math.Max(0m, Math.Min(1m, lossRate));
                var gross = baseline.Quantity * multiplier;
                var lost = gross * lossRate;
                var usable = gross - lost;
                var settled = new SettledCommodityQuantity(baseline, gross, lost, usable);
                outputs.Add(settled);
                baselineOutputValue += baseline.CurrentValue;
                settledOutputValue += settled.CurrentUsableValue;
            }

            var inputs = new List<SettledCommodityQuantity>();
            for (var index = 0; index < activity.PlannedIntermediateInputs.Count; index++)
            {
                var baseline = activity.PlannedIntermediateInputs[index];
                var multiplier = GetCombinedMultiplier(
                    activity.Sector,
                    baseline.MetricId,
                    SimulationEffectKind.IntermediateInputMultiplier,
                    activeDrivers);
                var used = baseline.Quantity * multiplier;
                inputs.Add(new SettledCommodityQuantity(baseline, used, 0m, used));
            }

            var outputScale = baselineOutputValue <= 0m ? 0m : settledOutputValue / baselineOutputValue;
            var appliedDriverIds = new List<StableId>();
            for (var index = 0; index < activeDrivers.Count; index++)
            {
                appliedDriverIds.Add(activeDrivers[index].DriverId);
            }

            return new ProductionActivitySettlement(
                activity,
                new ReadOnlyCollection<SettledCommodityQuantity>(outputs),
                new ReadOnlyCollection<SettledCommodityQuantity>(inputs),
                new ReadOnlyCollection<StableId>(appliedDriverIds),
                activity.PlannedSalesValue * outputScale,
                activity.PlannedInventoryChangeValue * outputScale);
        }

        private static List<SimulationDriverRecord> GetActiveDrivers(
            StableId scopeId,
            LedgerPeriod period,
            IEnumerable<SimulationDriverRecord> drivers)
        {
            var result = new List<SimulationDriverRecord>();
            if (drivers != null)
            {
                foreach (var driver in drivers)
                {
                    if (driver == null)
                    {
                        throw new ArgumentException("Simulation drivers cannot contain null.", nameof(drivers));
                    }

                    if (driver.IsActive(scopeId, period))
                    {
                        result.Add(driver);
                    }
                }
            }

            result.Sort((left, right) => string.CompareOrdinal(left.DriverId.Value, right.DriverId.Value));
            return result;
        }

        private static decimal GetCombinedMultiplier(
            EconomicSectorKind sector,
            StableId metricId,
            SimulationEffectKind kind,
            IList<SimulationDriverRecord> drivers)
        {
            var multiplier = 1m;
            for (var driverIndex = 0; driverIndex < drivers.Count; driverIndex++)
            {
                var driver = drivers[driverIndex];
                for (var effectIndex = 0; effectIndex < driver.Effects.Count; effectIndex++)
                {
                    var effect = driver.Effects[effectIndex];
                    if (effect.Kind != kind || !effect.Matches(sector, metricId))
                    {
                        continue;
                    }

                    multiplier *= 1m + (effect.Magnitude - 1m) * driver.ExecutionRate;
                }
            }

            return Math.Max(0m, multiplier);
        }

        private static decimal GetCombinedAdditive(
            EconomicSectorKind sector,
            StableId metricId,
            SimulationEffectKind kind,
            IList<SimulationDriverRecord> drivers)
        {
            var total = 0m;
            for (var driverIndex = 0; driverIndex < drivers.Count; driverIndex++)
            {
                var driver = drivers[driverIndex];
                for (var effectIndex = 0; effectIndex < driver.Effects.Count; effectIndex++)
                {
                    var effect = driver.Effects[effectIndex];
                    if (effect.Kind == kind && effect.Matches(sector, metricId))
                    {
                        total += effect.Magnitude * driver.ExecutionRate;
                    }
                }
            }

            return total;
        }
    }

    public sealed class EconomicAccountingService
    {
        public EconomicSectorStatement BuildSectorStatement(
            EconomicSectorKind sector,
            string valuationUnit,
            int referencePriceYear,
            IEnumerable<ProductionActivitySettlement> settlements)
        {
            LedgerContractGuard.RequireText(valuationUnit, nameof(valuationUnit));
            if (referencePriceYear <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(referencePriceYear));
            }

            if (settlements == null)
            {
                throw new ArgumentNullException(nameof(settlements));
            }

            var nominalOutput = 0m;
            var nominalInputs = 0m;
            var realOutput = 0m;
            var realInputs = 0m;
            var laborCompensation = 0m;
            var rent = 0m;
            var taxes = 0m;
            var mixedIncome = 0m;
            var laborMonths = 0m;
            var weightedUtilization = 0m;
            var utilizationWeight = 0m;
            var sales = 0m;
            var inventoryChange = 0m;
            var count = 0;

            foreach (var settlement in settlements)
            {
                if (settlement == null)
                {
                    throw new ArgumentException("Production settlements cannot contain null.", nameof(settlements));
                }

                if (settlement.Activity.Sector != sector)
                {
                    throw new ArgumentException("Every production settlement must belong to the requested sector.");
                }

                nominalOutput += settlement.NominalGrossOutput;
                nominalInputs += settlement.NominalIntermediateConsumption;
                realOutput += settlement.RealGrossOutputAtReferencePrices;
                realInputs += settlement.RealIntermediateConsumptionAtReferencePrices;
                laborCompensation += settlement.Activity.LaborCompensation;
                rent += settlement.Activity.LandAndAssetRent;
                taxes += settlement.Activity.NetProductionTaxes;
                mixedIncome += settlement.MixedIncomeAndOperatingSurplus;
                laborMonths += settlement.Activity.LaborPersonMonths;
                var weight = settlement.Activity.LaborPersonMonths > 0m
                    ? settlement.Activity.LaborPersonMonths
                    : 1m;
                weightedUtilization += settlement.Activity.CapacityUtilization * weight;
                utilizationWeight += weight;
                sales += settlement.SalesValue;
                inventoryChange += settlement.InventoryChangeValue;
                count++;
            }

            if (count == 0)
            {
                throw new ArgumentException("At least one production settlement is required.", nameof(settlements));
            }

            return new EconomicSectorStatement(
                sector,
                valuationUnit,
                referencePriceYear,
                nominalOutput,
                nominalInputs,
                realOutput,
                realInputs,
                laborCompensation,
                rent,
                taxes,
                mixedIncome,
                laborMonths,
                utilizationWeight <= 0m ? 0m : weightedUtilization / utilizationWeight,
                sales,
                inventoryChange);
        }

        public EconomicOutputStatement BuildOutputStatement(
            IEnumerable<EconomicSectorStatement> sectors,
            string valuationUnit,
            int referencePriceYear,
            decimal householdFinalConsumption,
            decimal governmentAndMilitaryFinalConsumption,
            decimal grossFixedCapitalFormation,
            decimal inventoryChange,
            decimal externalExports,
            decimal externalImports)
        {
            var sectorList = new List<EconomicSectorStatement>();
            if (sectors != null)
            {
                sectorList.AddRange(sectors);
            }

            var productionValueAdded = 0m;
            for (var index = 0; index < sectorList.Count; index++)
            {
                productionValueAdded += sectorList[index].NominalValueAdded;
            }

            var expenditure =
                householdFinalConsumption +
                governmentAndMilitaryFinalConsumption +
                grossFixedCapitalFormation +
                inventoryChange +
                externalExports -
                externalImports;
            var discrepancy = productionValueAdded - expenditure;

            return new EconomicOutputStatement(
                sectorList,
                valuationUnit,
                referencePriceYear,
                householdFinalConsumption,
                governmentAndMilitaryFinalConsumption,
                grossFixedCapitalFormation,
                inventoryChange,
                externalExports,
                externalImports,
                discrepancy);
        }
    }
}
