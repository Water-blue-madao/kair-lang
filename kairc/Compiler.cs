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
        // Validate input file exists
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

        // Asm IR → Internal IR → NASM
        var source = File.ReadAllText(_options.InputFile);
        var asmCode = CompileSourceToAsm(source);
        File.WriteAllText(_options.OutputFile, asmCode);

        Console.WriteLine("Done.");
    }

    private void CompileIrToExe()
    {
        Console.WriteLine($"Compiling {_options.InputFile} to {_options.OutputFile}...");

        // Step 1: IR -> ASM
        var tempAsmFile = Path.GetTempFileName() + ".asm";
        try
        {
            var source = File.ReadAllText(_options.InputFile);
            var asmCode = CompileSourceToAsm(source);
            File.WriteAllText(tempAsmFile, asmCode);

            // Step 2: ASM -> EXE
            AssembleToExe(tempAsmFile, _options.OutputFile);
        }
        finally
        {
            // Clean up temp file
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

        // Code Generator
        var codeGen = new NasmCodeGenerator(program, _options.EmitComments);
        return codeGen.Generate();
    }

    private void AssembleToExe(string asmFile, string exeFile)
    {
        Console.WriteLine($"Assembling {asmFile} to {exeFile}...");

        var objFile = Path.GetTempFileName() + ".obj";

        try
        {
            // Step 1: NASM -> OBJ
            AssemblerHelper.RunNasm(_options.NasmPath, asmFile, objFile);

            // Step 2: OBJ -> EXE
            LinkerHelper.RunGoLink(_options.GoLinkPath, objFile, exeFile);
        }
        finally
        {
            // Clean up temp obj file
            if (File.Exists(objFile))
                File.Delete(objFile);
        }
    }
}

