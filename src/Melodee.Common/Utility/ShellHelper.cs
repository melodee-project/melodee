using System.Diagnostics;

namespace Melodee.Common.Utility;

// WARNING: This class executes shell commands and may pose a security risk if used with untrusted input.
// Only use with paths and commands from trusted configuration sources.
// The current escape mechanism only handles double quotes and does NOT protect against all shell injection vectors
// such as backticks, semicolons, pipe operators, or other shell metacharacters.
// Consider validating that script paths are within expected directories and using allowlists for acceptable scripts.
public static class ShellHelper
{
    public static async Task<int> Bash(this string cmd)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(cmd);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Command `{cmd}` failed to start");
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);

            Trace.WriteLine(stderr, "Warning");
            Trace.WriteLine(stdout, "Information");
            if (process.ExitCode == 0)
            {
                return 0;
            }

            throw new Exception($"Command `{cmd}` failed with exit code `{process.ExitCode}`");
        }
        catch (Exception e)
        {
            Trace.WriteLine($"Command Line [{cmd}] Failed Error [{e}", "Error");
            throw;
        }
        finally
        {
            process.Dispose();
        }
    }
}
