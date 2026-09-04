using ProjectRealm.Foundation;
using System;
using UnityEngine;

namespace ProjectRealm.UnityAdapter
{
    [DisallowMultipleComponent]
    public sealed class UnityApplicationLifecycle : MonoBehaviour
    {
        public event Action<bool> FocusChanged;

        public bool HasFocus { get; private set; } = true;

        private void OnApplicationFocus(bool hasFocus)
        {
            HasFocus = hasFocus;
            FocusChanged?.Invoke(hasFocus);
        }
    }
}
