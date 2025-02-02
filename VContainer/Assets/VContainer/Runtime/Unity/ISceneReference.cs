using System;
using UnityEngine.SceneManagement;

namespace VContainer.Unity
{
    public interface ISceneReference
    {
        Scene Scene { get; }
    }
}