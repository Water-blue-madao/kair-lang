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
    public string NasmPath { get; set; } = "tools/nasm/nasm.exe";
    public string GoLinkPath { get; set; } = "tools/golink/GoLink.exe";
    public bool EmitComments { get; set; } = false;
}

public static class CommandLineParser
{
    public static CompilerOptions Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("No input file specified");

        string? inputFile = null;
        string? outputFile = null;
        string? targetStr = null;
        CompileMode? mode = null;
        string nasmPath = "tools/nasm/nasm.exe";
        string golinkPath = "tools/golink/GoLink.exe";
        bool emitComments = false;

        // Detect subcommand: "build" means exe, no subcommand means asm
        bool isBuildMode = (args[0] == "build");
        int startIndex = isBuildMode ? 1 : 0;

        // Set default mode based on subcommand
        if (isBuildMode)
        {
            mode = CompileMode.IrToExe;  // build → exe
        }
        else
        {
            mode = CompileMode.IrToAsm;  // no subcommand → asm
        }

        for (int i = startIndex; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                case "--output":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("Missing output file after -o");
                    outputFile = args[++i];
                    break;

                // These options can override the default mode
                case "--ir-to-asm":
                    mode = CompileMode.IrToAsm;
                    break;

                case "--ir-to-exe":
                    mode = CompileMode.IrToExe;
                    break;

                case "--asm-to-exe":
                    mode = CompileMode.AsmToExe;
                    break;

                case "--nasm":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("Missing path after --nasm");
                    nasmPath = args[++i];
                    break;

                case "--golink":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("Missing path after --golink");
                    golinkPath = args[++i];
                    break;

                case "--emit-comments":
                    emitComments = true;
                    break;

                default:
                    if (args[i].StartsWith("-"))
                        throw new ArgumentException($"Unknown option: {args[i]}");

                    // First positional arg is input file
                    if (inputFile == null)
                    {
                        inputFile = args[i];
                    }
                    // Second positional arg could be target (x64-win, arm64-linux, etc.)
                    else if (targetStr == null && IsTargetString(args[i]))
                    {
                        targetStr = args[i];
                    }
                    else
                    {
                        throw new ArgumentException($"Unexpected argument: {args[i]}");
                    }
                    break;
            }
        }

        if (inputFile == null)
            throw new ArgumentException("No input file specified");

        // Mode is already set based on subcommand (build or not)
        // But handle .asm files specially
        var ext = Path.GetExtension(inputFile).ToLowerInvariant();
        if (ext == ".asm" && mode == CompileMode.IrToAsm)
        {
            // If input is .asm and mode is IrToAsm, switch to AsmToExe
            mode = CompileMode.AsmToExe;
        }

        // Auto-generate output file if not specified
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

        // Parse target if specified
        TargetPlatform? target = targetStr != null ? ParseTarget(targetStr) : null;

        return new CompilerOptions
        {
            InputFile = inputFile,
            OutputFile = outputFile,
            Mode = mode.Value,
            Target = target,
            NasmPath = nasmPath,
            GoLinkPath = golinkPath,
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
            _ => throw new ArgumentException($"Unknown target platform: {target}")
        };
    }

    private static string GetExecutableExtension(string? targetStr)
    {
        if (targetStr == null)
        {
            // Default to current OS
            return OperatingSystem.IsWindows() ? ".exe" : "";
        }

        var lower = targetStr.ToLowerInvariant();
        return lower.Contains("win") ? ".exe" : "";
    }
}

