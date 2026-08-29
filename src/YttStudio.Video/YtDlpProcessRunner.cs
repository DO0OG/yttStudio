using System.Diagnostics;
using System.Text;

namespace YttStudio.Video;

internal sealed class YtDlpProcessRunner : IYtDlpProcessRunner
{
    private const int MaxStandardOutputCharacters = 1_048_576;
    private const int MaxStandardErrorCharacters = 64 * 1024;

    public async Task<YtDlpProcessResult> RunAsync(
        string executablePath,
        Uri uri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using Process process = CreateProcess(executablePath, uri);
        if (!process.Start())
        {
            throw new InvalidOperationException("yt-dlp 프로세스를 시작하지 못했습니다.");
        }

        Task<string> standardOutput = ReadOutputAsync(process.StandardOutput, MaxStandardOutputCharacters);
        Task<string> standardError = ReadOutputAsync(process.StandardError, MaxStandardErrorCharacters);
        using CancellationTokenSource timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Terminate(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            return new YtDlpProcessResult(-1, standardOutput.Result, standardError.Result, true);
        }
        catch (OperationCanceledException)
        {
            Terminate(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        return new YtDlpProcessResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result,
            false);
    }

    private static Process CreateProcess(string executablePath, Uri uri)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--no-download");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-progress");
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("--simulate");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(uri.AbsoluteUri);
        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, int maximumCharacters)
    {
        char[] buffer = new char[4096];
        StringBuilder output = new();
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length < maximumCharacters)
            {
                output.Append(buffer, 0, Math.Min(read, maximumCharacters - output.Length));
            }
        }

        return output.ToString();
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 프로세스가 종료되는 순간의 경쟁은 정상적인 취소 경로다.
        }
    }
}
