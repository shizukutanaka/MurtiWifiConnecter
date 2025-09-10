using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MurtiWifiConnecter.Interfaces;
using MurtiWifiConnecter.Services;

namespace MurtiWifiConnecter.Infrastructure
{
    /// <summary>
    /// 軽量DIコンテナ - Dependency Inversion Principleの実装
    /// Service Locator Pattern + Factory Pattern
    /// </summary>
    public class ServiceContainer : IDisposable
    {
        private readonly ConcurrentDictionary<Type, object> _singletonInstances = new();
        private readonly ConcurrentDictionary<Type, Func<ServiceContainer, object>> _factories = new();
        private readonly List<IDisposable> _disposables = new();
        private bool _disposed = false;

        /// <summary>
        /// サービスファクトリの登録
        /// </summary>
        public void Register<TInterface, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
            where TImplementation : class, TInterface
        {
            Register<TInterface>(() => CreateInstance<TImplementation>(), lifetime);
        }

        /// <summary>
        /// ファクトリメソッドの登録
        /// </summary>
        public void Register<TInterface>(Func<ServiceContainer, TInterface> factory, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            if (lifetime == ServiceLifetime.Singleton)
            {
                _factories[typeof(TInterface)] = container => 
                {
                    var instance = factory(container);
                    if (instance is IDisposable disposable)
                        _disposables.Add(disposable);
                    return instance;
                };
            }
            else
            {
                _factories[typeof(TInterface)] = container => factory(container);
            }
        }

        /// <summary>
        /// インスタンスの直接登録
        /// </summary>
        public void RegisterInstance<TInterface>(TInterface instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            
            _singletonInstances[typeof(TInterface)] = instance;
            
            if (instance is IDisposable disposable)
                _disposables.Add(disposable);
        }

        /// <summary>
        /// サービスの解決
        /// </summary>
        public T Resolve<T>()
        {
            return (T)Resolve(typeof(T));
        }

        /// <summary>
        /// サービスの解決（型指定）
        /// </summary>
        public object Resolve(Type serviceType)
        {
            // シングルトンインスタンスの確認
            if (_singletonInstances.TryGetValue(serviceType, out var singletonInstance))
                return singletonInstance;

            // ファクトリの確認
            if (_factories.TryGetValue(serviceType, out var factory))
            {
                var instance = factory(this);
                
                // シングルトンの場合はキャッシュ
                if (_factories.ContainsKey(serviceType))
                    _singletonInstances[serviceType] = instance;
                    
                return instance;
            }

            // 自動解決の試行
            if (serviceType.IsClass && !serviceType.IsAbstract)
            {
                return CreateInstance(serviceType);
            }

            throw new ServiceResolutionException($"Service of type {serviceType.Name} is not registered");
        }

        /// <summary>
        /// デフォルトサービスの登録
        /// </summary>
        public void RegisterDefaultServices()
        {
            // ロガーの登録
            RegisterInstance<ConnectionLogger>(new ConnectionLogger());
            
            // コアサービスの登録
            Register<IWifiService, WifiService>();
            
            // 管理サービスの登録
            Register<IProfileService>(container => 
                new ProfileService(container.Resolve<ConnectionLogger>()));
                
            Register<ILoggingService>(container => 
                new LoggingService(container.Resolve<ConnectionLogger>()));
                
            Register<IStatisticsService>(container => 
                new StatisticsService(container.Resolve<ConnectionLogger>()));
            
            // 接続管理サービスの登録
            Register<IConnectionManagementService>(container =>
            {
                var logger = container.Resolve<ConnectionLogger>();
                return new ConnectionManagementService(
                    logger,
                    new ConnectionRetryManager(logger),
                    new UnifiedProfileManager(logger),
                    new ConnectionMonitor(logger));
            });

            // ファサードサービスの登録
            Register<WifiServiceFacade>(container => new WifiServiceFacade(
                container.Resolve<IWifiService>(),
                container.Resolve<IConnectionManagementService>(),
                container.Resolve<IProfileService>(),
                container.Resolve<ILoggingService>(),
                container.Resolve<IStatisticsService>()));
        }

        /// <summary>
        /// インスタンス作成（リフレクション使用）
        /// </summary>
        private T CreateInstance<T>() where T : class
        {
            return (T)CreateInstance(typeof(T));
        }

        /// <summary>
        /// インスタンス作成（型指定）
        /// </summary>
        private object CreateInstance(Type type)
        {
            try
            {
                return Activator.CreateInstance(type) 
                    ?? throw new ServiceResolutionException($"Failed to create instance of {type.Name}");
            }
            catch (Exception ex)
            {
                throw new ServiceResolutionException($"Error creating instance of {type.Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            // 登録されたDisposableの解放
            foreach (var disposable in _disposables)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    // ログ記録（ログサービスが利用可能な場合）
                    ErrorHandler.LogError("ServiceContainer.Dispose", ex);
                }
            }

            _disposables.Clear();
            _singletonInstances.Clear();
            _factories.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// サービスライフタイム定義
    /// </summary>
    public enum ServiceLifetime
    {
        Singleton,
        Transient
    }

    /// <summary>
    /// サービス解決例外
    /// </summary>
    public class ServiceResolutionException : Exception
    {
        public ServiceResolutionException(string message) : base(message) { }
        public ServiceResolutionException(string message, Exception innerException) : base(message, innerException) { }
    }
}