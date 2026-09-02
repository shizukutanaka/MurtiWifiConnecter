// CommunityToolkit.Mvvm の **型検査専用スタブ**。製品には含めない。
//
// 循環しない理由: ObservableObject / ObservableProperty / RelayCommand は
// **公表された安定した API** であり、署名を検査対象のコードから逆算していない。
// 生成メンバは tools/stubs/MvvmGenerate.py が公表された命名規約どおりに作る。
//
// ★ 検査しないこと: 変更通知の実挙動 (PropertyChanged の発火、コマンドの
//   CanExecute 再評価など)。型が合うことだけを見ている。
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CommunityToolkit.Mvvm.ComponentModel
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ObservablePropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class NotifyPropertyChangedForAttribute : Attribute
    {
        public NotifyPropertyChangedForAttribute(string name, params string[] more) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class NotifyCanExecuteChangedForAttribute : Attribute
    {
        public NotifyCanExecuteChangedForAttribute(string name, params string[] more) { }
    }

    public abstract class ObservableObject : INotifyPropertyChanged, INotifyPropertyChanging
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event PropertyChangingEventHandler? PropertyChanging;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected void OnPropertyChanging([CallerMemberName] string? name = null)
            => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(name));

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}

namespace CommunityToolkit.Mvvm.Input
{
    using System.Threading.Tasks;

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RelayCommandAttribute : Attribute
    {
        public string? CanExecute { get; set; }
        public bool AllowConcurrentExecutions { get; set; }
    }

    public interface IRelayCommand : System.Windows.Input.ICommand
    {
        void NotifyCanExecuteChanged();
    }

    public interface IAsyncRelayCommand : IRelayCommand
    {
        Task ExecuteAsync(object? parameter);
        bool IsRunning { get; }
    }
}

namespace Microsoft.Win32
{
    /// <summary>WPF の保存ダイアログ。MainViewModel がエクスポート先選択に使う。</summary>
    public class SaveFileDialog
    {
        public string FileName { get; set; } = "";
        public string Filter { get; set; } = "";
        public string Title { get; set; } = "";
        public string DefaultExt { get; set; } = "";
        public bool? ShowDialog() => false;
    }

    public class OpenFileDialog : SaveFileDialog { }
}

namespace System.Windows.Threading
{
    public class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; set; }
        public event EventHandler? Tick;
        public void Start() { IsEnabled = true; }
        public void Stop()  { IsEnabled = false; _ = Tick; }
    }

    public class Dispatcher
    {
        public void Invoke(Action a) => a();
        public System.Threading.Tasks.Task InvokeAsync(Action a) { a(); return System.Threading.Tasks.Task.CompletedTask; }
        public bool CheckAccess() => true;
    }
}
