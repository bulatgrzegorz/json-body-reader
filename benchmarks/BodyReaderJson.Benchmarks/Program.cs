using BenchmarkDotNet.Running;
using BodyReaderJson.Benchmarks;

Directory.SetCurrentDirectory(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../..")));
if (args is ["--allocations"])
{
    Environment.ExitCode = AllocationProbe.Run();
    return;
}
if (args is ["--strings"])
{
    Environment.ExitCode = StringDecodingProbe.Run();
    return;
}
if (args is ["--lookup"])
{
    LookupBenchmarks.Probe();
    return;
}
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
