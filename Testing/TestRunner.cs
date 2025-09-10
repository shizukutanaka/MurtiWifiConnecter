using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Testing
{
    public interface ITestRunner
    {
        Task<TestResults> RunAllTestsAsync(CancellationToken cancellationToken = default);
        Task<TestResults> RunTestSuiteAsync(string suiteName, CancellationToken cancellationToken = default);
        Task<TestResults> RunTestsAsync(IEnumerable<Type> testClasses, CancellationToken cancellationToken = default);
        Task<TestResult> RunSingleTestAsync(Type testClass, string methodName, CancellationToken cancellationToken = default);
        List<TestSuite> GetAvailableTestSuites();
        TestResults GetLastResults();
        event EventHandler<TestProgressEventArgs> TestProgress;
        event EventHandler<TestCompletedEventArgs> TestCompleted;
    }

    public class TestRunner : ITestRunner, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ITelemetryService _telemetryService;
        private readonly IServiceContainer _serviceContainer;
        private readonly Dictionary<string, TestSuite> _testSuites;
        private readonly SemaphoreSlim _executionSemaphore;
        private TestResults _lastResults;

        public event EventHandler<TestProgressEventArgs> TestProgress;
        public event EventHandler<TestCompletedEventArgs> TestCompleted;

        public TestRunner(ILoggingService logger, ITelemetryService telemetryService, IServiceContainer serviceContainer)
        {
            _logger = logger;
            _telemetryService = telemetryService;
            _serviceContainer = serviceContainer;
            _testSuites = new Dictionary<string, TestSuite>();
            _executionSemaphore = new SemaphoreSlim(1, 1);

            DiscoverTestSuites();
        }

        public async Task<TestResults> RunAllTestsAsync(CancellationToken cancellationToken = default)
        {
            await _executionSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                _logger.LogInfo("Starting full test run");
                var startTime = DateTime.UtcNow;

                var results = new TestResults
                {
                    StartTime = startTime,
                    TotalTests = _testSuites.Values.Sum(s => s.TestMethods.Count)
                };

                var testClasses = _testSuites.Values
                    .SelectMany(s => s.TestMethods)
                    .Select(m => m.DeclaringType)
                    .Distinct()
                    .ToList();

                foreach (var testClass in testClasses)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var classResults = await RunTestClassAsync(testClass, cancellationToken);
                    results.TestResults.AddRange(classResults.TestResults);
                    results.PassedTests += classResults.PassedTests;
                    results.FailedTests += classResults.FailedTests;
                    results.SkippedTests += classResults.SkippedTests;

                    TestProgress?.Invoke(this, new TestProgressEventArgs
                    {
                        CurrentTest = results.TestResults.Count,
                        TotalTests = results.TotalTests,
                        CurrentTestName = $"{testClass.Name} completed"
                    });
                }

                results.EndTime = DateTime.UtcNow;
                results.Duration = results.EndTime - results.StartTime;
                results.IsSuccess = results.FailedTests == 0;

                _lastResults = results;

                _telemetryService.TrackEvent("TestRun.Completed", new Dictionary<string, string>
                {
                    ["TotalTests"] = results.TotalTests.ToString(),
                    ["PassedTests"] = results.PassedTests.ToString(),
                    ["FailedTests"] = results.FailedTests.ToString(),
                    ["Duration"] = results.Duration.TotalSeconds.ToString(),
                    ["Success"] = results.IsSuccess.ToString()
                });

                _logger.LogInfo($"Test run completed: {results.PassedTests}/{results.TotalTests} passed in {results.Duration.TotalSeconds:F1}s");

                TestCompleted?.Invoke(this, new TestCompletedEventArgs { Results = results });
                return results;
            }
            finally
            {
                _executionSemaphore.Release();
            }
        }

        public async Task<TestResults> RunTestSuiteAsync(string suiteName, CancellationToken cancellationToken = default)
        {
            if (!_testSuites.TryGetValue(suiteName, out var suite))
                throw new ArgumentException($"Test suite '{suiteName}' not found", nameof(suiteName));

            var testClasses = suite.TestMethods.Select(m => m.DeclaringType).Distinct().ToList();
            return await RunTestsAsync(testClasses, cancellationToken);
        }

        public async Task<TestResults> RunTestsAsync(IEnumerable<Type> testClasses, CancellationToken cancellationToken = default)
        {
            var results = new TestResults
            {
                StartTime = DateTime.UtcNow
            };

            foreach (var testClass in testClasses)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var classResults = await RunTestClassAsync(testClass, cancellationToken);
                results.TestResults.AddRange(classResults.TestResults);
                results.PassedTests += classResults.PassedTests;
                results.FailedTests += classResults.FailedTests;
                results.SkippedTests += classResults.SkippedTests;
            }

            results.EndTime = DateTime.UtcNow;
            results.Duration = results.EndTime - results.StartTime;
            results.TotalTests = results.TestResults.Count;
            results.IsSuccess = results.FailedTests == 0;

            return results;
        }

        public async Task<TestResult> RunSingleTestAsync(Type testClass, string methodName, CancellationToken cancellationToken = default)
        {
            var method = testClass.GetMethod(methodName);
            if (method == null)
                throw new ArgumentException($"Test method '{methodName}' not found in class '{testClass.Name}'");

            return await ExecuteTestMethodAsync(testClass, method, cancellationToken);
        }

        private async Task<TestResults> RunTestClassAsync(Type testClass, CancellationToken cancellationToken)
        {
            var results = new TestResults { StartTime = DateTime.UtcNow };

            try
            {
                var testMethods = GetTestMethods(testClass);
                var instance = CreateTestInstance(testClass);

                await ExecuteSetupMethod(instance, testClass);

                foreach (var method in testMethods)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var result = await ExecuteTestMethodAsync(testClass, method, cancellationToken, instance);
                    results.TestResults.Add(result);

                    if (result.Status == TestStatus.Passed)
                        results.PassedTests++;
                    else if (result.Status == TestStatus.Failed)
                        results.FailedTests++;
                    else
                        results.SkippedTests++;
                }

                await ExecuteTeardownMethod(instance, testClass);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error running test class {testClass.Name}", ex);
                
                results.TestResults.Add(new TestResult
                {
                    TestClass = testClass.Name,
                    TestMethod = "ClassSetup",
                    Status = TestStatus.Failed,
                    ErrorMessage = ex.Message,
                    Duration = TimeSpan.Zero
                });
                results.FailedTests++;
            }

            results.EndTime = DateTime.UtcNow;
            results.Duration = results.EndTime - results.StartTime;
            results.TotalTests = results.TestResults.Count;

            return results;
        }

        private async Task<TestResult> ExecuteTestMethodAsync(Type testClass, MethodInfo method, CancellationToken cancellationToken, object instance = null)
        {
            var result = new TestResult
            {
                TestClass = testClass.Name,
                TestMethod = method.Name,
                StartTime = DateTime.UtcNow
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                instance ??= CreateTestInstance(testClass);
                
                if (HasSkipAttribute(method))
                {
                    result.Status = TestStatus.Skipped;
                    result.SkipReason = GetSkipReason(method);
                    return result;
                }

                await ExecuteBeforeTestMethod(instance, testClass);

                if (IsAsyncMethod(method))
                {
                    var task = (Task)method.Invoke(instance, null);
                    await task;
                }
                else
                {
                    method.Invoke(instance, null);
                }

                await ExecuteAfterTestMethod(instance, testClass);

                result.Status = TestStatus.Passed;
            }
            catch (Exception ex)
            {
                result.Status = TestStatus.Failed;
                result.ErrorMessage = ex.InnerException?.Message ?? ex.Message;
                result.StackTrace = ex.InnerException?.StackTrace ?? ex.StackTrace;

                _logger.LogWarning($"Test failed: {testClass.Name}.{method.Name} - {result.ErrorMessage}");
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                result.EndTime = DateTime.UtcNow;
            }

            return result;
        }

        private void DiscoverTestSuites()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            foreach (var assembly in assemblies)
            {
                try
                {
                    var testClasses = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract && HasTestMethods(t))
                        .ToList();

                    foreach (var testClass in testClasses)
                    {
                        var suiteAttribute = testClass.GetCustomAttribute<TestSuiteAttribute>();
                        var suiteName = suiteAttribute?.Name ?? "Default";

                        if (!_testSuites.TryGetValue(suiteName, out var suite))
                        {
                            suite = new TestSuite
                            {
                                Name = suiteName,
                                Description = suiteAttribute?.Description ?? $"Test suite for {suiteName}",
                                TestMethods = new List<MethodInfo>()
                            };
                            _testSuites[suiteName] = suite;
                        }

                        var testMethods = GetTestMethods(testClass);
                        suite.TestMethods.AddRange(testMethods);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error discovering tests in assembly {assembly.FullName}", ex);
                }
            }

            _logger.LogInfo($"Discovered {_testSuites.Count} test suites with {_testSuites.Values.Sum(s => s.TestMethods.Count)} tests");
        }

        private List<MethodInfo> GetTestMethods(Type testClass)
        {
            return testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null)
                .ToList();
        }

        private bool HasTestMethods(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(m => m.GetCustomAttribute<TestMethodAttribute>() != null);
        }

        private object CreateTestInstance(Type testClass)
        {
            try
            {
                return _serviceContainer.CreateInstance(testClass);
            }
            catch
            {
                return Activator.CreateInstance(testClass);
            }
        }

        private async Task ExecuteSetupMethod(object instance, Type testClass)
        {
            var setupMethod = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.GetCustomAttribute<SetupAttribute>() != null);

            if (setupMethod != null)
            {
                if (IsAsyncMethod(setupMethod))
                {
                    var task = (Task)setupMethod.Invoke(instance, null);
                    await task;
                }
                else
                {
                    setupMethod.Invoke(instance, null);
                }
            }
        }

        private async Task ExecuteTeardownMethod(object instance, Type testClass)
        {
            var teardownMethod = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.GetCustomAttribute<TeardownAttribute>() != null);

            if (teardownMethod != null)
            {
                if (IsAsyncMethod(teardownMethod))
                {
                    var task = (Task)teardownMethod.Invoke(instance, null);
                    await task;
                }
                else
                {
                    teardownMethod.Invoke(instance, null);
                }
            }
        }

        private async Task ExecuteBeforeTestMethod(object instance, Type testClass)
        {
            var beforeMethod = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.GetCustomAttribute<BeforeTestAttribute>() != null);

            if (beforeMethod != null)
            {
                if (IsAsyncMethod(beforeMethod))
                {
                    var task = (Task)beforeMethod.Invoke(instance, null);
                    await task;
                }
                else
                {
                    beforeMethod.Invoke(instance, null);
                }
            }
        }

        private async Task ExecuteAfterTestMethod(object instance, Type testClass)
        {
            var afterMethod = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.GetCustomAttribute<AfterTestAttribute>() != null);

            if (afterMethod != null)
            {
                if (IsAsyncMethod(afterMethod))
                {
                    var task = (Task)afterMethod.Invoke(instance, null);
                    await task;
                }
                else
                {
                    afterMethod.Invoke(instance, null);
                }
            }
        }

        private bool IsAsyncMethod(MethodInfo method)
        {
            return method.ReturnType == typeof(Task) || method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);
        }

        private bool HasSkipAttribute(MethodInfo method)
        {
            return method.GetCustomAttribute<SkipAttribute>() != null;
        }

        private string GetSkipReason(MethodInfo method)
        {
            var skipAttribute = method.GetCustomAttribute<SkipAttribute>();
            return skipAttribute?.Reason ?? "Test skipped";
        }

        public List<TestSuite> GetAvailableTestSuites()
        {
            return _testSuites.Values.ToList();
        }

        public TestResults GetLastResults()
        {
            return _lastResults;
        }

        public void Dispose()
        {
            _executionSemaphore?.Dispose();
        }
    }

    // Test attributes
    [AttributeUsage(AttributeTargets.Class)]
    public class TestSuiteAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; }

        public TestSuiteAttribute(string name)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute
    {
        public string Description { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class SetupAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class TeardownAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class BeforeTestAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class AfterTestAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class SkipAttribute : Attribute
    {
        public string Reason { get; set; }

        public SkipAttribute(string reason = null)
        {
            Reason = reason;
        }
    }

    // Test data models
    public class TestSuite
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<MethodInfo> TestMethods { get; set; } = new List<MethodInfo>();
    }

    public class TestResults
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public int SkippedTests { get; set; }
        public bool IsSuccess { get; set; }
        public List<TestResult> TestResults { get; set; } = new List<TestResult>();

        public double PassRate => TotalTests > 0 ? (double)PassedTests / TotalTests * 100 : 0;
    }

    public class TestResult
    {
        public string TestClass { get; set; }
        public string TestMethod { get; set; }
        public TestStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string ErrorMessage { get; set; }
        public string StackTrace { get; set; }
        public string SkipReason { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public enum TestStatus
    {
        Passed,
        Failed,
        Skipped
    }

    // Event args
    public class TestProgressEventArgs : EventArgs
    {
        public int CurrentTest { get; set; }
        public int TotalTests { get; set; }
        public string CurrentTestName { get; set; }
        public double ProgressPercentage => TotalTests > 0 ? (double)CurrentTest / TotalTests * 100 : 0;
    }

    public class TestCompletedEventArgs : EventArgs
    {
        public TestResults Results { get; set; }
    }
}