using Cysharp.Threading.Tasks;

namespace VContainer.Unity
{
    public interface ILifetimeScope : IUniTaskAsyncDisposable
    {
        IObjectResolver Container { get; }
        ILifetimeScope Parent { get; }
        bool IsRoot { get; }
        void Build();
    }
}