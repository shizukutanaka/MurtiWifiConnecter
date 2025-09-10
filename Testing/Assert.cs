using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MurtiWifiConnecter.Testing
{
    public static class Assert
    {
        public static void True(bool condition, string message = null)
        {
            if (!condition)
                throw new AssertionException(message ?? "Expected true but was false");
        }

        public static void False(bool condition, string message = null)
        {
            if (condition)
                throw new AssertionException(message ?? "Expected false but was true");
        }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!Equals(expected, actual))
                throw new AssertionException(message ?? $"Expected '{expected}' but was '{actual}'");
        }

        public static void NotEqual<T>(T expected, T actual, string message = null)
        {
            if (Equals(expected, actual))
                throw new AssertionException(message ?? $"Expected not equal to '{expected}' but was equal");
        }

        public static void Null(object value, string message = null)
        {
            if (value != null)
                throw new AssertionException(message ?? $"Expected null but was '{value}'");
        }

        public static void NotNull(object value, string message = null)
        {
            if (value == null)
                throw new AssertionException(message ?? "Expected not null but was null");
        }

        public static void Empty(IEnumerable collection, string message = null)
        {
            if (collection?.Cast<object>().Any() == true)
                throw new AssertionException(message ?? "Expected empty collection but was not empty");
        }

        public static void NotEmpty(IEnumerable collection, string message = null)
        {
            if (collection?.Cast<object>().Any() != true)
                throw new AssertionException(message ?? "Expected non-empty collection but was empty or null");
        }

        public static void Contains<T>(IEnumerable<T> collection, T item, string message = null)
        {
            if (collection?.Contains(item) != true)
                throw new AssertionException(message ?? $"Expected collection to contain '{item}' but it did not");
        }

        public static void DoesNotContain<T>(IEnumerable<T> collection, T item, string message = null)
        {
            if (collection?.Contains(item) == true)
                throw new AssertionException(message ?? $"Expected collection not to contain '{item}' but it did");
        }

        public static void Count<T>(IEnumerable<T> collection, int expectedCount, string message = null)
        {
            var actualCount = collection?.Count() ?? 0;
            if (actualCount != expectedCount)
                throw new AssertionException(message ?? $"Expected count {expectedCount} but was {actualCount}");
        }

        public static void IsType<T>(object value, string message = null)
        {
            IsType(typeof(T), value, message);
        }

        public static void IsType(Type expectedType, object value, string message = null)
        {
            if (value?.GetType() != expectedType)
                throw new AssertionException(message ?? $"Expected type '{expectedType?.Name}' but was '{value?.GetType()?.Name ?? "null"}'");
        }

        public static void IsAssignableFrom<T>(object value, string message = null)
        {
            IsAssignableFrom(typeof(T), value, message);
        }

        public static void IsAssignableFrom(Type expectedType, object value, string message = null)
        {
            if (value == null || !expectedType.IsAssignableFrom(value.GetType()))
                throw new AssertionException(message ?? $"Expected type assignable from '{expectedType?.Name}' but was '{value?.GetType()?.Name ?? "null"}'");
        }

        public static void Throws<T>(Action action, string message = null) where T : Exception
        {
            try
            {
                action();
                throw new AssertionException(message ?? $"Expected exception of type '{typeof(T).Name}' but no exception was thrown");
            }
            catch (T)
            {
                // Expected exception was thrown
            }
            catch (Exception ex)
            {
                throw new AssertionException(message ?? $"Expected exception of type '{typeof(T).Name}' but got '{ex.GetType().Name}': {ex.Message}");
            }
        }

        public static void ThrowsAny(Action action, string message = null)
        {
            try
            {
                action();
                throw new AssertionException(message ?? "Expected an exception but none was thrown");
            }
            catch (AssertionException)
            {
                throw;
            }
            catch
            {
                // Expected - any exception was thrown
            }
        }

        public static void DoesNotThrow(Action action, string message = null)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new AssertionException(message ?? $"Expected no exception but got '{ex.GetType().Name}': {ex.Message}");
            }
        }

        public static void InRange<T>(T actual, T low, T high, string message = null) where T : IComparable<T>
        {
            if (actual.CompareTo(low) < 0 || actual.CompareTo(high) > 0)
                throw new AssertionException(message ?? $"Expected value in range [{low}, {high}] but was {actual}");
        }

        public static void NotInRange<T>(T actual, T low, T high, string message = null) where T : IComparable<T>
        {
            if (actual.CompareTo(low) >= 0 && actual.CompareTo(high) <= 0)
                throw new AssertionException(message ?? $"Expected value not in range [{low}, {high}] but was {actual}");
        }

        public static void StartsWith(string actual, string expected, string message = null)
        {
            if (actual?.StartsWith(expected) != true)
                throw new AssertionException(message ?? $"Expected string to start with '{expected}' but was '{actual}'");
        }

        public static void EndsWith(string actual, string expected, string message = null)
        {
            if (actual?.EndsWith(expected) != true)
                throw new AssertionException(message ?? $"Expected string to end with '{expected}' but was '{actual}'");
        }

        public static void ContainsSubstring(string actual, string expected, string message = null)
        {
            if (actual?.Contains(expected) != true)
                throw new AssertionException(message ?? $"Expected string to contain '{expected}' but was '{actual}'");
        }

        public static void Matches(string actual, string pattern, string message = null)
        {
            if (!Regex.IsMatch(actual ?? "", pattern))
                throw new AssertionException(message ?? $"Expected string to match pattern '{pattern}' but was '{actual}'");
        }

        public static void DoesNotMatch(string actual, string pattern, string message = null)
        {
            if (Regex.IsMatch(actual ?? "", pattern))
                throw new AssertionException(message ?? $"Expected string not to match pattern '{pattern}' but it did: '{actual}'");
        }

        public static void All<T>(IEnumerable<T> collection, Predicate<T> predicate, string message = null)
        {
            var items = collection?.ToList() ?? new List<T>();
            var failedItem = items.FirstOrDefault(item => !predicate(item));
            
            if (failedItem != null)
                throw new AssertionException(message ?? $"Expected all items to match predicate but item '{failedItem}' did not");
        }

        public static void Any<T>(IEnumerable<T> collection, Predicate<T> predicate, string message = null)
        {
            var items = collection?.ToList() ?? new List<T>();
            if (!items.Any(item => predicate(item)))
                throw new AssertionException(message ?? "Expected at least one item to match predicate but none did");
        }

        public static void Single<T>(IEnumerable<T> collection, string message = null)
        {
            var items = collection?.ToList() ?? new List<T>();
            if (items.Count != 1)
                throw new AssertionException(message ?? $"Expected single item but found {items.Count} items");
        }

        public static void Single<T>(IEnumerable<T> collection, Predicate<T> predicate, string message = null)
        {
            var items = collection?.Where(item => predicate(item)).ToList() ?? new List<T>();
            if (items.Count != 1)
                throw new AssertionException(message ?? $"Expected single item matching predicate but found {items.Count} items");
        }

        // Floating point comparisons
        public static void Equal(double expected, double actual, double precision, string message = null)
        {
            if (Math.Abs(expected - actual) > precision)
                throw new AssertionException(message ?? $"Expected {expected} ± {precision} but was {actual}");
        }

        public static void Equal(float expected, float actual, float precision, string message = null)
        {
            if (Math.Abs(expected - actual) > precision)
                throw new AssertionException(message ?? $"Expected {expected} ± {precision} but was {actual}");
        }

        // Collection equality
        public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message = null)
        {
            var expectedList = expected?.ToList() ?? new List<T>();
            var actualList = actual?.ToList() ?? new List<T>();

            if (expectedList.Count != actualList.Count)
                throw new AssertionException(message ?? $"Expected collection count {expectedList.Count} but was {actualList.Count}");

            for (int i = 0; i < expectedList.Count; i++)
            {
                if (!Equals(expectedList[i], actualList[i]))
                    throw new AssertionException(message ?? $"Collections differ at index {i}: expected '{expectedList[i]}' but was '{actualList[i]}'");
            }
        }

        // Custom assertion
        public static void That(bool condition, string message)
        {
            if (!condition)
                throw new AssertionException(message);
        }

        public static void That<T>(T actual, Func<T, bool> assertion, string message)
        {
            if (!assertion(actual))
                throw new AssertionException(message);
        }

        // Fail explicitly
        public static void Fail(string message = null)
        {
            throw new AssertionException(message ?? "Assertion failed");
        }
    }

    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
        public AssertionException(string message, Exception innerException) : base(message, innerException) { }
    }

    // Fluent assertion extensions
    public static class FluentAssertions
    {
        public static AssertionBuilder<T> Should<T>(this T actual)
        {
            return new AssertionBuilder<T>(actual);
        }
    }

    public class AssertionBuilder<T>
    {
        private readonly T _actual;

        public AssertionBuilder(T actual)
        {
            _actual = actual;
        }

        public AssertionBuilder<T> Be(T expected, string message = null)
        {
            Assert.Equal(expected, _actual, message);
            return this;
        }

        public AssertionBuilder<T> NotBe(T expected, string message = null)
        {
            Assert.NotEqual(expected, _actual, message);
            return this;
        }

        public AssertionBuilder<T> BeNull(string message = null)
        {
            Assert.Null(_actual, message);
            return this;
        }

        public AssertionBuilder<T> NotBeNull(string message = null)
        {
            Assert.NotNull(_actual, message);
            return this;
        }

        public AssertionBuilder<T> BeOfType<TExpected>(string message = null)
        {
            Assert.IsType<TExpected>(_actual, message);
            return this;
        }

        public AssertionBuilder<T> Match(Predicate<T> predicate, string message = null)
        {
            Assert.True(predicate(_actual), message ?? $"Expected value to match predicate but it did not: {_actual}");
            return this;
        }
    }
}