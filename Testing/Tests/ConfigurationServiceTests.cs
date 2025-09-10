using System.Threading.Tasks;
using MurtiWifiConnecter.Testing;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Testing.Tests
{
    [TestSuite("Configuration")]
    public class ConfigurationServiceTests : TestFixture
    {
        private IConfigurationService _configService;

        [Setup]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            _configService = GetService<IConfigurationService>();
        }

        [TestMethod(Description = "Should get and set string values")]
        public async Task GetSetValue_String_ShouldWork()
        {
            const string key = "TestKey";
            const string value = "TestValue";

            await _configService.SetValueAsync(key, value);
            var retrievedValue = _configService.GetValue<string>(key);

            Assert.Equal(value, retrievedValue);
        }

        [TestMethod(Description = "Should get and set integer values")]
        public async Task GetSetValue_Integer_ShouldWork()
        {
            const string key = "NumberKey";
            const int value = 42;

            await _configService.SetValueAsync(key, value);
            var retrievedValue = _configService.GetValue<int>(key);

            Assert.Equal(value, retrievedValue);
        }

        [TestMethod(Description = "Should get and set boolean values")]
        public async Task GetSetValue_Boolean_ShouldWork()
        {
            const string key = "BoolKey";
            const bool value = true;

            await _configService.SetValueAsync(key, value);
            var retrievedValue = _configService.GetValue<bool>(key);

            Assert.Equal(value, retrievedValue);
        }

        [TestMethod(Description = "Should return default value for non-existent key")]
        public void GetValue_NonExistentKey_ShouldReturnDefault()
        {
            const string defaultValue = "default";
            var retrievedValue = _configService.GetValue("NonExistentKey", defaultValue);

            Assert.Equal(defaultValue, retrievedValue);
        }

        [TestMethod(Description = "Should return type default for non-existent key without default")]
        public void GetValue_NonExistentKeyNoDefault_ShouldReturnTypeDefault()
        {
            var stringValue = _configService.GetValue<string>("NonExistentKey");
            var intValue = _configService.GetValue<int>("NonExistentKey");
            var boolValue = _configService.GetValue<bool>("NonExistentKey");

            Assert.Null(stringValue);
            Assert.Equal(0, intValue);
            Assert.False(boolValue);
        }

        [TestMethod(Description = "Should raise configuration changed event")]
        public async Task SetValue_ShouldRaiseConfigurationChangedEvent()
        {
            const string key = "EventTestKey";
            const string value = "EventTestValue";
            
            var eventRaised = false;
            string eventKey = null;
            object eventOldValue = null;
            object eventNewValue = null;

            _configService.ConfigurationChanged += (sender, args) =>
            {
                eventRaised = true;
                eventKey = args.Key;
                eventOldValue = args.OldValue;
                eventNewValue = args.NewValue;
            };

            await _configService.SetValueAsync(key, value);

            Assert.True(eventRaised, "Configuration changed event should be raised");
            Assert.Equal(key, eventKey);
            Assert.Null(eventOldValue);
            Assert.Equal(value, eventNewValue);
        }

        [TestMethod(Description = "Should raise event with old value when updating")]
        public async Task SetValue_Update_ShouldRaiseEventWithOldValue()
        {
            const string key = "UpdateKey";
            const string oldValue = "OldValue";
            const string newValue = "NewValue";

            await _configService.SetValueAsync(key, oldValue);

            var eventRaised = false;
            object eventOldValue = null;
            object eventNewValue = null;

            _configService.ConfigurationChanged += (sender, args) =>
            {
                eventRaised = true;
                eventOldValue = args.OldValue;
                eventNewValue = args.NewValue;
            };

            await _configService.SetValueAsync(key, newValue);

            Assert.True(eventRaised);
            Assert.Equal(oldValue, eventOldValue);
            Assert.Equal(newValue, eventNewValue);
        }

        [TestMethod(Description = "Should remove value and raise event")]
        public async Task RemoveValue_ShouldRemoveAndRaiseEvent()
        {
            const string key = "RemoveKey";
            const string value = "RemoveValue";

            await _configService.SetValueAsync(key, value);
            Assert.True(_configService.HasValue(key));

            var eventRaised = false;
            _configService.ConfigurationChanged += (sender, args) =>
            {
                eventRaised = true;
                Assert.Equal(key, args.Key);
                Assert.Equal(value, args.OldValue);
                Assert.Null(args.NewValue);
            };

            await _configService.RemoveValueAsync(key);

            Assert.True(eventRaised);
            Assert.False(_configService.HasValue(key));
        }

        [TestMethod(Description = "Should handle removing non-existent key")]
        public async Task RemoveValue_NonExistentKey_ShouldNotRaiseEvent()
        {
            var eventRaised = false;
            _configService.ConfigurationChanged += (sender, args) => eventRaised = true;

            await _configService.RemoveValueAsync("NonExistentKey");

            Assert.False(eventRaised);
        }

        [TestMethod(Description = "Should get all values")]
        public async Task GetAllValues_ShouldReturnAllConfigurationValues()
        {
            await _configService.SetValueAsync("Key1", "Value1");
            await _configService.SetValueAsync("Key2", 42);
            await _configService.SetValueAsync("Key3", true);

            var allValues = _configService.GetAllValues();

            Assert.Equal(3, allValues.Count);
            Assert.Equal("Value1", allValues["Key1"]);
            Assert.Equal(42, allValues["Key2"]);
            Assert.Equal(true, allValues["Key3"]);
        }

        [TestMethod(Description = "Should check if value exists")]
        public async Task HasValue_ShouldReturnCorrectResult()
        {
            const string existingKey = "ExistingKey";
            const string nonExistentKey = "NonExistentKey";

            await _configService.SetValueAsync(existingKey, "value");

            Assert.True(_configService.HasValue(existingKey));
            Assert.False(_configService.HasValue(nonExistentKey));
        }

        [TestMethod(Description = "Should handle type conversion")]
        public async Task GetValue_TypeConversion_ShouldWork()
        {
            const string key = "ConversionKey";

            await _configService.SetValueAsync(key, "123");
            var intValue = _configService.GetValue<int>(key);
            Assert.Equal(123, intValue);

            await _configService.SetValueAsync(key, "true");
            var boolValue = _configService.GetValue<bool>(key);
            Assert.True(boolValue);

            await _configService.SetValueAsync(key, "45.67");
            var doubleValue = _configService.GetValue<double>(key);
            Assert.Equal(45.67, doubleValue, 0.01);
        }

        [TestMethod(Description = "Should handle invalid type conversion gracefully")]
        public void GetValue_InvalidTypeConversion_ShouldReturnDefault()
        {
            const string key = "InvalidConversionKey";
            const int defaultValue = 999;

            MockConfiguration.SetValueAsync(key, "not_a_number").Wait();

            var result = _configService.GetValue(key, defaultValue);

            Assert.Equal(defaultValue, result);
        }

        [TestMethod(Description = "Should handle concurrent access")]
        public async Task ConcurrentAccess_ShouldBeThreadSafe()
        {
            const int taskCount = 10;
            const int operationsPerTask = 100;

            var tasks = new Task[taskCount];

            for (int i = 0; i < taskCount; i++)
            {
                int taskId = i;
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < operationsPerTask; j++)
                    {
                        var key = $"ConcurrentKey_{taskId}_{j}";
                        var value = $"Value_{taskId}_{j}";
                        
                        await _configService.SetValueAsync(key, value);
                        var retrieved = _configService.GetValue<string>(key);
                        Assert.Equal(value, retrieved);
                    }
                });
            }

            await Task.WhenAll(tasks);

            var allValues = _configService.GetAllValues();
            Assert.Equal(taskCount * operationsPerTask, allValues.Count);

            AssertNoErrors();
        }

        [TestMethod(Description = "Should validate configuration keys")]
        public void SetValue_InvalidKey_ShouldThrow()
        {
            Assert.ThrowsAny(async () => await _configService.SetValueAsync(null, "value"));
            Assert.ThrowsAny(async () => await _configService.SetValueAsync("", "value"));
        }

        [TestMethod(Description = "Should handle complex objects")]
        public async Task SetValue_ComplexObject_ShouldWork()
        {
            const string key = "ComplexKey";
            var complexValue = new { Name = "Test", Value = 42, Active = true };

            await _configService.SetValueAsync(key, complexValue);
            var retrieved = _configService.GetValue<object>(key);

            Assert.NotNull(retrieved);
            Assert.Equal(complexValue, retrieved);
        }

        [TestMethod(Description = "Should track telemetry for configuration changes")]
        public async Task SetValue_ShouldTrackTelemetry()
        {
            const string key = "TelemetryKey";
            const string value = "TelemetryValue";

            await _configService.SetValueAsync(key, value);

            AssertEventTracked("Configuration.ValueChanged");
            AssertLogContains(LogLevel.Info, "configuration");
        }

        [TestMethod(Description = "Performance test for configuration operations")]
        public async Task PerformanceTest_ConfigurationOperations_ShouldMeetThresholds()
        {
            const int iterations = 1000;

            var setResult = await PerformanceTestHelper.BenchmarkAsync(async () =>
            {
                await _configService.SetValueAsync($"PerfKey_{System.Guid.NewGuid()}", "PerfValue");
            }, iterations);

            var getResult = PerformanceTestHelper.Benchmark(() =>
            {
                _configService.GetValue<string>("Key1", "default");
            }, iterations);

            // Performance assertions - configuration operations should be fast
            Assert.InRange(setResult.AverageMilliseconds, 0, 10);
            Assert.InRange(getResult.AverageMilliseconds, 0, 1);

            MockLogger.LogInfo($"Set performance: {setResult}");
            MockLogger.LogInfo($"Get performance: {getResult}");

            AssertNoErrors();
        }
    }
}