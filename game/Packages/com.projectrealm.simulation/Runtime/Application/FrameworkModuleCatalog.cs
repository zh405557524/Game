using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using ProjectRealm.Domain;

namespace ProjectRealm.Application
{
    public static class FrameworkModuleCatalog
    {
        public const string Version = "framework-module-catalog-v1";

        private static readonly string[] CanonicalSourceNames =
        {
            "AdministrationModule",
            "AdoptionModule",
            "AgricultureModule",
            "AnimalHusbandryModule",
            "AppointmentModule",
            "ArchiveAndBookModule",
            "AssetRightModule",
            "BattleModule",
            "BudgetModule",
            "CohortBehaviorDecisionModule",
            "CohortSocietyProfileModule",
            "CollectiveActionFormationModule",
            "CommunicationModule",
            "ComplianceModule",
            "ConstructionModule",
            "DebtModule",
            "DeceptionModule",
            "DecisionExplanationModule",
            "DiplomacyModule",
            "DisasterModule",
            "EdictModule",
            "EducationModule",
            "EnvironmentalRiskModule",
            "EpidemicModule",
            "EquipmentModule",
            "FacilityModule",
            "FiscalModule",
            "GovernmentDecisionModule",
            "GovernmentPlanningModule",
            "HealthModule",
            "HouseholdDecisionModule",
            "HouseholdModule",
            "IndustrialOrganizationModule",
            "InstitutionModule",
            "IntelligenceModule",
            "JusticeModule",
            "KnowledgeDiffusionModule",
            "KnowledgeModule",
            "KnowledgeViewModule",
            "LaborModule",
            "LandModule",
            "LogisticsModule",
            "MarketInformationModule",
            "MarketModule",
            "MigrationModule",
            "MilitaryLogisticsModule",
            "MilitaryMaterielModule",
            "MilitaryOrganizationModule",
            "MilitaryTrainingModule",
            "MiningOperationModule",
            "MonetaryStockModule",
            "MovementAndOperationModule",
            "NaturalResourceModule",
            "ObservationModule",
            "OccupationModule",
            "OfficialProfileModule",
            "OrganizationDecisionModule",
            "PaymentModule",
            "PersonAttributeModule",
            "PersonDecisionModule",
            "PersonIdentityModule",
            "PersonRelationModule",
            "PopulationModule",
            "PositionModule",
            "ProductionMethodModule",
            "ProductionModule",
            "ProductionWorldModule",
            "PublicOrderModule",
            "RecoveryModule",
            "RecruitmentModule",
            "ReportModule",
            "ReputationModule",
            "ResearchModule",
            "RouteModule",
            "RuralInfrastructureModule",
            "SocialConflictModule",
            "SocialGroupModule",
            "SocialInfluenceModule",
            "SocialOrganizationModule",
            "SocietyAggregationModule",
            "SoilAndWaterModule",
            "StorageModule",
            "TaxAnalyticsModule",
            "TaxAssessmentModule",
            "TaxAuditModule",
            "TaxAuthorityModule",
            "TaxBaseObservationModule",
            "TaxCollectionModule",
            "TaxEnforcementModule",
            "TaxLiabilityModule",
            "TaxPolicyModule",
            "TaxRevenueRoutingModule",
            "TechnologyModule",
            "TradeModule",
            "TransferModule",
            "TransportModule",
            "TreasuryModule",
            "VisibilityPolicyModule",
            "WarModule",
            "WorkshopOperationModule",
            "WorldEventModule"
        };

        private static readonly HashSet<string> PerceptionStageModules = new HashSet<string>(StringComparer.Ordinal)
        {
            "CommunicationModule", "DeceptionModule", "IntelligenceModule", "KnowledgeViewModule",
            "MarketInformationModule", "ObservationModule", "ReportModule", "VisibilityPolicyModule"
        };

        private static readonly HashSet<string> DecisionStageModules = new HashSet<string>(StringComparer.Ordinal)
        {
            "CohortBehaviorDecisionModule", "CollectiveActionFormationModule", "DecisionExplanationModule",
            "GovernmentDecisionModule", "GovernmentPlanningModule", "HouseholdDecisionModule",
            "OrganizationDecisionModule", "PersonDecisionModule", "SocialInfluenceModule"
        };

        private static readonly HashSet<string> AggregationStageModules = new HashSet<string>(StringComparer.Ordinal)
        {
            "CohortSocietyProfileModule", "FiscalModule", "SocietyAggregationModule", "TaxAnalyticsModule"
        };

        private static readonly string[] AggregateCountySourceNames =
        {
            "AdministrationModule", "AgricultureModule", "EnvironmentalRiskModule", "FiscalModule",
            "MarketModule", "MilitaryOrganizationModule", "ObservationModule", "PopulationModule",
            "PublicOrderModule", "SocietyAggregationModule", "TransportModule", "TreasuryModule"
        };

        public static IReadOnlyList<string> CanonicalNames => new ReadOnlyCollection<string>(CanonicalSourceNames);

        public static IReadOnlyList<string> AggregateCountyNames => new ReadOnlyCollection<string>(AggregateCountySourceNames);

        public static ModuleCatalog Create()
        {
            var definitions = CanonicalSourceNames.Select(CreateDefinition).ToList();
            var definitionsByName = definitions.ToDictionary(definition => definition.SourceName, StringComparer.Ordinal);
            var aliases = new Dictionary<string, StableId>(StringComparer.Ordinal)
            {
                { "PersonModule", definitionsByName["PersonIdentityModule"].DefinitionId },
                { "TaxModule", definitionsByName["TaxPolicyModule"].DefinitionId }
            };
            return new ModuleCatalog(definitions, aliases);
        }

        public static StableId DefinitionIdFor(string sourceName)
        {
            return new StableId("module." + ToKebabCase(sourceName.Substring(0, sourceName.Length - "Module".Length)) + ".v1");
        }

        private static ModuleDefinition CreateDefinition(string sourceName)
        {
            var stem = ToKebabCase(sourceName.Substring(0, sourceName.Length - "Module".Length));
            var definitionId = new StableId("module." + stem + ".v1");
            var capabilityId = new StableId("capability." + stem + ".scaffold.v1");
            var authorityKey = new StableId("authority." + stem);
            return new ModuleDefinition(
                definitionId,
                sourceName,
                "scaffold-v1",
                ModuleImplementationTier.Scaffold,
                new[] { new CapabilityContract(capabilityId, authorityKey, CapabilityAuthorityMode.Authoritative, false) },
                new[] { ResolveStage(sourceName) },
                sourceDocument: ResolveSourceGroup(sourceName));
        }

        private static WorldExecutionStage ResolveStage(string sourceName)
        {
            if (PerceptionStageModules.Contains(sourceName))
            {
                return WorldExecutionStage.S60PerceptionBuild;
            }

            if (DecisionStageModules.Contains(sourceName))
            {
                return WorldExecutionStage.S70DecisionPlanning;
            }

            if (AggregationStageModules.Contains(sourceName))
            {
                return WorldExecutionStage.S40UpwardAggregation;
            }

            if (string.Equals(sourceName, "WorldEventModule", StringComparison.Ordinal))
            {
                return WorldExecutionStage.S110ImmediateExecution;
            }

            return WorldExecutionStage.S30LocalFactSettlement;
        }

        private static string ResolveSourceGroup(string sourceName)
        {
            if (sourceName.StartsWith("Tax", StringComparison.Ordinal)) return "23-tax";
            if (sourceName.StartsWith("Person", StringComparison.Ordinal) || sourceName.Contains("Society") || sourceName.Contains("Profile")) return "24-person-society";
            if (sourceName.Contains("Decision") || sourceName.Contains("Influence") || sourceName.Contains("CollectiveAction")) return "25-behavior";
            if (sourceName.Contains("Military") || sourceName == "BattleModule" || sourceName == "WarModule" || sourceName == "DiplomacyModule") return "15-military";
            if (sourceName.Contains("Knowledge") || sourceName.Contains("Education") || sourceName.Contains("Research") || sourceName.Contains("Technology")) return "14-knowledge";
            return "08-20-domain-modules";
        }

        private static string ToKebabCase(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsUpper(character) && index > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }
    }
}
