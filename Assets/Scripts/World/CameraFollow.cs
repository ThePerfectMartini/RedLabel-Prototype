using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 플레이어를 따라가는 카메라. Main Camera에 붙인다.
///
/// X와 Z만 따라간다. 높이는 씬에서 잡아둔 값에 고정한다 — 점프까지 따라가면 화면이
/// 상하로 계속 출렁여서 발판이 어디인지 읽기 어려워진다.
///
/// 두 축 모두 화면 중앙에 데드존을 둬서, 대상이 그 안에서 움직이는 동안은 카메라를 세워둔다.
/// 잔걸음마다 화면이 흔들리는 걸 막기 위한 것이다. 대상이 데드존을 밀고 나가는 만큼만 따라간다.
///
/// 맵 경계 제한은 축마다 다르다.
///  X : 양쪽을 다 막는다. 보이는 가로 폭이 'orthographicSize x 화면비'로 정확히 나오기 때문이다.
///  Z : 아래쪽(-Z)만 막는다. 맵의 아래 끝이 화면 아래 끝에 닿는 자리에서 멈춘다.
///      위쪽을 같이 막으면 보이는 깊이와 맵 깊이가 비슷할 때 카메라가 Z로 아예 못 움직인다.
///      그래서 위로 갈 때는 데드존만 보고 따라간다.
///
/// Orthographic 카메라와 X축으로만 기울어진 회전을 전제한다. 그래야 화면의 가로축이
/// 곧 월드 X가 되어 보이는 가로 폭이 거리와 무관해진다.
///
/// [준비물] 이 오브젝트에 Camera. 따라갈 대상은 인스펙터에서 연결한다.
///          맵 경계 제한은 씬의 MapBounds를 쓴다. 없으면 제한 없이 따라간다.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Tooltip("따라갈 대상. 보통 Player 오브젝트를 넣는다.")]
    [SerializeField] private Transform target;

    [Tooltip("따라붙는 데 걸리는 대략적인 시간(초). 크면 느긋하게, 0에 가까우면 딱 붙어서 따라온다. " +
             "두 축이 같이 쓴다.")]
    [SerializeField] private float smoothTime = 0.2f;

    [Tooltip("좌우 데드존의 절반 폭(월드 단위). 대상이 이 안에 있으면 카메라가 좌우로 움직이지 않는다. " +
             "0이면 항상 따라간다. 화면 가로 절반 폭은 Orthographic Size x 화면비다.")]
    [FormerlySerializedAs("deadZone")]
    [SerializeField] private float deadZoneX = 2f;

    [Tooltip("앞뒤 데드존의 절반 폭(월드 단위). 대상이 이 안에 있으면 카메라가 앞뒤로 움직이지 않는다. " +
             "0이면 항상 따라간다.")]
    [SerializeField] private float deadZoneZ = 2f;

    Camera cam;

    /// <summary>시작할 때의 카메라와 대상의 좌표 차이. 카메라를 옆으로 밀어둔 배치를 그대로 유지한다.</summary>
    float offsetX;
    float offsetZ;

    /// <summary>씬에서 잡아둔 높이. 이 스크립트는 X와 Z만 건드린다.</summary>
    float fixedY;

    /// <summary>SmoothDamp가 프레임 사이에 들고 있어야 하는 현재 속도. 직접 읽고 쓸 값이 아니다.</summary>
    float velocityX;
    float velocityZ;

    /// <summary>씬의 MapBounds. 없으면 null이고 경계 제한을 건너뛴다.</summary>
    MapBounds map;

    void Awake()
    {
        cam = GetComponent<Camera>();

        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        if (target == null)
        {
            Debug.LogError($"{name}: 따라갈 대상이 연결되지 않았습니다. 카메라 추적을 끕니다.", this);
            enabled = false;
            return;
        }

        if (!cam.orthographic)
            Debug.LogWarning($"{name}: Orthographic 카메라를 전제로 경계를 계산합니다. " +
                             "원근 카메라에서는 보이는 폭이 거리에 따라 달라져 맵 경계 제한이 어긋납니다.", this);

        Vector3 pos = transform.position;
        offsetX = pos.x - target.position.x;
        offsetZ = pos.z - target.position.z;
        fixedY = pos.y;

        // 매 프레임 찾으면 낭비라 여기서 한 번만 찾아둔다.
        // 값(Bounds)이 아니라 참조만 잡아두므로 MapBounds.Awake가 나중에 돌아도 문제없다.
        map = MapBounds.Instance;
    }

    // Update가 아니라 LateUpdate다. MovementCore가 Update에서 몸을 옮기므로,
    // 그보다 먼저 따라가면 항상 한 프레임 뒤처진 위치를 쫓게 된다.
    void LateUpdate()
    {
        Vector3 pos = transform.position;

        float desiredX = DeadZoned(pos.x, target.position.x + offsetX, deadZoneX);
        float desiredZ = DeadZoned(pos.z, target.position.z + offsetZ, deadZoneZ);

        if (map != null)
        {
            Bounds b = map.Bounds;

            // ── X : 양쪽 다 막는다.
            // Orthographic Size는 화면 '절반 높이'다. 가로 절반 폭은 여기에 화면비를 곱한 값이다.
            // 화면비는 창 크기에 따라 바뀌므로 매 프레임 다시 읽는다.
            float halfWidth = cam.orthographicSize * cam.aspect;
            float minX = b.min.x + halfWidth;
            float maxX = b.max.x - halfWidth;

            // 맵이 화면보다 좁으면 양쪽 한계가 뒤집힌다. Mathf.Clamp에 그대로 넘기면
            // 가운데가 아니라 왼쪽 끝에 처박히므로, 그 경우는 맵 가운데에 세운다.
            desiredX = minX > maxX ? b.center.x : Mathf.Clamp(desiredX, minX, maxX);

            // ── Z : 아래쪽만 막는다. 위쪽은 데드존만 보고 자유롭게 따라간다.
            float nearOffset = GroundZOffsetAtScreenBottom(b.max.y);
            if (!float.IsNaN(nearOffset))
                desiredZ = Mathf.Max(desiredZ, b.min.z - nearOffset);
        }

        // 제한을 건 뒤에 부드럽게 만든다. 순서를 바꾸면 경계에서 벽에 부딪히듯 딱 멈춘다.
        float x = Mathf.SmoothDamp(pos.x, desiredX, ref velocityX, smoothTime);
        float z = Mathf.SmoothDamp(pos.z, desiredZ, ref velocityZ, smoothTime);

        transform.position = new Vector3(x, fixedY, z);
    }

    /// <summary>
    /// 데드존을 반영한 목표 좌표. 대상이 데드존 안에 있으면 지금 자리를 그대로 돌려준다.
    ///
    /// 데드존 안쪽 몫을 깎아내는 방식이다 — 안에 있으면 0이 남아 카메라가 서고,
    /// 밖으로 나가면 밀고 나간 만큼만 남아 딱 그만큼 따라간다.
    /// 데드존이 0이면 차이가 그대로 남아 데드존 없는 추적이 된다.
    /// </summary>
    static float DeadZoned(float current, float focus, float deadZone)
    {
        float delta = focus - current;
        return current + delta - Mathf.Clamp(delta, -deadZone, deadZone);
    }

    /// <summary>
    /// 화면 아래 끝에서 쏜 시선이 높이 groundY의 바닥면과 만나는 지점이,
    /// 카메라 Z에서 얼마나 앞인지를 잰다. 맵의 아래 끝을 화면 아래 끝에 맞추는 데 쓴다.
    ///
    /// 오일러 각이 아니라 카메라의 실제 up·forward를 쓴다. 기울기를 바꿔도 따라오고,
    /// Orthographic이라 광선이 모두 평행해서 화면 아래 끝 한 점만 보면 된다.
    ///
    /// 카메라가 아래를 보지 않으면 교점이 없다. 그럴 때는 NaN을 돌려 부르는 쪽이 건너뛰게 한다.
    /// </summary>
    float GroundZOffsetAtScreenBottom(float groundY)
    {
        Vector3 forward = transform.forward;
        if (forward.y >= 0f) return float.NaN;

        Vector3 bottomEdge = transform.position - transform.up * cam.orthographicSize;
        float distance = (bottomEdge.y - groundY) / -forward.y;

        return bottomEdge.z + forward.z * distance - transform.position.z;
    }

    // Scene 뷰에서 이 카메라를 선택하면 데드존이 하늘색 사각형으로 보인다.
    // 데드존은 눈에 보이지 않는 값이라, 없으면 감으로 맞춰야 한다.
    void OnDrawGizmosSelected()
    {
        if (target == null) return;
        if (deadZoneX <= 0f && deadZoneZ <= 0f) return;

        // 편집 모드에서는 Awake가 돌지 않아 오프셋이 아직 비어 있다. 지금 배치로 다시 잰다.
        Vector3 pos = transform.position;
        float focusX = Application.isPlaying ? pos.x - offsetX : target.position.x;
        float focusZ = Application.isPlaying ? pos.z - offsetZ : target.position.z;

        // 대상이 걸어다니는 평면에 납작하게 그린다.
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(
            new Vector3(focusX, target.position.y, focusZ),
            new Vector3(deadZoneX * 2f, 0f, deadZoneZ * 2f));
    }
}
