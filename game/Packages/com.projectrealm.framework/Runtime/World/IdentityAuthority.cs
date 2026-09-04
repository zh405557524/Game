using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;

namespace ProjectRealm.World
{
    public enum SocialIdentityKind
    {
        Villager = 0
    }

    public enum PositionKind
    {
        HouseholdHead = 0,
        MilitiaLeader = 1
    }

    public sealed class PositionAssignment
    {
        public PositionAssignment(
            StableId assignmentId,
            StableId personId,
            PositionKind position,
            StableId scopeId)
        {
            EnsureSet(assignmentId, nameof(assignmentId));
            EnsureSet(personId, nameof(personId));
            EnsureSet(scopeId, nameof(scopeId));

            AssignmentId = assignmentId;
            PersonId = personId;
            Position = position;
            ScopeId = scopeId;
            IsActive = true;
        }

        public StableId AssignmentId { get; }

        public StableId PersonId { get; }

        public PositionKind Position { get; }

        public StableId ScopeId { get; }

        public bool IsActive { get; private set; }

        public void Revoke()
        {
            IsActive = false;
        }

        private static void EnsureSet(StableId id, string parameterName)
        {
            if (string.IsNullOrEmpty(id.Value))
            {
                throw new ArgumentException("A position assignment requires stable IDs.", parameterName);
            }
        }
    }

    public sealed class InfluenceRelation
    {
        public InfluenceRelation(StableId relationId, StableId sourcePersonId, StableId targetId)
        {
            EnsureSet(relationId, nameof(relationId));
            EnsureSet(sourcePersonId, nameof(sourcePersonId));
            EnsureSet(targetId, nameof(targetId));

            RelationId = relationId;
            SourcePersonId = sourcePersonId;
            TargetId = targetId;
        }

        public StableId RelationId { get; }

        public StableId SourcePersonId { get; }

        public StableId TargetId { get; }

        private static void EnsureSet(StableId id, string parameterName)
        {
            if (string.IsNullOrEmpty(id.Value))
            {
                throw new ArgumentException("An influence relation requires stable IDs.", parameterName);
            }
        }
    }

    public sealed class PersonAuthorityState
    {
        private readonly List<StableId> _knownTargetIds;
        private readonly List<PositionAssignment> _positionAssignments;
        private readonly List<InfluenceRelation> _influenceRelations;

        public PersonAuthorityState(
            StableId personId,
            SocialIdentityKind identity,
            IEnumerable<StableId> knownTargetIds = null,
            IEnumerable<PositionAssignment> positionAssignments = null,
            IEnumerable<InfluenceRelation> influenceRelations = null)
        {
            if (string.IsNullOrEmpty(personId.Value))
            {
                throw new ArgumentException("An authority state requires a person ID.", nameof(personId));
            }

            PersonId = personId;
            Identity = identity;
            _knownTargetIds = CopyKnownTargets(knownTargetIds);
            _positionAssignments = CopyAssignments(personId, positionAssignments);
            _influenceRelations = CopyInfluenceRelations(personId, influenceRelations);
        }

        public StableId PersonId { get; }

        public SocialIdentityKind Identity { get; }

        public IReadOnlyList<PositionAssignment> PositionAssignments => _positionAssignments;

        public IReadOnlyList<InfluenceRelation> InfluenceRelations => _influenceRelations;

        public bool Knows(StableId targetId)
        {
            for (var index = 0; index < _knownTargetIds.Count; index++)
            {
                if (_knownTargetIds[index].Equals(targetId))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<StableId> CopyKnownTargets(IEnumerable<StableId> knownTargetIds)
        {
            var result = new List<StableId>();
            if (knownTargetIds == null)
            {
                return result;
            }

            foreach (var targetId in knownTargetIds)
            {
                if (string.IsNullOrEmpty(targetId.Value))
                {
                    throw new ArgumentException("Known targets require stable IDs.", nameof(knownTargetIds));
                }

                result.Add(targetId);
            }

            return result;
        }

        private static List<PositionAssignment> CopyAssignments(
            StableId personId,
            IEnumerable<PositionAssignment> assignments)
        {
            var result = new List<PositionAssignment>();
            if (assignments == null)
            {
                return result;
            }

            foreach (var assignment in assignments)
            {
                if (assignment == null)
                {
                    throw new ArgumentException("Position assignments cannot contain null.", nameof(assignments));
                }

                if (!assignment.PersonId.Equals(personId))
                {
                    throw new ArgumentException("Every position assignment must belong to the authority state person.", nameof(assignments));
                }

                result.Add(assignment);
            }

            return result;
        }

        private static List<InfluenceRelation> CopyInfluenceRelations(
            StableId personId,
            IEnumerable<InfluenceRelation> relations)
        {
            var result = new List<InfluenceRelation>();
            if (relations == null)
            {
                return result;
            }

            foreach (var relation in relations)
            {
                if (relation == null)
                {
                    throw new ArgumentException("Influence relations cannot contain null.", nameof(relations));
                }

                if (!relation.SourcePersonId.Equals(personId))
                {
                    throw new ArgumentException("Every influence relation must start from the authority state person.", nameof(relations));
                }

                result.Add(relation);
            }

            return result;
        }
    }
}
