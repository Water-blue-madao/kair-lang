namespace Kairc;

public enum CompileMode
{
    IrToAsm,
    IrToExe,
    AsmToExe
}

public enum TargetPlatform
{
    X64Win,
    X64Linux,
    Arm64Linux,
    Arm64Mac
}

public class CompilerOptions
{
    public required string InputFile { get; set; }
    public required string OutputFile { get; set; }
    public CompileMode Mode { get; set; }
    public TargetPlatform? Target { get; set; }
    public string LlvmMcPath { get; set; } = "tools/llvm/bin/llvm-mc.exe";
    public string LldLinkPath { get; set; } = "tools/llvm/bin/lld-link.exe";
    public string Kernel32LibPath { get; set; } = "tools/llvm/lib/kernel32.lib";
    public bool EmitComments { get; set; } = false;
}

public static class CommandLineParser
{
    public static CompilerOptions Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("入力ファイルが指定されていません");

        string? inputFile = null;
        string? outputFile = null;
        string? targetStr = null;
        CompileMode? mode = null;
        string llvmMcPath = "tools/llvm/bin/llvm-mc.exe";
        string lldLinkPath = "tools/llvm/bin/lld-link.exe";
        string kernel32LibPath = "tools/llvm/lib/kernel32.lib";
        bool emitComments = false;

        // サブコマンドを判定: "build" は実行ファイル、サブコマンドなしはアセンブリ出力
        bool isBuildMode = (args[0] == "build");
        int startIndex = isBuildMode ? 1 : 0;

        // サブコマンドに基づいてデフォルトモードを設定
        if (isBuildMode)
        {
            mode = CompileMode.IrToExe;  // build → 実行ファイル
        }
        else
        {
            mode = CompileMode.IrToAsm;  // サブコマンドなし → アセンブリ出力
        }

        for (int i = startIndex; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                case "--output":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("-o の後に出力ファイルが指定されていません");
                    outputFile = args[++i];
                    break;

                // これらのオプションはデフォルトモードを上書きできる
                case "--ir-to-asm":
                    mode = CompileMode.IrToAsm;
                    break;

                case "--ir-to-exe":
                    mode = CompileMode.IrToExe;
                    break;

                case "--asm-to-exe":
                    mode = CompileMode.AsmToExe;
                    break;

                case "--llvm-mc":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--llvm-mc の後にパスが指定されていません");
                    llvmMcPath = args[++i];
                    break;

                case "--lld-link":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--lld-link の後にパスが指定されていません");
                    lldLinkPath = args[++i];
                    break;

                case "--kernel32-lib":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--kernel32-lib の後にパスが指定されていません");
                    kernel32LibPath = args[++i];
                    break;

                case "--emit-comments":
                    emitComments = true;
                    break;

                default:
                    if (args[i].StartsWith("-"))
                        throw new ArgumentException($"不明なオプション: {args[i]}");

                    // 最初の位置引数は入力ファイル
                    if (inputFile == null)
                    {
                        inputFile = args[i];
                    }
                    // 2番目の位置引数はターゲット (x64-win, arm64-linux など) になり得る
                    else if (targetStr == null && IsTargetString(args[i]))
                    {
                        targetStr = args[i];
                    }
                    else
                    {
                        throw new ArgumentException($"予期しない引数: {args[i]}");
                    }
                    break;
            }
        }

        if (inputFile == null)
            throw new ArgumentException("入力ファイルが指定されていません");

        // モードはサブコマンドの有無に基づいて既に設定済み
        // ただし .asm ファイルは特別扱いする
        var ext = Path.GetExtension(inputFile).ToLowerInvariant();
        if (ext == ".asm" && mode == CompileMode.IrToAsm)
        {
            // 入力が .asm かつモードが IrToAsm の場合、AsmToExe に切り替える
            mode = CompileMode.AsmToExe;
        }

        // 出力ファイルが指定されていない場合は自動生成
        if (outputFile == null)
        {
            var baseName = Path.GetFileNameWithoutExtension(inputFile);
            outputFile = mode switch
            {
                CompileMode.IrToAsm => baseName + ".asm",
                CompileMode.IrToExe => baseName + GetExecutableExtension(targetStr),
                CompileMode.AsmToExe => baseName + GetExecutableExtension(targetStr),
                _ => baseName + ".exe"
            };
        }

        // ターゲットが指定されていれば解析
        TargetPlatform? target = targetStr != null ? ParseTarget(targetStr) : null;

        return new CompilerOptions
        {
            InputFile = inputFile,
            OutputFile = outputFile,
            Mode = mode.Value,
            Target = target,
            LlvmMcPath = llvmMcPath,
            LldLinkPath = lldLinkPath,
            Kernel32LibPath = kernel32LibPath,
            EmitComments = emitComments
        };
    }

    private static bool IsTargetString(string arg)
    {
        return arg.Contains('-') && (
            arg.Contains("x64") || arg.Contains("x86") ||
            arg.Contains("arm") || arg.Contains("arm64") ||
            arg.Contains("win") || arg.Contains("linux") ||
            arg.Contains("mac") || arg.Contains("macos")
        );
    }

    private static TargetPlatform ParseTarget(string target)
    {
        return target.ToLowerInvariant() switch
        {
            "x64-win" or "win-x64" or "x64-windows" or "windows-x64" => TargetPlatform.X64Win,
            "x64-linux" or "linux-x64" => TargetPlatform.X64Linux,
            "arm64-linux" or "linux-arm64" => TargetPlatform.Arm64Linux,
            "arm64-mac" or "mac-arm64" or "arm64-macos" or "macos-arm64" => TargetPlatform.Arm64Mac,
            _ => throw new ArgumentException($"不明なターゲットプラットフォーム: {target}")
        };
    }

    private static string GetExecutableExtension(string? targetStr)
    {
        if (targetStr == null)
        {
            // デフォルトは現在のOS
            return OperatingSystem.IsWindows() ? ".exe" : "";
        }

        var lower = targetStr.ToLowerInvariant();
        return lower.Contains("win") ? ".exe" : "";
    }
}

