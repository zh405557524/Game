using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;

namespace ProjectRealm.World
{
    public enum CommandAssessmentKind
    {
        DirectCommand = 0,
        RequestOrNegotiate = 1,
        Unavailable = 2
    }

    public enum CommandAssessmentReason
    {
        ActivePositionGrant = 0,
        InfluenceRelationOnly = 1,
        UnknownTarget = 2,
        NoAuthorityOrInfluence = 3,
        ActorContextMismatch = 4,
        CommandDefinitionMismatch = 5
    }

    public sealed class CommandDefinition
    {
        public CommandDefinition(StableId commandId, bool allowsNegotiation)
        {
            if (string.IsNullOrEmpty(commandId.Value))
            {
                throw new ArgumentException("A command definition requires an ID.", nameof(commandId));
            }

            CommandId = commandId;
            AllowsNegotiation = allowsNegotiation;
        }

        public StableId CommandId { get; }

        public bool AllowsNegotiation { get; }
    }

    public sealed class AuthorityGrant
    {
        public AuthorityGrant(
            StableId grantId,
            PositionKind requiredPosition,
            StableId commandId,
            StableId scopeId)
        {
            EnsureSet(grantId, nameof(grantId));
            EnsureSet(commandId, nameof(commandId));
            EnsureSet(scopeId, nameof(scopeId));

            GrantId = grantId;
            RequiredPosition = requiredPosition;
            CommandId = commandId;
            ScopeId = scopeId;
        }

        public StableId GrantId { get; }

        public PositionKind RequiredPosition { get; }

        public StableId CommandId { get; }

        public StableId ScopeId { get; }

        private static void EnsureSet(StableId id, string parameterName)
        {
            if (string.IsNullOrEmpty(id.Value))
            {
                throw new ArgumentException("An authority grant requires stable IDs.", parameterName);
            }
        }
    }

    public sealed class CommandAttempt
    {
        public CommandAttempt(StableId actorId, StableId commandId, StableId targetId, StableId scopeId)
        {
            EnsureSet(actorId, nameof(actorId));
            EnsureSet(commandId, nameof(commandId));
            EnsureSet(targetId, nameof(targetId));
            EnsureSet(scopeId, nameof(scopeId));

            ActorId = actorId;
            CommandId = commandId;
            TargetId = targetId;
            ScopeId = scopeId;
        }

        public StableId ActorId { get; }

        public StableId CommandId { get; }

        public StableId TargetId { get; }

        public StableId ScopeId { get; }

        private static void EnsureSet(StableId id, string parameterName)
        {
            if (string.IsNullOrEmpty(id.Value))
            {
                throw new ArgumentException("A command attempt requires stable IDs.", parameterName);
            }
        }
    }

    public sealed class CommandAssessment
    {
        public CommandAssessment(
            CommandAssessmentKind kind,
            CommandAssessmentReason reason,
            string explanation,
            StableId? evidenceId = null)
        {
            if (string.IsNullOrWhiteSpace(explanation))
            {
                throw new ArgumentException("A command assessment requires a traceable explanation.", nameof(explanation));
            }

            Kind = kind;
            Reason = reason;
            Explanation = explanation;
            EvidenceId = evidenceId;
        }

        public CommandAssessmentKind Kind { get; }

        public CommandAssessmentReason Reason { get; }

        public string Explanation { get; }

        public StableId? EvidenceId { get; }
    }

    public sealed class CommandAuthorityEvaluator
    {
        public CommandAssessment Assess(
            CommandDefinition definition,
            CommandAttempt attempt,
            PersonAuthorityState authorityState,
            IEnumerable<AuthorityGrant> grants)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (attempt == null)
            {
                throw new ArgumentNullException(nameof(attempt));
            }

            if (authorityState == null)
            {
                throw new ArgumentNullException(nameof(authorityState));
            }

            if (grants == null)
            {
                throw new ArgumentNullException(nameof(grants));
            }

            if (!definition.CommandId.Equals(attempt.CommandId))
            {
                return new CommandAssessment(
                    CommandAssessmentKind.Unavailable,
                    CommandAssessmentReason.CommandDefinitionMismatch,
                    "The attempt does not match the command definition.");
            }

            if (!authorityState.PersonId.Equals(attempt.ActorId))
            {
                return new CommandAssessment(
                    CommandAssessmentKind.Unavailable,
                    CommandAssessmentReason.ActorContextMismatch,
                    "The authority state belongs to a different actor.");
            }

            if (!authorityState.Knows(attempt.TargetId))
            {
                return new CommandAssessment(
                    CommandAssessmentKind.Unavailable,
                    CommandAssessmentReason.UnknownTarget,
                    "The actor does not know the target well enough to address this command.");
            }

            var directAssessment = FindDirectAuthority(attempt, authorityState, grants);
            if (directAssessment != null)
            {
                return directAssessment;
            }

            var influence = FindInfluence(attempt, authorityState);
            if (definition.AllowsNegotiation && influence != null)
            {
                return new CommandAssessment(
                    CommandAssessmentKind.RequestOrNegotiate,
                    CommandAssessmentReason.InfluenceRelationOnly,
                    "The actor has a relationship with the target but no direct authority.",
                    influence.RelationId);
            }

            return new CommandAssessment(
                CommandAssessmentKind.Unavailable,
                CommandAssessmentReason.NoAuthorityOrInfluence,
                "The actor has neither direct authority nor a valid negotiation path.");
        }

        private static CommandAssessment FindDirectAuthority(
            CommandAttempt attempt,
            PersonAuthorityState authorityState,
            IEnumerable<AuthorityGrant> grants)
        {
            foreach (var assignment in authorityState.PositionAssignments)
            {
                if (!assignment.IsActive || !assignment.ScopeId.Equals(attempt.ScopeId))
                {
                    continue;
                }

                foreach (var grant in grants)
                {
                    if (grant == null)
                    {
                        throw new ArgumentException("Authority grants cannot contain null.", nameof(grants));
                    }

                    if (grant.RequiredPosition == assignment.Position
                        && grant.CommandId.Equals(attempt.CommandId)
                        && grant.ScopeId.Equals(attempt.ScopeId))
                    {
                        return new CommandAssessment(
                            CommandAssessmentKind.DirectCommand,
                            CommandAssessmentReason.ActivePositionGrant,
                            "An active position grants direct authority for this command and scope.",
                            grant.GrantId);
                    }
                }
            }

            return null;
        }

        private static InfluenceRelation FindInfluence(
            CommandAttempt attempt,
            PersonAuthorityState authorityState)
        {
            foreach (var relation in authorityState.InfluenceRelations)
            {
                if (relation.TargetId.Equals(attempt.TargetId))
                {
                    return relation;
                }
            }

            return null;
        }
    }
}
