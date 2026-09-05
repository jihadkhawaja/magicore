using System.Text.Json;

namespace MagiCore;

public static partial class RoboticsMemoryExtensions
{
    private const string EpisodeKey = "robot_action_episode_v1";
    private const string EpisodeType = "robot_action_episode";

    /// <summary>Persists a controller-reported action attempt without language-model inference.</summary>
    /// <param name="memory">Backing service.</param>
    /// <param name="episode">Measured execution feedback.</param>
    /// <param name="options">User, agent and retention scope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The storage result.</returns>
    public static Task<AddResult> RememberRobotEpisodeAsync(this IMemoryService memory, RobotActionEpisode episode,
        MemoryAddOptions options, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(memory);
        Guard.NotNull(options);
        Guard.NotNullOrWhiteSpace(options.UserId);
        Validate(episode);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = options.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<string, string>();
        metadata[EpisodeKey] = JsonSerializer.Serialize(episode);
        return memory.AddAsync($"{episode.Action}: {episode.Outcome}. {episode.Feedback}", options with
        {
            Metadata = metadata, MemoryType = EpisodeType, Infer = false, Deduplicate = false,
            ReferenceTime = episode.CompletedAt
        }, cancellationToken);
    }

    /// <summary>Recalls recent, nearby attempts, optionally matching heading and skill. Outcomes are advisory, not a safety clearance.</summary>
    /// <param name="memory">Backing service.</param>
    /// <param name="options">Recall geometry, scope and event-time limits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Attempts ordered by completion time descending; identical write retries appear once.</returns>
    public static async Task<IReadOnlyList<RobotActionEpisode>> RecallRobotEpisodesAsync(this IMemoryService memory,
        RobotEpisodeRecallOptions options, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(memory);
        Guard.NotNull(options);
        Validate(new RoboticsRecallOptions { Spatial = options.Spatial, FrameId = options.FrameId });
        if ((options.HeadingRadians.HasValue && !SpatialPoint.IsFiniteNumber(options.HeadingRadians.Value))
            || !SpatialPoint.IsFiniteNumber(options.HeadingToleranceRadians)
            || options.HeadingToleranceRadians < 0 || options.HeadingToleranceRadians > Math.PI)
            throw new ArgumentOutOfRangeException(nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        var query = options.Spatial;
        var records = await memory.GetAllAsync(new MemoryFilter(UserId: query.UserId, AgentId: query.AgentId), cancellationToken).ConfigureAwait(false);
        var episodes = new List<RobotActionEpisode>();
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.MemoryType != EpisodeType || record.ExpiresAt <= DateTimeOffset.UtcNow
                || !record.Metadata.TryGetValue(EpisodeKey, out var json)) continue;
            try
            {
                var episode = JsonSerializer.Deserialize<RobotActionEpisode>(json);
                if (episode is null) continue;
                Validate(episode);
                if (episode.MapId != query.MapId || episode.FrameId != options.FrameId
                    || episode.CompletedAt > options.At || episode.CompletedAt < query.ObservedAfter
                    || episode.Start.DistanceTo(query.Center) > query.Radius
                    || (query.EntityId is not null && query.EntityId != episode.EntityId)
                    || (options.Action is not null && options.Action != episode.Action)) continue;
                if (options.HeadingRadians.HasValue)
                {
                    var difference = episode.HeadingRadians % (2 * Math.PI) - options.HeadingRadians.Value % (2 * Math.PI);
                    if (Math.Abs(Math.Atan2(Math.Sin(difference), Math.Cos(difference))) > options.HeadingToleranceRadians) continue;
                }
                episodes.Add(episode);
            }
            catch (JsonException) { }
            catch (ArgumentException) { }
        }
        return episodes.Distinct().OrderByDescending(item => item.CompletedAt)
            .ThenBy(item => item.EpisodeId, StringComparer.Ordinal).Take(query.TopK).ToArray();
    }

    private static void Validate(RobotActionEpisode episode)
    {
        Guard.NotNull(episode);
        Guard.NotNullOrWhiteSpace(episode.EpisodeId);
        Guard.NotNullOrWhiteSpace(episode.MapId);
        Guard.NotNullOrWhiteSpace(episode.FrameId);
        Guard.NotNullOrWhiteSpace(episode.Mission);
        Guard.NotNullOrWhiteSpace(episode.Action);
        Guard.NotNullOrWhiteSpace(episode.Feedback);
        if (episode.Start is null || !episode.Start.IsFinite || episode.End is null || !episode.End.IsFinite
            || !SpatialPoint.IsFiniteNumber(episode.HeadingRadians) || episode.CompletedAt < episode.StartedAt
            || !Enum.IsDefined(typeof(RobotActionOutcome), episode.Outcome))
            throw new ArgumentException("An episode requires finite poses, ordered timestamps and a valid outcome.", nameof(episode));
    }
}