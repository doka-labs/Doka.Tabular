using BenchmarkDotNet.Running;
using Doka.Tabular.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(ReferenceProfile).Assembly)
    .Run(args);
