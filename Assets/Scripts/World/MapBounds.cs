using UnityEngine;

/// <summary>
/// 맵 경계(보이지 않는 벽)를 정의하는 컴포넌트.
/// 바닥 오브젝트에 직접 붙이면 그 오브젝트의 Renderer 크기가 그대로 경계가 된다.
///
/// 씬에 하나만 존재한다고 가정하고, MovementCore는 인스펙터 연결 없이 MapBounds.Instance로 찾는다.
/// 경계는 씬 전체가 공유하는 값이라 캐릭터마다 따로 적을 이유가 없기 때문이다.
/// </summary>
public class MapBounds : MonoBehaviour
{
    static MapBounds instance;

    /// <summary>
    /// 씬 안의 MapBounds를 찾아 캐싱해서 반환. 없으면 null.
    /// Awake 실행 순서에 의존하지 않도록 첫 접근 시점에 찾는다.
    /// </summary>
    public static MapBounds Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<MapBounds>();
            return instance;
        }
    }

    Bounds bounds;

    /// <summary>월드 좌표 기준 경계 상자. Awake에서 한 번 계산하고 고정된다.</summary>
    public Bounds Bounds => bounds;

    void Awake()
    {
        instance = this;
        bounds = CalculateBounds();
    }

    Bounds CalculateBounds()
    {
        // Renderer.bounds는 회전·스케일이 전부 반영된 월드 크기다.
        // 바닥 Plane의 Scale만 바꿔도 경계가 따라온다.
        Renderer r = GetComponent<Renderer>();
        if (r != null)
            return r.bounds;

        Debug.LogWarning($"{name}: Renderer가 없어 기본 크기(20×10×20)를 사용합니다.", this);
        return new Bounds(transform.position, new Vector3(20f, 10f, 20f));
    }

    // Scene 뷰에서 이 오브젝트를 선택하면 경계가 노란 상자로 보인다.
    void OnDrawGizmosSelected()
    {
        Bounds b = Application.isPlaying ? bounds : CalculateBounds();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(b.center, b.size);
    }
}