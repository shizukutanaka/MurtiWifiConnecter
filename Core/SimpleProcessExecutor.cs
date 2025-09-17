using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// Simple process executor for netsh commands
    /// </summary>
    public interface IProcessExecutor
    {
        Task<ProcessResult> RunAsync(string fileName, string arguments, int timeoutMs);
    }

    public sealed class ProcessExecutor : IProcessExecutor
    {
        public async Task<ProcessResult> RunAsync(string fileName, string arguments, int timeoutMs)
        {
            var result = new ProcessResult();

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        outputBuilder.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completed = await Task.Run(() => process.WaitForExit(timeoutMs));

                if (!completed)
                {
                    try { process.Kill(); } catch { }
                    result.Success = false;
                    result.Error = "Process timed out";
                    return result;
                }

                result.ExitCode = process.ExitCode;
                result.Output = outputBuilder.ToString();
                result.Error = errorBuilder.ToString();
                result.Success = process.ExitCode == 0;

                // Some netsh commands return non-zero even on success
                if (!result.Success && string.IsNullOrEmpty(result.Error) && !string.IsNullOrEmpty(result.Output))
                {
                    // Check for success indicators in output
                    var output = result.Output.ToLowerInvariant();
                    if (output.Contains("successfully") ||
                        output.Contains("completed") ||
                        output.Contains("connected") ||
                        (output.Contains("profile") && output.Contains("added")))
                    {
                        result.Success = true;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                return result;
            }
        }
    }
}