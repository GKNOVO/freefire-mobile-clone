using UnityEngine;
using UnityEngine.UI;
using BR.Managers;
using BR.Player;

namespace BR.UI
{
    /// <summary>
    /// Minimapa barato: camera ortografica top-down renderizando para uma RenderTexture
    /// pequena (256x256), com culling mask reduzida (apenas terreno + marcadores).
    ///
    /// Custo: 1 draw extra por frame na GPU, nenhuma logica no main thread.
    /// Alternativa ainda mais barata: mapa 2D por UV (proximo passo, ver roadmap).
    /// </summary>
    public sealed class Minimap : MonoBehaviour
    {
        [SerializeField] private Camera minimapCamera;
        [SerializeField] private RawImage display;
        [SerializeField] private float orthographicSize = 120f;
        [SerializeField] private float height = 220f;
        [SerializeField] private bool rotateWithPlayer = true;
        [SerializeField] private bool followZone;

        private RenderTexture _rt;
        private Transform _target;

        private void Awake()
        {
            if (minimapCamera == null) minimapCamera = GetComponent<Camera>();

            _rt = new RenderTexture(256, 256, 16, RenderTextureFormat.RGB565)
            {
                name = "MinimapRT",
                useMipMap = false
            };
            _rt.Create();

            if (minimapCamera != null)
            {
                minimapCamera.targetTexture = _rt;
                minimapCamera.orthographic = true;
                minimapCamera.orthographicSize = orthographicSize;
                minimapCamera.clearFlags = CameraClearFlags.SolidColor;
                minimapCamera.backgroundColor = new Color(0.05f, 0.08f, 0.1f, 1f);
            }

            if (display != null) display.texture = _rt;
        }

        private void LateUpdate()
        {
            if (minimapCamera == null) return;

            if (_target == null && PlayerController.Local != null)
                _target = PlayerController.Local.transform;

            var center = _target != null ? _target.transform.position : Vector3.zero;
            if (followZone && ZoneManager.Instance != null) center = ZoneManager.Instance.Center;

            minimapCamera.transform.position = new Vector3(center.x, center.y + height, center.z);

            var yaw = rotateWithPlayer && _target != null ? _target.eulerAngles.y : 0f;
            minimapCamera.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
        }

        private void OnDestroy()
        {
            if (minimapCamera != null) minimapCamera.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }
    }
}
