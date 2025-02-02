using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VContainer.Diagnostics;
using VContainer.Internal;
using VContainer.Unity;

namespace VContainer
{
    public interface IObjectResolver : IUniTaskAsyncDisposable
    {
        DiagnosticsCollector Diagnostics { get; set; }

        /// <summary>
        ///     Resolve from type
        /// </summary>
        /// <remarks>
        ///     This version of resolve looks for all of scopes
        /// </remarks>
        object Resolve(Type type);

        /// <summary>
        ///     Try resolve from type
        /// </summary>
        /// <remarks>
        ///     This version of resolve looks for all of scopes
        /// </remarks>
        /// <returns>Successfully resolved</returns>
        bool TryResolve(Type type, out object resolved);

        /// <summary>
        ///     Resolve from meta with registration
        /// </summary>
        /// <remarks>
        ///     This version of resolve will look for instances from only the registration information already founds.
        /// </remarks>
        object Resolve(Registration registration);

        IScopedObjectResolver CreateScope([CanBeNull] ISceneReference scene, Action<IContainerBuilder> installation = null);

        void Inject(object instance);
        bool TryGetRegistration(Type type, out Registration registration);
    }

    public interface IScopedObjectResolver : IObjectResolver
    {
        IObjectResolver Root { get; }
        IScopedObjectResolver Parent { get; }
    }

    public enum Lifetime
    {
        Transient,
        Singleton,
        Scoped
    }

    public sealed class ScopedContainer : IScopedObjectResolver
    {
        public IObjectResolver Root { get; }
        public IScopedObjectResolver Parent { get; }
        public DiagnosticsCollector Diagnostics { get; set; }

        private readonly Registry registry;
        private readonly ConcurrentDictionary<Registration, Lazy<object>> sharedInstances = new();
        private readonly CompositeDisposable disposables = new();
        private readonly Func<Registration, Lazy<object>> createInstance;

        internal ScopedContainer(
            Registry registry,
            IObjectResolver root,
            [CanBeNull] IScopedObjectResolver parent)
        {
            Root = root;
            Parent = parent;
            this.registry = registry;
            createInstance = registration => { return new Lazy<object>(() => registration.SpawnInstance(this)); };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object Resolve(Type type)
        {
            if (TryFindRegistration(type, out Registration registration))
            {
                return Resolve(registration);
            }

            throw new VContainerException(type, $"No such registration of type: {type}");
        }

        public bool TryResolve(Type type, out object resolved)
        {
            if (TryFindRegistration(type, out Registration registration))
            {
                resolved = Resolve(registration);
                return true;
            }

            resolved = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object Resolve(Registration registration)
        {
            if (Diagnostics != null)
            {
                return Diagnostics.TraceResolve(registration, ResolveCore);
            }

            return ResolveCore(registration);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IScopedObjectResolver CreateScope([CanBeNull] ISceneReference scene, Action<IContainerBuilder> installation = null)
        {
            ScopedContainerBuilder containerBuilder = new(Root, this, scene);
            installation?.Invoke(containerBuilder);
            return containerBuilder.BuildScope();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Inject(object instance)
        {
            IInjector injector = InjectorCache.GetOrBuild(instance.GetType());
            injector.Inject(instance, this, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRegistration(Type type, out Registration registration)
            => registry.TryGet(type, out registration);

        public async UniTask DisposeAsync()
        {
            List<Exception> exceptions = new();

            try
            {
                Diagnostics?.Clear();
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            try
            {
                disposables.Dispose();
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            try
            {
                sharedInstances.Clear();
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            if (exceptions.Count == 1)
            {
                throw exceptions[0];
            }

            if (exceptions.Count > 1)
            {
                throw new AggregateException(exceptions);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object ResolveCore(Registration registration)
        {
            switch (registration.Lifetime)
            {
                case Lifetime.Singleton:
                    if (Parent is null)
                    {
                        return Root.Resolve(registration);
                    }

                    if (!registry.Exists(registration.ImplementationType))
                    {
                        return Parent.Resolve(registration);
                    }

                    return CreateTrackedInstance(registration);

                case Lifetime.Scoped:
                    return CreateTrackedInstance(registration);

                default:
                    return registration.SpawnInstance(this);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object CreateTrackedInstance(Registration registration)
        {
            var lazy = sharedInstances.GetOrAdd(registration, createInstance);
            var created = lazy.IsValueCreated;
            var instance = lazy.Value;
            if (!created && instance is IDisposable disposable && !(registration.Provider is ExistingInstanceProvider))
            {
                disposables.Add(disposable);
            }

            return instance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryFindRegistration(Type type, out Registration registration)
        {
            IScopedObjectResolver scope = this;
            while (scope != null)
            {
                if (scope.TryGetRegistration(type, out registration))
                {
                    return true;
                }

                scope = scope.Parent;
            }

            registration = default;
            return false;
        }
    }

    public sealed class Container : IObjectResolver
    {
        public DiagnosticsCollector Diagnostics { get; set; }

        private readonly Registry registry;
        private readonly IScopedObjectResolver rootScope;
        private readonly ConcurrentDictionary<Registration, Lazy<object>> sharedInstances = new();
        private readonly CompositeDisposable disposables = new();
        private readonly Func<Registration, Lazy<object>> createInstance;

        internal Container(Registry registry)
        {
            this.registry = registry;
            rootScope = new ScopedContainer(registry, this, null);

            createInstance = registration => { return new Lazy<object>(() => registration.SpawnInstance(this)); };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object Resolve(Type type)
        {
            if (TryGetRegistration(type, out Registration registration))
            {
                return Resolve(registration);
            }

            throw new VContainerException(type, $"No such registration of type: {type}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolve(Type type, out object resolved)
        {
            if (TryGetRegistration(type, out Registration registration))
            {
                resolved = Resolve(registration);
                return true;
            }

            resolved = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object Resolve(Registration registration)
        {
            if (Diagnostics != null)
            {
                return Diagnostics.TraceResolve(registration, ResolveCore);
            }

            return ResolveCore(registration);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IScopedObjectResolver CreateScope([CanBeNull] ISceneReference scene, Action<IContainerBuilder> installation = null)
            => rootScope.CreateScope(scene, installation);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Inject(object instance)
        {
            IInjector injector = InjectorCache.GetOrBuild(instance.GetType());
            injector.Inject(instance, this, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRegistration(Type type, out Registration registration)
            => registry.TryGet(type, out registration);

        public async UniTask DisposeAsync()
        {
            List<Exception> exceptions = new();

            try
            {
                Diagnostics?.Clear();
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            try
            {
                await rootScope.DisposeAsync();
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            try
            {
                disposables.Dispose();
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            try
            {
                sharedInstances.Clear();
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            if (exceptions.Count == 1)
            {
                throw exceptions[0];
            }

            if (exceptions.Count > 1)
            {
                throw new AggregateException(exceptions);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object ResolveCore(Registration registration)
        {
            switch (registration.Lifetime)
            {
                case Lifetime.Singleton:
                    var singleton = sharedInstances.GetOrAdd(registration, createInstance);
                    if (!singleton.IsValueCreated && singleton.Value is IDisposable disposable &&
                        !(registration.Provider is ExistingInstanceProvider))
                    {
                        disposables.Add(disposable);
                    }

                    return singleton.Value;

                case Lifetime.Scoped:
                    return rootScope.Resolve(registration);

                default:
                    return registration.SpawnInstance(this);
            }
        }
    }
}