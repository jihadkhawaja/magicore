using Xunit;

namespace MagiCore.Tests;

public sealed class RoboticsMemoryTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly MemoryAddOptions Scope = new() { UserId = "robot", AgentId = "unit" };

    [Fact]
    public async Task Recall_MovedObjectResolvesBeforeRadiusFilterAndSurvivesRestart()
    {
        var store = new InMemoryStore();
        var memory = new MemoryService(store: store);
        await memory.RememberObjectAsync(Observe("first", 1, CapturedAt), Scope);
        await memory.RememberObjectAsync(Observe("moved", 20, CapturedAt.AddMinutes(1)), Scope);
        await memory.RememberObjectAsync(Observe("late-arrival", 2, CapturedAt.AddSeconds(10)), Scope);
        var restarted = new MemoryService(store: store);
        Assert.Empty(await restarted.RecallObjectsAsync(Query(5)));
        var result = Assert.Single(await restarted.RecallObjectsAsync(Query(30)));
        Assert.Equal(20, result.Position.X);
        Assert.Equal(3, result.ObservationCount);
        Assert.Equal(2, result.RelocationCount);
        Assert.Equal(SpatialBeliefState.Observed, result.State);
        Assert.Equal(1, Assert.Single(await restarted.RecallObjectsAsync(Query(5) with { At = CapturedAt })).Position.X);
    }

    [Theory]
    [InlineData(ObjectVisibility.Occluded, SpatialBeliefState.Occluded)]
    [InlineData(ObjectVisibility.Absent, SpatialBeliefState.Missing)]
    public async Task Recall_VisibilityDoesNotInventANewLocationOrRefreshLastSeen(ObjectVisibility visibility, SpatialBeliefState expected)
    {
        var memory = new MemoryService();
        await memory.RememberObjectAsync(Observe("seen", 1, CapturedAt), Scope);
        await memory.RememberObjectAsync(Observe("visibility", 9, CapturedAt.AddSeconds(30)) with { Visibility = visibility }, Scope);
        var belief = Assert.Single(await memory.RecallObjectsAsync(Query()));
        Assert.Equal(expected, belief.State);
        Assert.Equal(1, belief.Position.X);
        Assert.Equal(CapturedAt, belief.LastSeenAt);
        Assert.True(belief.NeedsObservation);
        await memory.RememberObjectAsync(Observe("seen-again", 4, CapturedAt.AddMinutes(1)), Scope);
        Assert.False(Assert.Single(await memory.RecallObjectsAsync(Query())).NeedsObservation);
    }

    [Fact]
    public async Task Recall_DuplicateRetriesDoNotIncreaseEvidenceOrConfidenceAndAgingIsDeterministic()
    {
        var memory = new MemoryService();
        var evidence = Observe("same-frame", 1, CapturedAt);
        await memory.RememberObjectAsync(evidence, Scope);
        await memory.RememberObjectAsync(evidence, Scope);
        var belief = Assert.Single(await memory.RecallObjectsAsync(Query() with { At = CapturedAt.AddDays(1) }));
        Assert.Equal(1, belief.ObservationCount);
        Assert.Equal(0.45, belief.Confidence, 6);
        Assert.Equal(SpatialBeliefState.Stale, belief.State);
    }

    [Fact]
    public async Task Recall_ConflictsAreExplicitAndLaterCleanEvidenceResolvesSimultaneousDisagreement()
    {
        var memory = new MemoryService();
        await memory.RememberObjectAsync(Observe("camera", 1, CapturedAt), Scope);
        await memory.RememberObjectAsync(Observe("camera", 9, CapturedAt) with { SourceId = "other-camera" }, Scope);
        Assert.Equal(SpatialBeliefState.Conflicted, Assert.Single(await memory.RecallObjectsAsync(Query())).State);
        await memory.RememberObjectAsync(Observe("resolved", 9, CapturedAt.AddSeconds(30)), Scope);
        Assert.Equal(SpatialBeliefState.Observed, Assert.Single(await memory.RecallObjectsAsync(Query())).State);
    }

    [Fact]
    public async Task Recall_RejectsConflictingReuseOfAnImmutableEventId()
    {
        var memory = new MemoryService();
        await memory.RememberObjectAsync(Observe("same-id", 1, CapturedAt), Scope);
        await memory.RememberObjectAsync(Observe("same-id", 3, CapturedAt.AddSeconds(1)), Scope);
        Assert.Equal(SpatialBeliefState.Conflicted, Assert.Single(await memory.RecallObjectsAsync(Query())).State);
    }

    [Fact]
    public async Task Recall_WeakNewEvidenceDoesNotResurrectStrongOldEvidence()
    {
        var memory = new MemoryService();
        await memory.RememberObjectAsync(Observe("old", 1, CapturedAt), Scope);
        var weak = Observe("new", 10, CapturedAt.AddSeconds(1));
        await memory.RememberObjectAsync(weak with { Observation = weak.Observation with { Confidence = 0.1 } }, Scope);
        var options = Query() with { Spatial = Query().Spatial with { MinimumConfidence = 0.6 } };
        var belief = Assert.Single(await memory.RecallObjectsAsync(options));
        Assert.Equal(10, belief.Position.X);
        Assert.Equal(SpatialBeliefState.Uncertain, belief.State);
    }

    [Fact]
    public async Task Recall_IsolatesTenantsAgentsMapsFramesAndFutureEvidence()
    {
        var memory = new MemoryService();
        var evidence = Observe("seen", 1, CapturedAt);
        await memory.RememberObjectAsync(evidence, Scope with { UserId = "other" });
        await memory.RememberObjectAsync(evidence, Scope with { AgentId = "other" });
        await memory.RememberObjectAsync(evidence with { FrameId = "map-v2" }, Scope);
        await memory.RememberObjectAsync(evidence with { Observation = evidence.Observation with { MapId = "other" } }, Scope);
        await memory.RememberObjectAsync(Observe("future", 1, CapturedAt.AddDays(1)), Scope);
        await memory.RememberObjectAsync(evidence, Scope with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) });
        Assert.Empty(await memory.RecallObjectsAsync(Query()));
    }

    [Fact]
    public async Task Association_RefusesAmbiguousSameLabelObjectsAndRelationsExcludeStaleBeliefs()
    {
        var memory = new MemoryService();
        var first = Observe("first", 1, CapturedAt);
        var second = Observe("second", 2, CapturedAt);
        await memory.RememberObjectAsync(first, Scope);
        await memory.RememberObjectAsync(second with
        {
            Observation = second.Observation with { EntityId = "crate-2", Position = new SpatialPoint(2, 1, 0) }
        }, Scope);
        var objects = await memory.RecallObjectsAsync(Query());
        Assert.Null(RoboticsMemoryExtensions.AssociateObject(first.Observation, "map-v1", objects, 2));
        Assert.Equal("crate-1", RoboticsMemoryExtensions.AssociateObject(first.Observation, "map-v1", objects));
        Assert.Contains(RoboticsMemoryExtensions.GetObjectRelations(objects), relation => relation.Kind == SpatialRelationKind.Above && relation.SubjectId == "crate-2");
        var stale = await memory.RecallObjectsAsync(Query() with { At = CapturedAt.AddDays(1) });
        Assert.Empty(RoboticsMemoryExtensions.GetObjectRelations(stale));
    }

    [Fact]
    public async Task Evidence_ValidatesInputAndHonorsCancellation()
    {
        var memory = new MemoryService();
        var evidence = Observe("seen", 1, CapturedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => memory.RememberObjectAsync(evidence with { Observation = evidence.Observation with { EntityId = null } }, Scope));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => memory.RememberObjectAsync(evidence with { PositionUncertainty = double.NaN }, Scope));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => memory.RecallObjectsAsync(Query() with { ConfidenceHalfLife = TimeSpan.Zero }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => memory.RecallObjectsAsync(Query(), new CancellationToken(true)));
    }

    [Fact]
    public async Task Episodes_PersistFeedbackAndFilterHeadingWithAngleWrap()
    {
        var store = new InMemoryStore();
        var memory = new MemoryService(store: store);
        var episode = new RobotActionEpisode
        {
            EpisodeId = "attempt-1", MapId = "lab", FrameId = "map-v1", Action = "forward", Mission = "inspect",
            Start = new SpatialPoint(0, 0, 0), End = new SpatialPoint(0, 0, 0.1),
            HeadingRadians = Math.PI - 0.05, StartedAt = CapturedAt, CompletedAt = CapturedAt.AddSeconds(1),
            Outcome = RobotActionOutcome.Blocked, Feedback = "Controller detected wall contact."
        };
        await memory.RememberRobotEpisodeAsync(episode, Scope);
        await memory.RememberRobotEpisodeAsync(episode, Scope);
        await memory.RememberRobotEpisodeAsync(episode with { EpisodeId = "other-direction", HeadingRadians = 0 }, Scope);
        await memory.RememberRobotEpisodeAsync(episode, Scope with { UserId = "other" });
        var options = new RobotEpisodeRecallOptions
        {
            Spatial = Query().Spatial, FrameId = "map-v1", At = CapturedAt.AddSeconds(2), HeadingRadians = -Math.PI + 0.05
        };
        var restarted = new MemoryService(store: store);
        Assert.Equal(episode, Assert.Single(await restarted.RecallRobotEpisodesAsync(options)));
        Assert.Empty(await restarted.RecallRobotEpisodesAsync(options with { FrameId = "map-v2" }));
        Assert.Empty(await restarted.RecallRobotEpisodesAsync(options with { Action = "backward" }));
        Assert.Empty(await restarted.RecallRobotEpisodesAsync(options with { At = CapturedAt }));
        await Assert.ThrowsAsync<ArgumentException>(() => memory.RememberRobotEpisodeAsync(episode with { CompletedAt = CapturedAt.AddSeconds(-1) }, Scope));
    }

    private static RoboticsObservation Observe(string eventId, double positionX, DateTimeOffset time) => new()
    {
        ObservationId = eventId, SourceId = "rgbd", FrameId = "map-v1",
        Observation = new SpatialObservation
        {
            MapId = "lab", EntityId = "crate-1", Description = "red crate",
            Position = new SpatialPoint(positionX, 0, 0), ObservedAt = time, Confidence = 0.9
        }
    };

    private static RoboticsRecallOptions Query(double radius = 30) => new()
    {
        FrameId = "map-v1", At = CapturedAt.AddMinutes(1),
        Spatial = new SpatialRecallOptions
        {
            MapId = "lab", UserId = "robot", AgentId = "unit", Center = new SpatialPoint(0, 0, 0), Radius = radius
        }
    };
}