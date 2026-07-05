using System.Net.Sockets;
using Xunit;

namespace WaPlatform.IntegrationTests.Support;

/// <summary>
/// A <see cref="FactAttribute"/> that skips (not hangs, not a cryptic red X) when no Docker
/// daemon is reachable — issue #46's requirement that this suite be "skipped-or-green locally
/// when Docker isn't available." <see cref="FactAttribute.Skip"/> is an ordinary settable
/// property, not a compile-time constant, so it can be computed once here at test-discovery time;
/// xunit v2 has no <c>Skip.If</c> (that is a v3/SkippableFact-package feature) and this project
/// takes no new dependency for it.
///
/// Detection: try to open the socket Docker.DotNet/Testcontainers would use (DOCKER_HOST if set,
/// else the platform default unix socket) with a short timeout. This does not guarantee
/// Testcontainers itself will succeed (wrong daemon version, no image pull access, etc.) — those
/// still surface as a real (loud) test failure from <see cref="DatabaseFixture"/>, which is
/// correct: "cryptic failure" refers to a silent hang, not a daemon-reachable-but-something-else-
/// wrong failure.
/// </summary>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> UnavailableReason = new(DetectDockerUnavailableReason);

    public RequiresDockerFactAttribute()
    {
        if (UnavailableReason.Value is { } reason)
        {
            Skip = reason;
        }
    }

    private static string? DetectDockerUnavailableReason()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        var candidates = dockerHost is { Length: > 0 }
            ? [dockerHost]
            : new[]
            {
                // Default daemon socket, and this dev machine's Colima socket (see
                // .claude/agent-memory/dotnet-backend-developer/local-docker-backend-colima.md) —
                // tried in order so the suite works out of the box on both a plain Docker Desktop
                // machine and this Colima-based one without requiring DOCKER_HOST to be exported.
                "unix:///var/run/docker.sock",
                $"unix://{Environment.GetEnvironmentVariable("HOME")}/.colima/default/docker.sock",
            };

        foreach (var candidate in candidates)
        {
            if (TryConnect(candidate))
            {
                return null; // reachable — do not skip
            }
        }

        return "Docker daemon not reachable (checked DOCKER_HOST / /var/run/docker.sock / " +
               "~/.colima/default/docker.sock). Start Docker Desktop or run 'colima start', or " +
               "export DOCKER_HOST, to run this real-Postgres integration suite.";
    }

    private static bool TryConnect(string unixSocketUrl)
    {
        if (!unixSocketUrl.StartsWith("unix://", StringComparison.Ordinal))
        {
            return false; // TCP DOCKER_HOST values aren't this dev machine's shape; let Testcontainers try.
        }

        var path = unixSocketUrl["unix://".Length..];
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var connectTask = socket.ConnectAsync(new UnixDomainSocketEndPoint(path));
            return connectTask.Wait(TimeSpan.FromSeconds(2)) && socket.Connected;
        }
        catch
        {
            return false;
        }
    }
}
