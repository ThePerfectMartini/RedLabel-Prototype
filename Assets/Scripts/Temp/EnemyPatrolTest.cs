using UnityEngine;

/// <summary>
/// [테스트용] 시작 위치를 중심으로 좌우 distance(m)만큼 걸어서 왕복하는 컴포넌트.
///
/// 적의 이동을 확인하려고 만든 임시 스크립트다. 확인이 끝나면 Temp 폴더째 지우면 된다.
///
/// MovementCore에 좌우 입력만 넣어준다. 방향 전환·걷기 애니메이션·맵 경계는 기존 시스템이 맡는다.
/// 공격 중이거나 경직 중엔 입력을 넣지 않는다 — SetMoveInput을 부르면 Lunge·넉백 속도가 지워진다.
///
/// [준비물] 이 오브젝트 또는 그 자식에 MovementCore + StateMachine.
/// </summary>
public class EnemyPatrolTest : MonoBehaviour
{
    [Tooltip("시작 위치에서 좌우로 각각 몇 m까지 갈지.")]
    [SerializeField] private float distance = 3f;

    MovementCore movement;
    StateMachine state;

    /// <summary>순찰 범위의 중심. Awake 때의 X 좌표다.</summary>
    float startX;

    /// <summary>지금 걷는 방향. 1이면 오른쪽, -1이면 왼쪽.</summary>
    float moveDir = 1f;

    void Awake()
    {
        // Core들은 LogicCores 같은 자식으로 내려가 있다. PlayerMoveInput과 같은 방식으로 찾는다.
        movement = GetComponentInChildren<MovementCore>();
        state = GetComponentInChildren<StateMachine>();

        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        if (movement == null || state == null)
        {
            Debug.LogError($"{name}: MovementCore / StateMachine 중 일부를 찾지 못했습니다. 순찰을 끕니다.", this);
            enabled = false;
            return;
        }

        startX = transform.position.x;
    }

    void Update()
    {
        if (!state.CanAct) return;

        // 범위 끝을 넘으면 돌아선다. 넉백으로 밖까지 밀려나도 범위 쪽을 향하게 된다.
        float x = transform.position.x;
        if (x >= startX + distance) moveDir = -1f;
        else if (x <= startX - distance) moveDir = 1f;

        movement.SetMoveInput(new Vector2(moveDir, 0f));
    }
}
