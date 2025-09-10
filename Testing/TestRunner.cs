using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Testing
{
    /// <summary>
    /// テストランナー
    /// </summary>
    public class TestRunner
    {
        private readonly List<TestResult> _results;
        private readonly Stopwatch _stopwatch;

        public TestRunner()
        {
            _results = new List<TestResult>();
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// 指定されたアセンブリ内のすべてのテストを実行
        /// </summary>
        public async Task<TestRunSummary> RunAllTestsAsync(Assembly assembly)
        {
            var testClasses = assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null)
                .ToList();

            _stopwatch.Start();

            foreach (var testClass in testClasses)
            {
                await RunTestClassAsync(testClass);
            }

            _stopwatch.Stop();

            return GenerateSummary();
        }

        /// <summary>
        /// 特定のテストクラスを実行
        /// </summary>
        public async Task<TestRunSummary> RunTestClassAsync(Type testClass)
        {
            var testMethods = testClass.GetMethods()
                .Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null)
                .ToList();

            var instance = Activator.CreateInstance(testClass);

            // Setup実行
            var setupMethod = testClass.GetMethod("Setup");
            setupMethod?.Invoke(instance, null);

            foreach (var method in testMethods)
            {
                await RunTestMethodAsync(instance, method);
            }

            // Teardown実行
            var teardownMethod = testClass.GetMethod("Teardown");
            teardownMethod?.Invoke(instance, null);

            return GenerateSummary();
        }

        /// <summary>
        /// 個別のテストメソッドを実行
        /// </summary>
        private async Task RunTestMethodAsync(object instance, MethodInfo method)
        {
            var result = new TestResult
            {
                TestName = $"{instance.GetType().Name}.{method.Name}",
                StartTime = DateTime.Now
            };

            var methodStopwatch = Stopwatch.StartNew();

            try
            {
                if (method.ReturnType == typeof(Task))
                {
                    await (Task)method.Invoke(instance, null);
                }
                else
                {
                    method.Invoke(instance, null);
                }

                result.Success = true;
                result.Message = "Test passed";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Exception = ex.InnerException ?? ex;
                result.Message = result.Exception.Message;
            }
            finally
            {
                methodStopwatch.Stop();
                result.Duration = methodStopwatch.Elapsed;
                result.EndTime = DateTime.Now;
                _results.Add(result);
            }
        }

        /// <summary>
        /// テスト実行サマリーを生成
        /// </summary>
        private TestRunSummary GenerateSummary()
        {
            return new TestRunSummary
            {
                TotalTests = _results.Count,
                PassedTests = _results.Count(r => r.Success),
                FailedTests = _results.Count(r => !r.Success),
                TotalDuration = _stopwatch.Elapsed,
                Results = _results.ToList(),
                SuccessRate = _results.Count > 0 
                    ? (double)_results.Count(r => r.Success) / _results.Count * 100 
                    : 0
            };
        }
    }

    /// <summary>
    /// テスト結果
    /// </summary>
    public class TestResult
    {
        public string TestName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// テスト実行サマリー
    /// </summary>
    public class TestRunSummary
    {
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public List<TestResult> Results { get; set; }
    }

    /// <summary>
    /// テストクラス属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute
    {
        public string Description { get; set; }
    }

    /// <summary>
    /// テストメソッド属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute
    {
        public string Description { get; set; }
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// テストアサーション
    /// </summary>
    public static class Assert
    {
        public static void IsTrue(bool condition, string message = null)
        {
            if (!condition)
                throw new AssertionException(message ?? "Expected condition to be true");
        }

        public static void IsFalse(bool condition, string message = null)
        {
            if (condition)
                throw new AssertionException(message ?? "Expected condition to be false");
        }

        public static void AreEqual<T>(T expected, T actual, string message = null)
        {
            if (!Equals(expected, actual))
                throw new AssertionException(message ?? $"Expected {expected} but got {actual}");
        }

        public static void AreNotEqual<T>(T expected, T actual, string message = null)
        {
            if (Equals(expected, actual))
                throw new AssertionException(message ?? $"Expected values to be different but both were {expected}");
        }

        public static void IsNull(object obj, string message = null)
        {
            if (obj != null)
                throw new AssertionException(message ?? "Expected null but got an object");
        }

        public static void IsNotNull(object obj, string message = null)
        {
            if (obj == null)
                throw new AssertionException(message ?? "Expected an object but got null");
        }

        public static void Throws<T>(Action action, string message = null) where T : Exception
        {
            try
            {
                action();
                throw new AssertionException(message ?? $"Expected exception of type {typeof(T).Name} was not thrown");
            }
            catch (T)
            {
                // Expected exception was thrown
            }
            catch (Exception ex)
            {
                throw new AssertionException($"Expected exception of type {typeof(T).Name} but got {ex.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// アサーション例外
    /// </summary>
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }
}