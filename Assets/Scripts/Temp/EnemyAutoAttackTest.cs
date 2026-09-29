using UnityEngine;

/// <summary>
/// [테스트용] 일정 시간마다 제자리에서 같은 공격을 반복하는 컴포넌트.
///
/// 맞는 순간 상대의 콤보가 초기화되는지 확인하려고 만든 임시 스크립트다.
/// 확인이 끝나면 Temp 폴더째 지우면 된다.
///
/// 쓸 공격은 CharacterSettings가 아니라 자기 칸에서 꺼낸다 — Player와 Enemy가 셋팅 에셋 하나를
/// 공유하고 있어서, 셋팅의 공격 칸을 건드리면 플레이어의 공격까지 같이 바뀌기 때문이다.
///
/// 공격을 시작하는 순간 target이 있는 좌/우로 돌아선다. 휘두르는 도중에는 따라 돌지 않는다.
///
/// [준비물] 이 오브젝트 또는 그 자식에 AttackCore + StateMachine + MovementCore.
///          Inspector의 target에 맞을 대상(Player).
///          애니메이터에 skill의 animatorStateName과 같은 이름의 상태·클립,
///          그리고 그 클립의 타격 프레임에 OnAttackHitFrame 이벤트.
/// </summary>
public class EnemyAutoAttackTest : MonoBehaviour
{
    [Tooltip("공격 사이에 쉬는 시간(초). 공격 동작이 끝난 시점부터 센다.")]
    [SerializeField] private float interval = 3f;

    [Tooltip("반복할 공격. 이 스킬의 상태 이름이 애니메이터에 있어야 한다.")]
    [SerializeField] private SkillData skill;

    [Tooltip("공격할 때 바라볼 대상. 보통 Player를 넣는다.")]
    [SerializeField] private Transform target;

    AttackCore attack;
    StateMachine state;
    MovementCore movement;

    /// <summary>다음 공격까지 남은 시간.</summary>
    float timer;

    void Awake()
    {
        // Core들은 LogicCores 같은 자식으로 내려가 있다. PlayerMoveInput과 같은 방식으로 찾는다.
        attack = GetComponentInChildren<AttackCore>();
        state = GetComponentInChildren<StateMachine>();
        movement = GetComponentInChildren<MovementCore>();

        // UnityEngine.Object는 파괴된 객체가 '가짜 null'로 잡히므로 ?. 나 ?? 를 쓰지 않고 == null 로 비교한다.
        if (attack == null || state == null || movement == null || skill == null || target == null)
        {
            Debug.LogError($"{name}: AttackCore / StateMachine / MovementCore / 공격 스킬 / target 중 일부를 찾지 못했습니다. 자동 공격을 끕니다.", this);
            enabled = false;
            return;
        }

        // 시작과 동시에 때리면 콤보를 시작할 틈이 없다. 첫 공격도 간격만큼 기다린다.
        timer = interval;
    }

    void Update()
    {
        // 공격 동작 중이거나 경직 중이면 시간도 세지 않는다. interval은 '쉬는 시간'이라는 뜻이다.
        if (!state.CanAct) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        timer = interval;

        // 판정·넉백·스프라이트가 전부 FacingDir을 따르므로 공격 직전에 방향만 맞춰두면 된다.
        movement.FaceTowards(target.position.x);
        attack.TryAttack(skill);
    }
}
