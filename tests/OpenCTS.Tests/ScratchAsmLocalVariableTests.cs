using System.Text.Json;
using OpenCTS.Core;

namespace OpenCTS.Tests;

[TestClass]
public sealed class ScratchAsmLocalVariableTests
{
    [TestMethod]
    public void LowersReentrantLocalsToWrapperFrameListsAndStandardScratchBlocks()
    {
        const string source = """
stage {
  proc countdown(n: num):
    local var accumulator = n
    accumulator += 1
    control.wait 0.01
    if accumulator > 0:
      call countdown(accumulator - 1)
  @greenflag:
    call countdown(2)
}
""";

        CtsCompileResult result = CtsCompiler.Compile(source, "locals.sasm");

        AssertNoErrors(result.Diagnostics);
        using JsonDocument document = JsonDocument.Parse(result.ProjectJsonBytes);
        JsonElement stage = GetStage(document.RootElement);
        string[] variableNames = stage.GetProperty("variables").EnumerateObject()
            .Select(property => property.Value[0].GetString()!)
            .ToArray();
        string[] listNames = stage.GetProperty("lists").EnumerateObject()
            .Select(property => property.Value[0].GetString()!)
            .ToArray();
        Assert.IsTrue(variableNames.Any(name => name.Contains("frame", StringComparison.Ordinal)));
        Assert.IsGreaterThanOrEqualTo(2, listNames.Count(name => name.Contains("countdown", StringComparison.Ordinal)));

        JsonElement blocks = stage.GetProperty("blocks");
        string[] opcodes = blocks.EnumerateObject()
            .Select(property => property.Value.GetProperty("opcode").GetString()!)
            .ToArray();
        Assert.IsGreaterThanOrEqualTo(2, opcodes.Count(opcode => opcode == "procedures_definition"));
        CollectionAssert.Contains(opcodes, "data_changevariableby");
        CollectionAssert.Contains(opcodes, "data_addtolist");
        CollectionAssert.Contains(opcodes, "data_itemnumoflist");
        CollectionAssert.Contains(opcodes, "data_itemoflist");
        CollectionAssert.Contains(opcodes, "data_replaceitemoflist");
        CollectionAssert.Contains(opcodes, "data_deleteoflist");
        Assert.IsFalse(opcodes.Any(opcode => opcode.StartsWith("scratchasm_", StringComparison.Ordinal)));

        string[] proccodes = blocks.EnumerateObject()
            .Where(property => property.Value.GetProperty("opcode").GetString() is "procedures_prototype" or "procedures_call")
            .Select(property => property.Value.GetProperty("mutation").GetProperty("proccode").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.IsTrue(proccodes.Contains("countdown %n", StringComparer.Ordinal));
        Assert.IsTrue(proccodes.Any(code => code.StartsWith("__sasm_", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LocalDeclarationsMustBeContiguousAtProcedureStart()
    {
        const string source = """
stage {
  proc invalid():
    looks.say "started"
    local var late = 1
}
""";

        CtsCompileResult result = CtsCompiler.Compile(source, "late-local.sasm");

        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.Message.Contains("start", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void LocalProcedureRejectsTerminalRawAndUnknownFlowBlocks()
    {
        const string terminal = """
stage {
  proc unsafe():
    local var value = 0
    forever:
      value += 1
}
""";
        const string raw = """
stage {
  proc unsafe():
    local var value = 0
    % "motion_movesteps" input STEPS=10
}
""";
        const string generic = """
stage {
  proc unsafe():
    local var value = 0
    block "motion_movesteps" input STEPS=10
}
""";

        AssertUnsafe(CtsCompiler.Compile(terminal, "terminal.sasm"), "terminal");
        AssertUnsafe(CtsCompiler.Compile(raw, "raw.sasm"), "raw");
        AssertUnsafe(CtsCompiler.Compile(generic, "generic.sasm"), "flow");
    }

    [TestMethod]
    public void LocalProcedureRejectsUnsafeTransitiveCalls()
    {
        const string source = """
stage {
  proc stopNow():
    control.stop "this script"
  proc caller():
    call stopNow()
  proc localOwner():
    local var value = 0
    call caller()
}
""";

        CtsCompileResult result = CtsCompiler.Compile(source, "transitive.sasm");

        AssertUnsafe(result, "transitive");
    }

    [TestMethod]
    public void LocalLoweringIsDeterministicAndDoesNotReuseFrameIds()
    {
        const string source = """
stage {
  var __sasm_frame_next = 99
  list __sasm_work_frames = []
  proc work():
    local var value = 1
    value += 1
  @greenflag:
    call work()
    call work()
}
""";

        CtsCompileResult first = CtsCompiler.Compile(source, "frames.sasm");
        CtsCompileResult second = CtsCompiler.Compile(source, "frames.sasm");

        AssertNoErrors(first.Diagnostics);
        CollectionAssert.AreEqual(first.ProjectJsonBytes, second.ProjectJsonBytes);
        using JsonDocument document = JsonDocument.Parse(first.ProjectJsonBytes);
        JsonElement stage = GetStage(document.RootElement);
        Assert.IsGreaterThanOrEqualTo(2, stage.GetProperty("variables").EnumerateObject().Count());
        Assert.IsGreaterThanOrEqualTo(3, stage.GetProperty("lists").EnumerateObject().Count());

        JsonElement blocks = stage.GetProperty("blocks");
        JsonElement frameIncrement = blocks.EnumerateObject()
            .Select(property => property.Value)
            .Single(block => block.GetProperty("opcode").GetString() == "data_changevariableby");
        Assert.IsTrue(frameIncrement.GetProperty("fields").GetProperty("VARIABLE")[0].GetString()!
            .Contains("frame", StringComparison.Ordinal));
    }

    private static void AssertUnsafe(CtsCompileResult result, string expectedText)
    {
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.Message.Contains(expectedText, StringComparison.OrdinalIgnoreCase)),
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    private static void AssertNoErrors(IEnumerable<CtsDiagnostic> diagnostics)
    {
        CtsDiagnostic[] errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
    }

    private static JsonElement GetStage(JsonElement root)
    {
        return root.GetProperty("targets").EnumerateArray().Single(target => target.GetProperty("isStage").GetBoolean());
    }
}
