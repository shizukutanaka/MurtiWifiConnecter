using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;

namespace MurtiWifiConnecter.Infrastructure.Validation
{
    /// <summary>
    /// 入力検証フレームワーク
    /// </summary>
    public interface IValidationFramework
    {
        ValidationResult ValidateObject(object obj);
        ValidationResult ValidateProperty(object obj, string propertyName);
        void RegisterValidator<T>(IValidator<T> validator);
        ValidationResult ValidateWithCustomRules(object obj, List<IValidationRule> customRules);
    }

    /// <summary>
    /// 検証フレームワークの実装
    /// </summary>
    public class ValidationFramework : IValidationFramework
    {
        private readonly Dictionary<Type, object> _validators;

        public ValidationFramework()
        {
            _validators = new Dictionary<Type, object>();
            RegisterDefaultValidators();
        }

        /// <summary>
        /// オブジェクト全体を検証
        /// </summary>
        public ValidationResult ValidateObject(object obj)
        {
            if (obj == null)
                return ValidationResult.Error("Object cannot be null");

            var results = new List<ValidationError>();
            var context = new ValidationContext(obj);

            // Data Annotationsによる検証
            var annotationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            var isValid = Validator.TryValidateObject(obj, context, annotationResults, true);

            if (!isValid)
            {
                results.AddRange(annotationResults.Select(r => new ValidationError
                {
                    PropertyName = r.MemberNames.FirstOrDefault() ?? string.Empty,
                    ErrorMessage = r.ErrorMessage,
                    Severity = ValidationSeverity.Error
                }));
            }

            // カスタムバリデーターによる検証
            var objType = obj.GetType();
            if (_validators.TryGetValue(objType, out var validator))
            {
                var customResult = InvokeValidator(validator, obj);
                if (!customResult.IsValid)
                {
                    results.AddRange(customResult.Errors);
                }
            }

            return new ValidationResult
            {
                IsValid = !results.Any(r => r.Severity == ValidationSeverity.Error),
                Errors = results
            };
        }

        /// <summary>
        /// 特定のプロパティを検証
        /// </summary>
        public ValidationResult ValidateProperty(object obj, string propertyName)
        {
            if (obj == null)
                return ValidationResult.Error("Object cannot be null");

            var property = obj.GetType().GetProperty(propertyName);
            if (property == null)
                return ValidationResult.Error($"Property '{propertyName}' not found");

            var context = new ValidationContext(obj) { MemberName = propertyName };
            var value = property.GetValue(obj);
            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

            var isValid = Validator.TryValidateProperty(value, context, results);

            var errors = results.Select(r => new ValidationError
            {
                PropertyName = propertyName,
                ErrorMessage = r.ErrorMessage,
                Severity = ValidationSeverity.Error
            }).ToList();

            return new ValidationResult
            {
                IsValid = isValid,
                Errors = errors
            };
        }

        /// <summary>
        /// バリデーターを登録
        /// </summary>
        public void RegisterValidator<T>(IValidator<T> validator)
        {
            _validators[typeof(T)] = validator;
        }

        /// <summary>
        /// カスタムルールで検証
        /// </summary>
        public ValidationResult ValidateWithCustomRules(object obj, List<IValidationRule> customRules)
        {
            var errors = new List<ValidationError>();

            foreach (var rule in customRules)
            {
                var ruleResult = rule.Validate(obj);
                if (!ruleResult.IsValid)
                {
                    errors.AddRange(ruleResult.Errors);
                }
            }

            return new ValidationResult
            {
                IsValid = !errors.Any(e => e.Severity == ValidationSeverity.Error),
                Errors = errors
            };
        }

        /// <summary>
        /// デフォルトバリデーターを登録
        /// </summary>
        private void RegisterDefaultValidators()
        {
            RegisterValidator(new WifiNetworkValidator());
            RegisterValidator(new ConnectionSettingsValidator());
        }

        /// <summary>
        /// バリデーターを実行
        /// </summary>
        private ValidationResult InvokeValidator(object validator, object obj)
        {
            var method = validator.GetType().GetMethod("Validate");
            return (ValidationResult)method?.Invoke(validator, new[] { obj });
        }
    }

    /// <summary>
    /// バリデーターインターフェース
    /// </summary>
    public interface IValidator<T>
    {
        ValidationResult Validate(T obj);
    }

    /// <summary>
    /// 検証ルールインターフェース
    /// </summary>
    public interface IValidationRule
    {
        ValidationResult Validate(object obj);
    }

    /// <summary>
    /// 検証結果
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<ValidationError> Errors { get; set; } = new();

        public static ValidationResult Success()
        {
            return new ValidationResult { IsValid = true };
        }

        public static ValidationResult Error(string message, string propertyName = "")
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = new List<ValidationError>
                {
                    new ValidationError
                    {
                        PropertyName = propertyName,
                        ErrorMessage = message,
                        Severity = ValidationSeverity.Error
                    }
                }
            };
        }

        public static ValidationResult Warning(string message, string propertyName = "")
        {
            return new ValidationResult
            {
                IsValid = true,
                Errors = new List<ValidationError>
                {
                    new ValidationError
                    {
                        PropertyName = propertyName,
                        ErrorMessage = message,
                        Severity = ValidationSeverity.Warning
                    }
                }
            };
        }
    }

    /// <summary>
    /// 検証エラー
    /// </summary>
    public class ValidationError
    {
        public string PropertyName { get; set; }
        public string ErrorMessage { get; set; }
        public ValidationSeverity Severity { get; set; }
        public string ErrorCode { get; set; }
    }

    /// <summary>
    /// 検証重要度
    /// </summary>
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// WiFiネットワークバリデーター
    /// </summary>
    public class WifiNetworkValidator : IValidator<WifiNetwork>
    {
        public ValidationResult Validate(WifiNetwork network)
        {
            var errors = new List<ValidationError>();

            // SSID検証
            if (string.IsNullOrWhiteSpace(network.SSID))
            {
                errors.Add(new ValidationError
                {
                    PropertyName = nameof(network.SSID),
                    ErrorMessage = "SSIDは必須です",
                    Severity = ValidationSeverity.Error
                });
            }
            else if (network.SSID.Length > 32)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = nameof(network.SSID),
                    ErrorMessage = "SSIDは32文字以下である必要があります",
                    Severity = ValidationSeverity.Error
                });
            }

            // パスワード検証
            if (!string.IsNullOrEmpty(network.Password))
            {
                if (network.Password.Length < 8)
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = nameof(network.Password),
                        ErrorMessage = "パスワードは8文字以上である必要があります",
                        Severity = ValidationSeverity.Warning
                    });
                }

                if (network.Password.Length > 63)
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = nameof(network.Password),
                        ErrorMessage = "パスワードは63文字以下である必要があります",
                        Severity = ValidationSeverity.Error
                    });
                }
            }

            return new ValidationResult
            {
                IsValid = !errors.Any(e => e.Severity == ValidationSeverity.Error),
                Errors = errors
            };
        }
    }

    /// <summary>
    /// 接続設定バリデーター
    /// </summary>
    public class ConnectionSettingsValidator : IValidator<object>
    {
        public ValidationResult Validate(object obj)
        {
            // 動的にプロパティを検証
            var errors = new List<ValidationError>();
            var properties = obj.GetType().GetProperties();

            foreach (var property in properties)
            {
                var value = property.GetValue(obj);
                
                // IP アドレス検証
                if (property.Name.Contains("IP") && value is string ipStr)
                {
                    if (!IsValidIPAddress(ipStr))
                    {
                        errors.Add(new ValidationError
                        {
                            PropertyName = property.Name,
                            ErrorMessage = "有効なIPアドレスを入力してください",
                            Severity = ValidationSeverity.Error
                        });
                    }
                }

                // ポート番号検証
                if (property.Name.Contains("Port") && value is int port)
                {
                    if (port < 1 || port > 65535)
                    {
                        errors.Add(new ValidationError
                        {
                            PropertyName = property.Name,
                            ErrorMessage = "ポート番号は1-65535の範囲で入力してください",
                            Severity = ValidationSeverity.Error
                        });
                    }
                }
            }

            return new ValidationResult
            {
                IsValid = !errors.Any(e => e.Severity == ValidationSeverity.Error),
                Errors = errors
            };
        }

        private bool IsValidIPAddress(string ip)
        {
            return System.Net.IPAddress.TryParse(ip, out _);
        }
    }

    /// <summary>
    /// 共通検証ルール
    /// </summary>
    public static class CommonValidationRules
    {
        public static IValidationRule RequiredField(string propertyName)
        {
            return new RequiredFieldRule(propertyName);
        }

        public static IValidationRule StringLength(string propertyName, int minLength, int maxLength)
        {
            return new StringLengthRule(propertyName, minLength, maxLength);
        }

        public static IValidationRule RegexPattern(string propertyName, string pattern, string errorMessage)
        {
            return new RegexPatternRule(propertyName, pattern, errorMessage);
        }

        public static IValidationRule Range(string propertyName, int min, int max)
        {
            return new RangeRule(propertyName, min, max);
        }
    }

    public class RequiredFieldRule : IValidationRule
    {
        private readonly string _propertyName;

        public RequiredFieldRule(string propertyName)
        {
            _propertyName = propertyName;
        }

        public ValidationResult Validate(object obj)
        {
            var property = obj.GetType().GetProperty(_propertyName);
            var value = property?.GetValue(obj);

            if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
            {
                return ValidationResult.Error($"{_propertyName}は必須項目です", _propertyName);
            }

            return ValidationResult.Success();
        }
    }

    public class StringLengthRule : IValidationRule
    {
        private readonly string _propertyName;
        private readonly int _minLength;
        private readonly int _maxLength;

        public StringLengthRule(string propertyName, int minLength, int maxLength)
        {
            _propertyName = propertyName;
            _minLength = minLength;
            _maxLength = maxLength;
        }

        public ValidationResult Validate(object obj)
        {
            var property = obj.GetType().GetProperty(_propertyName);
            var value = property?.GetValue(obj) as string;

            if (value != null)
            {
                if (value.Length < _minLength)
                    return ValidationResult.Error($"{_propertyName}は{_minLength}文字以上である必要があります", _propertyName);

                if (value.Length > _maxLength)
                    return ValidationResult.Error($"{_propertyName}は{_maxLength}文字以下である必要があります", _propertyName);
            }

            return ValidationResult.Success();
        }
    }

    public class RegexPatternRule : IValidationRule
    {
        private readonly string _propertyName;
        private readonly Regex _regex;
        private readonly string _errorMessage;

        public RegexPatternRule(string propertyName, string pattern, string errorMessage)
        {
            _propertyName = propertyName;
            _regex = new Regex(pattern);
            _errorMessage = errorMessage;
        }

        public ValidationResult Validate(object obj)
        {
            var property = obj.GetType().GetProperty(_propertyName);
            var value = property?.GetValue(obj) as string;

            if (value != null && !_regex.IsMatch(value))
            {
                return ValidationResult.Error(_errorMessage, _propertyName);
            }

            return ValidationResult.Success();
        }
    }

    public class RangeRule : IValidationRule
    {
        private readonly string _propertyName;
        private readonly int _min;
        private readonly int _max;

        public RangeRule(string propertyName, int min, int max)
        {
            _propertyName = propertyName;
            _min = min;
            _max = max;
        }

        public ValidationResult Validate(object obj)
        {
            var property = obj.GetType().GetProperty(_propertyName);
            var value = property?.GetValue(obj);

            if (value is int intValue)
            {
                if (intValue < _min || intValue > _max)
                {
                    return ValidationResult.Error($"{_propertyName}は{_min}から{_max}の範囲で入力してください", _propertyName);
                }
            }

            return ValidationResult.Success();
        }
    }
}