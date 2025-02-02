using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Diagnostics;

namespace VContainer.Unity
{
    [DefaultExecutionOrder(-5000)]
    public class LifetimeScope : ILifetimeScope
    {
        public IObjectResolver Container { get; private set; }
        public ILifetimeScope Parent { get; private set; }
        public bool IsRoot { get; private set; }

        private readonly List<IInstaller> localExtraInstallers = new();
        private string scopeName;
        private ISceneReference scene;

        public static LifetimeScope Create(ISceneReference scene, IInstaller installer = null, string name = null, ILifetimeScope parent = null)
        {
            LifetimeScope newScope = new();

            newScope.scene = scene;
            newScope.Parent = parent;
            newScope.IsRoot = parent == null;
            newScope.scopeName = name;

            if (installer != null)
            {
                newScope.localExtraInstallers.Add(installer);
            }

            return newScope;
        }

        public static LifetimeScope Create(ISceneReference scene, Action<IContainerBuilder> configuration, string name = null, LifetimeScope parent = null)
        {
            return Create(scene, new ActionInstaller(configuration), name, parent);
        }

        protected virtual void Configure(IContainerBuilder builder)
        {
        }

        public async UniTask DisposeAsync()
        {
            try
            {
                if (Container != null)
                {
                    await Container.DisposeAsync();
                }
            }
            finally
            {
                Container = null;
                if (VContainerSettings.DiagnosticsEnabled)
                {
                    DiagnositcsContext.RemoveCollector(scopeName);
                }
            }
        }

        public void Build()
        {
            if (Parent != null)
            {
                if (VContainerSettings.Instance != null && Parent.IsRoot)
                {
                    if (Parent.Container == null)
                    {
                        Parent.Build();
                    }
                }

                // ReSharper disable once PossibleNullReferenceException
                Parent.Container.CreateScope(scene,
                    builder =>
                {
                    builder.RegisterBuildCallback(SetContainer);
                    builder.Diagnostics = VContainerSettings.DiagnosticsEnabled ? DiagnositcsContext.GetCollector(scopeName) : null;
                    InstallTo(builder);
                });
            }
            else
            {
                ContainerBuilder builder = new()
                {
                    ApplicationOrigin = this,
                    Diagnostics = VContainerSettings.DiagnosticsEnabled ? DiagnositcsContext.GetCollector(scopeName) : null
                };
                builder.RegisterBuildCallback(SetContainer);
                InstallTo(builder);
                builder.Build();
            }
        }

        private void SetContainer(IObjectResolver container)
        {
            Container = container;
        }

        private void InstallTo(IContainerBuilder builder)
        {
            Configure(builder);

            foreach (IInstaller installer in localExtraInstallers)
            {
                installer.Install(builder);
            }

            localExtraInstallers.Clear();

            builder.RegisterInstance<LifetimeScope>(this).AsSelf();
            EntryPointsBuilder.EnsureDispatcherRegistered(builder);
        }
    }
}