using Kairc.Lexer;
using Kairc.Parser;
using Kairc.CodeGen;

namespace Kairc;

public class Compiler
{
    private readonly CompilerOptions _options;

    public Compiler(CompilerOptions options)
    {
        _options = options;
    }

    public void Run()
    {
        // 入力ファイルの存在確認
        if (!File.Exists(_options.InputFile))
            throw new FileNotFoundException($"Input file not found: {_options.InputFile}");

        switch (_options.Mode)
        {
            case CompileMode.IrToAsm:
                CompileIrToAsm();
                break;

            case CompileMode.IrToExe:
                CompileIrToExe();
                break;

            case CompileMode.AsmToExe:
                AssembleToExe(_options.InputFile, _options.OutputFile);
                break;

            default:
                throw new InvalidOperationException($"Unknown compile mode: {_options.Mode}");
        }
    }

    private void CompileIrToAsm()
    {
        Console.WriteLine($"Compiling {_options.InputFile} to {_options.OutputFile}...");

        // KIR → 内部IR → LLVM MC Assembly
        var source = File.ReadAllText(_options.InputFile);
        var asmCode = CompileSourceToAsm(source);
        File.WriteAllText(_options.OutputFile, asmCode);

        Console.WriteLine("Done.");
    }

    private void CompileIrToExe()
    {
        Console.WriteLine($"Compiling {_options.InputFile} to {_options.OutputFile}...");

        // ステップ1: IR -> ASM
        var tempAsmFile = Path.GetTempFileName() + ".asm";
        try
        {
            var source = File.ReadAllText(_options.InputFile);
            var asmCode = CompileSourceToAsm(source);
            File.WriteAllText(tempAsmFile, asmCode);

            // ステップ2: ASM -> EXE
            AssembleToExe(tempAsmFile, _options.OutputFile);
        }
        finally
        {
            // 一時ファイルをクリーンアップ
            if (File.Exists(tempAsmFile))
                File.Delete(tempAsmFile);
        }

        Console.WriteLine("Done.");
    }

    private string CompileSourceToAsm(string source)
    {
        // Lexer
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize();

        // Parser（ソース行情報を渡す）
        var sourceLines = source.Split('\n');
        var parser = new Parser.Parser(tokens, sourceLines);
        var program = parser.Parse();

        // コード生成器 (LLVM MC構文)
        var codeGen = new LlvmCodeGenerator(program, _options.EmitComments);
        return codeGen.Generate();
    }

    private void AssembleToExe(string asmFile, string exeFile)
    {
        Console.WriteLine($"Assembling {asmFile} to {exeFile}...");

        var objFile = Path.GetTempFileName() + ".obj";

        try
        {
            // ステップ1: llvm-mc -> OBJ
            LlvmAssemblerHelper.RunLlvmMc(_options.LlvmMcPath, asmFile, objFile);

            // ステップ2: lld-link -> EXE
            LlvmLinkerHelper.RunLldLink(_options.LldLinkPath, objFile, exeFile, _options.Kernel32LibPath);
        }
        finally
        {
            // 一時OBJファイルをクリーンアップ
            if (File.Exists(objFile))
                File.Delete(objFile);
        }
    }
}

