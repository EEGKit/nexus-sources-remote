using System.Diagnostics;

namespace Nexus.Sources.Tests;

public class RemoteTestsFixture : IDisposable
{
    private Process? _buildProcess;

    private Process? _runProcess;

    private readonly SemaphoreSlim _semaphoreBuild = new(0, 1);

    private readonly SemaphoreSlim _semaphoreRun = new(0, 1);

    private bool _success;

    public RemoteTestsFixture()
    {
        Initialize = Task.Run(async () =>
        {
            /* Why not `dotnet run`? Because it spawns a child process for which
             * we do not know the process ID and so we cannot kill it.
             */

            // Build Nexus.Agent
            var psi_build = new ProcessStartInfo("bash")
            {
                /* Why `sleep infinity`? Because the test debugger seems to stop whenever a child process stops */
                Arguments = "-c \"dotnet build ../../../../src/Nexus.Agent/Nexus.Agent.csproj && sleep infinity\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _buildProcess = new Process
            {
                StartInfo = psi_build,
                EnableRaisingEvents = true
            };

            _buildProcess.OutputDataReceived += (sender, e) =>
            {
                if (e.Data is not null && e.Data.Contains("Build succeeded"))
                {
                    _success = true;
                    _semaphoreBuild.Release();
                }
            };

            _buildProcess.ErrorDataReceived += (sender, e) =>
            {
                _success = false;
                _semaphoreBuild.Release();
            };

            _buildProcess.Start();
            _buildProcess.BeginOutputReadLine();
            _buildProcess.BeginErrorReadLine();

            await _semaphoreBuild.WaitAsync(TimeSpan.FromMinutes(1));

            if (!_success)
                throw new Exception("Unable to build Nexus.Agent.");

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

            _runProcess = new Process
            {
                StartInfo = psi_run,
                EnableRaisingEvents = true
            };

            _runProcess.OutputDataReceived += (sender, e) =>
            {
                // File.AppendAllText("/home/vincent/Downloads/output.txt", e.Data + Environment.NewLine);

                if (e.Data is not null && e.Data.Contains("Now listening on"))
                {
                    _success = true;
                    _semaphoreRun.Release();
                }
            };

            _runProcess.ErrorDataReceived += (sender, e) =>
            {
                File.AppendAllText("/home/vincent/Downloads/error.txt", e.Data + Environment.NewLine);

                _success = false;
                _semaphoreRun.Release();
            };

            _runProcess.Start();
            _runProcess.BeginOutputReadLine();
            _runProcess.BeginErrorReadLine();

            await _semaphoreRun.WaitAsync(TimeSpan.FromMinutes(1));

            if (!_success)
                throw new Exception("Unable to launch Nexus.Agent.");
        });
    }

    public Task Initialize { get; }

    public void Dispose()
    {
        _buildProcess?.Kill();
        _runProcess?.Kill();
    }
}
