using BenchmarkDotNet.Running;
using MWC.Benchmarks;

// Entry point documented in MwcBenchmarks.cs (dotnet run -c Release).
// The switcher lets the operator choose which benchmark class to run.
BenchmarkSwitcher.FromAssembly(typeof(ProfileXmlBuilderBenchmarks).Assembly).Run(args);
