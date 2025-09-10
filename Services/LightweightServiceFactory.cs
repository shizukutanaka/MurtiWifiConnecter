using System;
using System.Collections.Concurrent;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Services
{
    /// <summary>
    /// 軽量サービスファクトリ - 高速で最小限のDI機能
    /// </summary>
    public static class LightweightServiceFactory
    {
        private static readonly ConcurrentDictionary<Type, Lazy<object>> _singletons = new();
        private static readonly ConcurrentDictionary<Type, Func<object>> _factories = new();

        /// <summary>
        /// シングルトンサービスの登録
        /// </summary>
        public static void RegisterSingleton<T>(Func<T> factory) where T : class
        {
            _singletons[typeof(T)] = new Lazy<object>(() => factory());
        }

        /// <summary>
        /// 一時的サービスの登録
        /// </summary>
        public static void RegisterTransient<T>(Func<T> factory) where T : class
        {
            _factories[typeof(T)] = () => factory();
        }

        /// <summary>
        /// サービスの取得
        /// </summary>
        public static T GetService<T>() where T : class
        {
            var type = typeof(T);
            
            // シングルトンを確認
            if (_singletons.TryGetValue(type, out var singleton))
            {
                return (T)singleton.Value;
            }
            
            // 一時的サービスを確認
            if (_factories.TryGetValue(type, out var factory))
            {
                return (T)factory();
            }
            
            throw new InvalidOperationException($"Service of type {type.Name} is not registered");
        }

        /// <summary>
        /// サービスの取得（null許可）
        /// </summary>
        public static T TryGetService<T>() where T : class
        {
            try
            {
                return GetService<T>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// デフォルトサービス登録
        /// </summary>
        public static void RegisterDefaults()
        {
            RegisterSingleton<ILoggingService>(() => new SimpleLoggingService());
            RegisterSingleton<WifiService>(() => new WifiService());
            RegisterSingleton<LightweightMonitoringService>(() => new LightweightMonitoringService());
            RegisterTransient<ConnectionLogger>(() => new ConnectionLogger());
        }

        /// <summary>
        /// すべての登録をクリア
        /// </summary>
        public static void Clear()
        {
            _singletons.Clear();
            _factories.Clear();
        }
    }
}