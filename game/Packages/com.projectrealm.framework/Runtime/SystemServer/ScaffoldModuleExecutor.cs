using ProjectRealm.Foundation;
using System;
using ProjectRealm.World;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>
    /// 尚未实现领域公式时使用的统一执行器。成功表示“框架调用完成”，
    /// <see cref="DataQuality.Unavailable"/> 才是业务数据不可用的真实语义。
    /// </summary>
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

    /// <summary>首版执行器注册表；遇到非 Scaffold 模块时快速失败，防止静默降级。</summary>
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

    /// <summary>未配置诊断消费者时使用的空对象实现。</summary>
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
