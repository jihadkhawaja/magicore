using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace MagiCore;

internal static class Guard
{
    public static void NotNull([NotNull] object? value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value is null) ThrowArgumentNull(parameterName);
    }

    public static void NotNullOrWhiteSpace([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value is null) ThrowArgument(parameterName);
        if (string.IsNullOrWhiteSpace(value)) ThrowArgument(parameterName);
    }

    [DoesNotReturn]
    private static void ThrowArgumentNull(string? parameterName) => throw new ArgumentNullException(parameterName);

    [DoesNotReturn]
    private static void ThrowArgument(string? parameterName) => throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
}

internal static class Compatibility
{
    public static double Clamp(double value, double minimum, double maximum) => Math.Max(minimum, Math.Min(maximum, value));

    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public static string Sha256Hex(string value)
    {
        using var algorithm = SHA256.Create();
        var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static Task<string> ReadAsStringAsync(HttpContent content, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
#else
        return content.ReadAsStringAsync(cancellationToken);
#endif
    }

    public static HttpRequestException CreateHttpRequestException(string message, HttpStatusCode statusCode)
    {
#if NETSTANDARD2_0
        return new HttpRequestException(message);
#else
        return new HttpRequestException(message, null, statusCode);
#endif
    }

    public static async Task ForEachAsync<T>(IEnumerable<T> source, int maxDegreeOfParallelism, Func<T, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        var tasks = source.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await action(item, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
    }
}

#if NETSTANDARD2_0
internal static class CollectionCompatibilityExtensions
{
    public static TValue? GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key) =>
        dictionary.TryGetValue(key, out var value) ? value : default;

    public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null) => new(source, comparer);

    public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        if (dictionary.ContainsKey(key)) return false;
        dictionary.Add(key, value);
        return true;
    }
}
#endif