using System.Diagnostics;

namespace Kairc;

/// <summary>
/// 外部プロセス実行の共通ヘルパー
/// </summary>
public static class ProcessHelper
{
    /// <summary>
    /// 外部ツールを実行し、標準出力・エラーを処理する
    /// </summary>
    /// <param name="toolPath">実行可能ファイルのパス</param>
    /// <param name="arguments">コマンドライン引数</param>
    /// <param name="toolName">ツール名（エラーメッセージ用）</param>
    /// <param name="outputFile">生成されるべき出力ファイル（nullの場合はチェックしない）</param>
    public static void RunTool(string toolPath, string arguments, string toolName, string? outputFile = null)
    {
        if (!File.Exists(toolPath))
            throw new FileNotFoundException($"{toolName} not found at: {toolPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        Console.WriteLine($"Running: {toolPath} {arguments}");

        using var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException($"Failed to start {toolName} process");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(output))
            Console.WriteLine(output);

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(error))
                Console.Error.WriteLine(error);
            throw new InvalidOperationException($"{toolName} failed with exit code {process.ExitCode}");
        }

        if (outputFile != null && !File.Exists(outputFile))
            throw new InvalidOperationException($"{toolName} did not produce output file");
    }
}

