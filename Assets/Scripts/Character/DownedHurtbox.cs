using UnityEngine;

/// <summary>
/// 쓰러져 있는 동안(착지·누움·일어서기) 피격 박스를 누운 모양으로 바꾸는 컴포넌트.
///
/// 콜라이더는 Visual과 형제 가지라 애니메이션 클립으로는 건드릴 수 없어서 여기서 직접 바꾼다.
/// Transform이 아니라 BoxCollider의 center·size를 바꾸는 이유: 콜라이더 값은 바로 물리에 반영되어
/// 다음 OverlapBox부터 새 모양으로 맞는다. Transform 변경은 물리 동기화 전까지 늦게 잡힐 수 있다.
///
/// 선 모양은 따로 입력받지 않고 Awake 때의 콜라이더 값을 그대로 기억해 둔다.
/// 누운 모양은 오른쪽을 볼 때 기준이고, 왼쪽을 보면 center.x를 뒤집는다(머리 방향).
///
/// MovementCore는 몸집·발바닥을 Awake에서 한 번만 재므로, 여기서 모양을 바꿔도 이동·착지에는 영향이 없다.
///
/// [준비물] 이 오브젝트에 BoxCollider, 계층 위쪽에 Character, 그 아래 어딘가에 StateMachine + MovementCore.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DownedHurtbox : MonoBehaviour
{
    /// <summary>오른쪽을 볼 때의 누운 박스 기즈모 색.</summary>
    static readonly Color LyingColor = new Color(1f, 0.5f, 0f);

    /// <summary>왼쪽을 볼 때(좌우 반전)의 누운 박스 기즈모 색. 오른쪽 것과 구분되게 흐리게 그린다.</summary>
    static readonly Color LyingMirroredColor = new Color(1f, 0.5f, 0f, 0.3f);

    [Tooltip("누워 있을 때의 BoxCollider Center. 오른쪽을 볼 때 기준이며 왼쪽을 보면 x가 뒤집힌다. 이 오브젝트의 Transform 스케일이 곱해진다.")]
    [SerializeField] private Vector3 lyingCenter;

    [Tooltip("누워 있을 때의 BoxCollider Size. 이 오브젝트의 Transform 스케일이 곱해진다.")]
    [SerializeField] private Vector3 lyingSize = Vector3.one;

    BoxCollider box;
    StateMachine stateMachine;
    MovementCore movement;

    /// <summary>서 있을 때의 콜라이더 값. 씬에 놓인 그대로다.</summary>
    Vector3 standingCenter;
    Vector3 standingSize;

    /// <summary>마지막으로 반영한 상태. 바뀐 프레임에만 값을 쓰기 위해 기억한다.</summary>
    bool appliedDown;
    bool appliedFacingRight = true;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        standingCenter = box.center;
        standingSize = box.size;

        // StateMachine·MovementCore는 LogicCores 같은 형제 가지에 있다. SpriteFacing과 같은 방식으로 찾는다.
        Character character = GetComponentInParent<Character>();
        if (character != null)
        {
            stateMachine = character.GetComponentInChildren<StateMachine>();
            movement = character.GetComponentInChildren<MovementCore>();
        }

        if (stateMachine == null || movement == null)
        {
            Debug.LogWarning($"{name}: StateMachine 또는 MovementCore를 찾지 못해 피격 박스를 바꿀 수 없습니다.", this);
            enabled = false;
        }
    }

    // StateMachine이 Update에서 상태를 정한 뒤에 반영한다. 같은 프레임 안에서 한 박자 늦는 걸 막는다.
    void LateUpdate()
    {
        bool down = stateMachine.IsDown;
        bool facingRight = movement.FacingDir.x > 0f;

        // 선 모양은 방향과 무관하므로, 서 있을 때는 방향이 바뀌어도 다시 쓸 필요가 없다.
        if (down == appliedDown && (!down || facingRight == appliedFacingRight)) return;

        appliedDown = down;
        appliedFacingRight = facingRight;

        if (down)
        {
            Vector3 center = lyingCenter;
            if (!facingRight) center.x = -center.x;
            box.center = center;
            box.size = lyingSize;
        }
        else
        {
            box.center = standingCenter;
            box.size = standingSize;
        }
    }

    // 이 오브젝트를 선택했을 때 누운 박스를 그린다. 선 박스는 유니티가 콜라이더 기즈모(초록)로 이미 그려준다.
    // 인스펙터에서 값을 바꾸면 Play 없이도 바로 보이므로, 이걸 보면서 누운 모양을 맞추면 된다.
    void OnDrawGizmosSelected()
    {
        // BoxCollider의 center·size와 같은 공간(이 오브젝트의 로컬, 스케일 포함)에서 그려야 실제 판정과 겹친다.
        Gizmos.matrix = transform.localToWorldMatrix;

        Vector3 mirrored = lyingCenter;
        mirrored.x = -mirrored.x;

        // 왼쪽을 볼 때(흐리게)를 먼저 그려, 오른쪽을 볼 때(진하게)가 위에 보이게 한다.
        Gizmos.color = LyingMirroredColor;
        Gizmos.DrawWireCube(mirrored, lyingSize);

        Gizmos.color = LyingColor;
        Gizmos.DrawWireCube(lyingCenter, lyingSize);

        Gizmos.matrix = Matrix4x4.identity;
    }
}
