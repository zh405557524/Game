using System;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Application
{
    public sealed class ScaffoldModuleExecutor : IModuleExecutor
    {
        public const string UnavailableReason = "implementation_unavailable";

        public ModuleResult Execute(ModuleExecutionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return new ModuleResult(
                context.TickId,
                context.Instance.InstanceId,
                context.Instance.NodeId,
                context.Stage,
                ModuleImplementationTier.Scaffold,
                DataQuality.Unavailable,
                true,
                UnavailableReason);
        }
    }

    public sealed class DefaultModuleExecutorFactory : IModuleExecutorFactory
    {
        public IModuleExecutor Create(ModuleDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (definition.ImplementationTier != ModuleImplementationTier.Scaffold)
            {
                throw new InvalidOperationException(
                    $"No executor is registered for non-scaffold module '{definition.DefinitionId}'.");
            }

            return new ScaffoldModuleExecutor();
        }
    }

    public sealed class NullSimulationDiagnosticsSink : ISimulationDiagnosticsSink
    {
        public void RecordStage(TickId tickId, StageExecutionRecord stage)
        {
        }

        public void RecordModuleResult(ModuleResult result)
        {
        }

        public void RecordCommandStatus(CommandStatusEvent statusEvent)
        {
        }
    }
}
