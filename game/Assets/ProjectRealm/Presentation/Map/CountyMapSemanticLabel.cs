using UnityEngine;

namespace ProjectRealm.UnityPresentation.Map
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CountyMapSemanticLabel : MonoBehaviour
    {
        [SerializeField] private float maximumVisibleOrthographicSize = 18f;
        private Renderer labelRenderer;

        public void Configure(float maximumVisibleSize)
        {
            maximumVisibleOrthographicSize = maximumVisibleSize;
            labelRenderer = GetComponent<Renderer>();
        }

        private void Awake()
        {
            labelRenderer = GetComponent<Renderer>();
        }

        private void LateUpdate()
        {
            var mapCamera = Camera.main;
            if (mapCamera == null)
            {
                return;
            }

            if (labelRenderer == null)
            {
                labelRenderer = GetComponent<Renderer>();
            }

            if (labelRenderer != null)
            {
                labelRenderer.enabled = !mapCamera.orthographic ||
                                        mapCamera.orthographicSize <= maximumVisibleOrthographicSize;
            }

            transform.rotation = mapCamera.transform.rotation;
        }
    }
}
