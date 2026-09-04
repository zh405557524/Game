using ProjectRealm.Foundation;
using NUnit.Framework;
using ProjectRealm.World;

namespace ProjectRealm.Tests.Unit
{
    public sealed class CommandAuthorityTests
    {
        private static readonly StableId MobilizeMilitiaCommandId = new StableId("command.mobilize-militia");
        private static readonly StableId MilitiaTeamId = new StableId("militia.team.east");
        private static readonly StableId VillageScopeId = new StableId("settlement.east-village");

        [Test]
        public void SameCommandProducesAllThreeAuthorityOutcomes()
        {
            var evaluator = new CommandAuthorityEvaluator();
            var definition = new CommandDefinition(MobilizeMilitiaCommandId, allowsNegotiation: true);
            var leaderGrant = CreateMilitiaLeaderGrant();

            var villagerId = new StableId("person.villager");
            var villager = new PersonAuthorityState(villagerId, SocialIdentityKind.Villager);
            var villagerAssessment = evaluator.Assess(
                definition,
                CreateAttempt(villagerId),
                villager,
                new[] { leaderGrant });

            var householdHeadId = new StableId("person.household-head");
            var householdHead = new PersonAuthorityState(
                householdHeadId,
                SocialIdentityKind.Villager,
                new[] { MilitiaTeamId },
                new[]
                {
                    new PositionAssignment(
                        new StableId("position.household-head"),
                        householdHeadId,
                        PositionKind.HouseholdHead,
                        VillageScopeId)
                },
                new[]
                {
                    new InfluenceRelation(
                        new StableId("influence.household-to-militia"),
                        householdHeadId,
                        MilitiaTeamId)
                });
            var householdHeadAssessment = evaluator.Assess(
                definition,
                CreateAttempt(householdHeadId),
                householdHead,
                new[] { leaderGrant });

            var militiaLeaderId = new StableId("person.militia-leader");
            var militiaLeader = new PersonAuthorityState(
                militiaLeaderId,
                SocialIdentityKind.Villager,
                new[] { MilitiaTeamId },
                new[] { CreateMilitiaLeaderAssignment(militiaLeaderId) });
            var militiaLeaderAssessment = evaluator.Assess(
                definition,
                CreateAttempt(militiaLeaderId),
                militiaLeader,
                new[] { leaderGrant });

            Assert.That(villagerAssessment.Kind, Is.EqualTo(CommandAssessmentKind.Unavailable));
            Assert.That(villagerAssessment.Reason, Is.EqualTo(CommandAssessmentReason.UnknownTarget));
            Assert.That(householdHeadAssessment.Kind, Is.EqualTo(CommandAssessmentKind.RequestOrNegotiate));
            Assert.That(householdHeadAssessment.Reason, Is.EqualTo(CommandAssessmentReason.InfluenceRelationOnly));
            Assert.That(militiaLeaderAssessment.Kind, Is.EqualTo(CommandAssessmentKind.DirectCommand));
            Assert.That(militiaLeaderAssessment.Reason, Is.EqualTo(CommandAssessmentReason.ActivePositionGrant));
        }

        [Test]
        public void RevokingPositionImmediatelyRemovesDirectAuthority()
        {
            var evaluator = new CommandAuthorityEvaluator();
            var definition = new CommandDefinition(MobilizeMilitiaCommandId, allowsNegotiation: true);
            var leaderId = new StableId("person.revoked-leader");
            var assignment = CreateMilitiaLeaderAssignment(leaderId);
            var influence = new InfluenceRelation(
                new StableId("influence.former-leader"),
                leaderId,
                MilitiaTeamId);
            var authority = new PersonAuthorityState(
                leaderId,
                SocialIdentityKind.Villager,
                new[] { MilitiaTeamId },
                new[] { assignment },
                new[] { influence });

            var beforeRevocation = evaluator.Assess(
                definition,
                CreateAttempt(leaderId),
                authority,
                new[] { CreateMilitiaLeaderGrant() });

            assignment.Revoke();

            var afterRevocation = evaluator.Assess(
                definition,
                CreateAttempt(leaderId),
                authority,
                new[] { CreateMilitiaLeaderGrant() });

            Assert.That(beforeRevocation.Kind, Is.EqualTo(CommandAssessmentKind.DirectCommand));
            Assert.That(afterRevocation.Kind, Is.EqualTo(CommandAssessmentKind.RequestOrNegotiate));
            Assert.That(afterRevocation.Reason, Is.EqualTo(CommandAssessmentReason.InfluenceRelationOnly));
            Assert.That(afterRevocation.EvidenceId, Is.EqualTo(influence.RelationId));
        }

        [Test]
        public void UnavailableAssessmentKeepsTraceableReason()
        {
            var actorId = new StableId("person.isolated-villager");
            var assessment = new CommandAuthorityEvaluator().Assess(
                new CommandDefinition(MobilizeMilitiaCommandId, allowsNegotiation: true),
                CreateAttempt(actorId),
                new PersonAuthorityState(
                    actorId,
                    SocialIdentityKind.Villager,
                    new[] { MilitiaTeamId }),
                new[] { CreateMilitiaLeaderGrant() });

            Assert.That(assessment.Kind, Is.EqualTo(CommandAssessmentKind.Unavailable));
            Assert.That(assessment.Reason, Is.EqualTo(CommandAssessmentReason.NoAuthorityOrInfluence));
            Assert.That(assessment.Explanation, Is.Not.Empty);
            Assert.That(assessment.EvidenceId, Is.Null);
        }

        private static AuthorityGrant CreateMilitiaLeaderGrant()
        {
            return new AuthorityGrant(
                new StableId("grant.militia-leader.mobilize"),
                PositionKind.MilitiaLeader,
                MobilizeMilitiaCommandId,
                VillageScopeId);
        }

        private static PositionAssignment CreateMilitiaLeaderAssignment(StableId leaderId)
        {
            return new PositionAssignment(
                new StableId($"position.militia-leader.{leaderId.Value}"),
                leaderId,
                PositionKind.MilitiaLeader,
                VillageScopeId);
        }

        private static CommandAttempt CreateAttempt(StableId actorId)
        {
            return new CommandAttempt(actorId, MobilizeMilitiaCommandId, MilitiaTeamId, VillageScopeId);
        }
    }
}
