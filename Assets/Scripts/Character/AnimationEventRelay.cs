using UnityEngine;

/// <summary>
/// 애니메이션 클립에 박힌 Animation Event를 받아 부모의 담당 컴포넌트로 넘기기만 하는 다리.
///
/// Animation Event는 Animator가 붙은 <b>바로 그 오브젝트</b>의 컴포넌트에서만 함수를 찾는다.
/// 부모도 자식도 뒤지지 않는다. 그래서 이 스크립트는 반드시 Animator와 같은 오브젝트에 붙어야 한다.
///
/// [준비물] 이 오브젝트에 Animator, 계층 위쪽에 Character, 그 아래 어딘가에 AttackCore + MovementCore.
/// [클립 이벤트] 공격 타격 프레임 → OnAttackHitFrame
///              움직이며 계속 판정하는 구간의 시작·끝 → OnAttackHitStart / OnAttackHitEnd
///                (구간 동안 매 프레임 판정, 대상마다 한 번만. 끝이 없으면 공격 동작이 끝날 때 닫힌다.
///                 클립에 OnComboWindowOpen이 없으면 콤보 창은 구간이 열릴 때 열린다)
///              콤보 캔슬이 허용되는 프레임 → OnComboWindowOpen (선택. 없으면 타격 프레임에 콤보 창이 열린다)
///              점프 준비 클립의 끝 → OnJumpTakeoff (없으면 준비 동작에서 영영 뜨지 못한다)
///              Lunge Attack의 돌진 출발 → OnLungeStart (선택. 없으면 공격 시작 순간 출발한다)
///              Lunge Attack의 돌진 도착 → OnLungeEnd (선택. 없으면 Burst는 마찰로, Constant는 클립 끝에 멈춘다)
/// </summary>
public class AnimationEventRelay : MonoBehaviour
{
    AttackCore attack;
    MovementCore movement;

    void Awake()
    {
        // Core들은 LogicCores 같은 형제 가지에 있을 수 있다. GetComponentInParent는 형제를 보지
        // 못하므로, 공통 조상인 Character까지 올라간 다음 거기서 아래로 다시 찾는다.
        Character character = GetComponentInParent<Character>();
        if (character != null)
        {
            attack = character.GetComponentInChildren<AttackCore>();
            movement = character.GetComponentInChildren<MovementCore>();
        }

        if (attack == null)
            Debug.LogWarning($"{name}: AttackCore를 찾지 못해 애니메이션 이벤트를 전달할 수 없습니다.", this);

        if (movement == null)
            Debug.LogWarning($"{name}: MovementCore를 찾지 못해 점프 이벤트를 전달할 수 없습니다.", this);
    }

    /// <summary>
    /// 공격 클립의 타격 프레임에 거는 이벤트. 클립에 OnComboWindowOpen이 따로 없으면 콤보 창도 여기서 열린다.
    /// 이 함수 이름이 곧 Animation Event의 Function 값이 되므로 함부로 바꾸면 클립 쪽이 끊긴다.
    /// </summary>
    public void OnAttackHitFrame()
    {
        if (attack == null) return;

        attack.ExecuteHit();
        attack.OpenComboWindow(true);
    }

    /// <summary>
    /// 공격 클립에서 계속 판정하는 구간이 시작되는 프레임에 거는 이벤트. 이 프레임부터 매 프레임 판정한다.
    /// 클립에 OnComboWindowOpen이 따로 없으면 콤보 창도 여기서 열린다(OnAttackHitFrame과 같다).
    /// 이 함수 이름이 곧 Animation Event의 Function 값이 되므로 함부로 바꾸면 클립 쪽이 끊긴다.
    /// </summary>
    public void OnAttackHitStart()
    {
        if (attack == null) return;

        attack.OpenHitWindow();
        attack.OpenComboWindow(true);
    }

    /// <summary>
    /// 공격 클립에서 계속 판정하는 구간이 끝나는 프레임에 거는 이벤트.
    /// 이 함수 이름이 곧 Animation Event의 Function 값이 되므로 함부로 바꾸면 클립 쪽이 끊긴다.
    /// </summary>
    public void OnAttackHitEnd()
    {
        if (attack != null)
            attack.CloseHitWindow();
    }

    /// <summary>
    /// 공격 클립에서 다음 콤보로 끊고 넘어가도 되는 프레임에 거는 이벤트.
    /// 이 함수 이름이 곧 Animation Event의 Function 값이 되므로 함부로 바꾸면 클립 쪽이 끊긴다.
    /// </summary>
    public void OnComboWindowOpen()
    {
        if (attack != null)
            attack.OpenComboWindow(false);
    }

    /// <summary>
    /// 점프 준비 클립의 끝, 체공 동작이 시작되는 순간에 거는 이벤트. 여기서 실제로 뜬다.
    /// 이 함수 이름이 곧 Animation Event의 Function 값이 되므로 함부로 바꾸면 클립 쪽이 끊긴다.
    /// </summary>
    public void OnJumpTakeoff()
    {
        if (movement != null)
            movement.Jump();
    }

    /// <summary>
    /// Lunge Attack 클립에서 준비 자세가 끝나고 돌진을 출발하는 프레임에 거는 이벤트.
    /// 이 함수 이름이 곧 Animation Event의 Function 값이 되므로 함부로 바꾸면 클립 쪽이 끊긴다.
    /// </summary>
    public void OnLungeStart()
    {
        if (attack != null)
            attack.StartLunge();
    }

    /// <summary>
    /// Lunge Attack 클립에서 돌진이 끝나고 도착 자세가 시작되는 프레임에 거는 이벤트.
    /// 이 함수 이름이 곧 Animation Event의 Function 값이 되므로 함부로 바꾸면 클립 쪽이 끊긴다.
    /// </summary>
    public void OnLungeEnd()
    {
        if (attack != null)
            attack.EndLunge();
    }
}