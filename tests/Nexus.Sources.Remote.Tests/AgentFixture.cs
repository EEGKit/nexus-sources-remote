using System.Diagnostics;

namespace Nexus.Sources.Tests;

public class AgentFixture : IDisposable
{
    private readonly Process _process;

    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public AgentFixture()
    {
        Initialize = Task.Run(async () =>
        {
            await _semaphore.WaitAsync(TimeSpan.FromMinutes(1));
        });

        /* Why not `dotnet run`? Because it spawns a child process for which
         * we do not know the process ID and so we cannot kill it.
         */

        // Build Nexus.Agent
        var psi_build = new ProcessStartInfo("dotnet")
        {
            Arguments = $"build ../../../../src/Nexus.Agent/Nexus.Agent.csproj",
            UseShellExecute = false
        };

        var process = new Process
        {
            StartInfo = psi_build
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new Exception("Unable to compile Nexus.Agent.");

        // Run Nexus.Agent
        var psi_run = new ProcessStartInfo("dotnet")
        {
            Arguments = $"../../artifacts/bin/Nexus.Agent/debug/Nexus.Agent.dll",
            WorkingDirectory="../../../../src/Nexus.Agent",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi_run.Environment["NEXUSAGENT_Paths__Config"] = "../../.nexus-agent/config";

        _process = new Process
        {
            StartInfo = psi_run,
            EnableRaisingEvents = true
        };

        _process.OutputDataReceived += (sender, e) =>
        {
            // File.AppendAllText("/home/vincent/Downloads/output.txt", e.Data + Environment.NewLine);

            if (e.Data is not null && e.Data.Contains("Now listening on"))
                _semaphore.Release();
        };

        _process.ErrorDataReceived += (sender, e) =>
        {
            // File.AppendAllText("/home/vincent/Downloads/error.txt", e.Data + Environment.NewLine);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public Task Initialize { get; }

    public void Dispose()
    {
        _process.Kill();
    }
}
